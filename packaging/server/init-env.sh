#!/usr/bin/env bash
# Cria os segredos do Docker Compose em secrets/ e o .env, este só com o que não é segredo.
# Uso: packaging/server/init-env.sh [pasta]   (padrão: a raiz do repositório)
# Nada que já existe é sobrescrito: cada arquivo presente fica como está.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
base="${1:-$root}"
secrets="$base/secrets"
env_file="$base/.env"

command -v openssl >/dev/null || { echo "Preciso do openssl para gerar os segredos." >&2; exit 1; }

# Hexadecimal: sem ';', '=' nem aspas, que quebrariam a string de conexão do PostgreSQL.
hex() { openssl rand -hex "$1"; }

# Só o dono entra na pasta, desde a criação.
umask 077
mkdir -p "$secrets"
chmod 700 "$secrets"

# Grava o segredo se o arquivo ainda não existe, sem quebra de linha no fim. Fica em 644: o Compose sem
# Swarm monta o arquivo com o dono e a permissão do host, e o usuário do contêiner precisa ler. A pasta 700
# protege no host.
put() {
  local file="$secrets/$1"
  if [ -e "$file" ]; then
    echo "secrets/$1 já existe; ficou como estava."
    return
  fi
  printf '%s' "$2" > "$file"
  chmod 644 "$file"
}

# Lê o valor que ficou no arquivo, para as strings de conexão usarem a senha que vale.
get() { cat "$secrets/$1"; }

put postgres_password "$(hex 32)"
put owner_password "$(hex 32)"
put app_password "$(hex 32)"
connection() { printf 'Host=db;Port=5432;Database=slimegoals;Username=%s;Password=%s;GSS Encryption Mode=Disable' "$1" "$2"; }
put ConnectionStrings__PostgresMigrations "$(connection slimegoals_owner "$(get owner_password)")"
put ConnectionStrings__Postgres "$(connection slimegoals_app "$(get app_password)")"
put Server__PreloginSecret "$(hex 32)"
put Server__InviteCode "$(hex 16)"
put Email__Smtp__Password ""

if [ -e "$env_file" ]; then
  echo "$env_file já existe; ficou como estava."
  if grep -qE '^(POSTGRES_PASSWORD|SERVER_PRELOGIN_SECRET)=' "$env_file"; then
    echo "Esse .env é do formato antigo: troque o conteúdo pelas variáveis do .env.example (SLIMEGOALS_DOMAIN e SMTP)."
    echo "POSTGRES_PASSWORD e SERVER_PRELOGIN_SECRET não são mais lidos: os segredos agora ficam em secrets/."
  fi
else
  cat > "$env_file" <<'ENV'
# Gerado por packaging/server/init-env.sh. Só o que não é segredo: os segredos ficam em secrets/.
SLIMEGOALS_DOMAIN=localhost
SMTP_HOST=
SMTP_PORT=
SMTP_USERNAME=
SMTP_FROM=
# Na tailnet (compose.tailnet.yaml): o IP 100.x da VM e a pasta de tailnet.crt e tailnet.key.
# TAILNET_IP=
# TAILNET_CERT_DIR=/etc/slimegoals/tls
ENV
  echo "Criado $env_file."
fi

echo "Segredos em $secrets. O convite do cadastro está em secrets/Server__InviteCode."
echo "Preencha o SMTP no .env (a senha vai em secrets/Email__Smtp__Password) e suba com: docker compose up -d --build --wait"
