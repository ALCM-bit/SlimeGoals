#!/bin/sh
# Cria slimegoals_owner e slimegoals_app com o roles.sql. A imagem do PostgreSQL roda este script uma vez,
# como o superusuário, quando cria o volume (/docker-entrypoint-initdb.d). As senhas vêm de /run/secrets
# pelo stdin do psql (\set com crase), nunca pela linha de comando, que qualquer processo vê.
# O roles.sql entra pelo \i, não por um cat no pipe: sem pipefail no sh, um cat que falhasse deixaria o psql
# sair com 0 e o volume ficaria sem papéis, sem que este script rodasse de novo. Com ON_ERROR_STOP, o \i falha.
# Um CREATE ROLE ... PASSWORD que falhe não vai para o log do banco (log_min_error_statement) nem tem o texto
# repetido na saída do psql (VERBOSITY terse), que é o log do contêiner.
set -eu

{
  printf '%s\n' 'SET log_min_error_statement = panic;'
  printf '%s\n' '\set VERBOSITY terse'
  printf '%s\n' '\set owner_password `cat /run/secrets/owner_password`'
  printf '%s\n' '\set app_password `cat /run/secrets/app_password`'
  printf '%s\n' '\i /etc/slimegoals/roles.sql'
} | psql -v ON_ERROR_STOP=1 --single-transaction --no-psqlrc \
  --username "${POSTGRES_USER:-postgres}" --dbname "$POSTGRES_DB" \
  -v owner_role=slimegoals_owner -v app_role=slimegoals_app -v db_name="$POSTGRES_DB"
