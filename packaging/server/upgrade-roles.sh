#!/usr/bin/env bash
# Passa o banco do servidor de desenvolvimento que já existe para os papéis novos, sem apagar dados.
# No volume antigo, as tabelas são do superusuário slimegoals. Este script cria slimegoals_owner e
# slimegoals_app com o roles.sql, passa as tabelas e sequências de public (o schema_version incluso) para
# o dono e dá ao app o acesso mínimo. Rodar de novo só reafirma senhas, atributos e permissões.
# Uso, na raiz do repositório e depois do packaging/server/init-env.sh:
#   packaging/server/upgrade-roles.sh        (PGSUPERUSER=<papel> se o superusuário não for slimegoals)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
superuser="${PGSUPERUSER:-slimegoals}"
roles_sql="packaging/server/postgres/roles.sql"

for name in owner_password app_password ConnectionStrings__Postgres ConnectionStrings__PostgresMigrations; do
  [ -e "secrets/$name" ] || { echo "Falta secrets/$name: rode antes o packaging/server/init-env.sh." >&2; exit 1; }
done

# O banco não usa o domínio; o padrão só evita que o compose.yaml recuse um .env antigo, sem SLIMEGOALS_DOMAIN.
export SLIMEGOALS_DOMAIN="${SLIMEGOALS_DOMAIN:-localhost}"

# Recria o banco com a configuração nova, que monta as senhas em /run/secrets. Os dados ficam no volume.
docker compose up -d --wait db

# As senhas são lidas dentro do contêiner, pelo \set com crase: não passam pela linha de comando. Um
# CREATE/ALTER ROLE ... PASSWORD que falhe não vai para o log do banco (log_min_error_statement) nem tem o
# texto repetido na saída do psql (VERBOSITY terse).
{
  cat <<'SQL'
SET log_min_error_statement = panic;
\set VERBOSITY terse
\set owner_role slimegoals_owner
\set app_role slimegoals_app
\set db_name slimegoals
\set owner_password `cat /run/secrets/owner_password`
\set app_password `cat /run/secrets/app_password`
SELECT count(*) AS existing FROM pg_roles WHERE rolname IN (:'owner_role', :'app_role') \gset
SELECT :existing = 0 AS create_roles, :existing = 2 AS update_roles \gset
\if :create_roles
SQL
  cat "$roles_sql"
  printf '%s\n' '\elif :update_roles'
  # Os mesmos comandos, com ALTER ROLE no lugar de CREATE ROLE: reafirma senha e atributos.
  sed 's/^CREATE ROLE /ALTER ROLE /' "$roles_sql"
  cat <<'SQL'
\else
DO $$ BEGIN RAISE EXCEPTION 'Só um dos papéis slimegoals_owner e slimegoals_app existe; confira à mão.'; END $$;
\endif

-- Tabelas e sequências soltas de public passam para o dono. As sequências de coluna acompanham a tabela.
SELECT format('ALTER TABLE %s OWNER TO %I', c.oid::regclass, :'owner_role')
FROM pg_class c
WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r', 'p') \gexec
SELECT format('ALTER SEQUENCE %s OWNER TO %I', c.oid::regclass, :'owner_role')
FROM pg_class c
WHERE c.relnamespace = 'public'::regnamespace AND c.relkind = 'S'
  AND NOT EXISTS (SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype IN ('a', 'i')) \gexec

-- O que já existe recebe o mesmo acesso que o ALTER DEFAULT PRIVILEGES dá ao que for criado depois.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_role";
SQL
} | docker compose exec -T db psql -v ON_ERROR_STOP=1 --single-transaction --no-psqlrc --quiet \
  -U "$superuser" -d slimegoals

echo "Papéis prontos. Suba o servidor com: docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait"
