#!/bin/sh
# Cria slimegoals_owner e slimegoals_app com o roles.sql. A imagem do PostgreSQL roda este script uma vez,
# como o superusuário, quando cria o volume (/docker-entrypoint-initdb.d). As senhas vêm de /run/secrets
# pelo stdin do psql (\set com crase), nunca pela linha de comando, que qualquer processo vê.
set -eu

{
  printf '%s\n' '\set owner_password `cat /run/secrets/owner_password`'
  printf '%s\n' '\set app_password `cat /run/secrets/app_password`'
  cat /etc/slimegoals/roles.sql
} | psql -v ON_ERROR_STOP=1 --single-transaction --no-psqlrc \
  --username "${POSTGRES_USER:-postgres}" --dbname "$POSTGRES_DB" \
  -v owner_role=slimegoals_owner -v app_role=slimegoals_app -v db_name="$POSTGRES_DB"
