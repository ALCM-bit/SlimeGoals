#!/usr/bin/env bash
# Cria o .env do Docker Compose com segredos aleatórios, se ele ainda não existir.
# Uso: packaging/server/init-env.sh [arquivo]   (padrão: .env na raiz do repositório)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
target="${1:-$root/.env}"

if [ -e "$target" ]; then
  echo "$target já existe; nada foi mudado."
  exit 0
fi

command -v openssl >/dev/null || { echo "Preciso do openssl para gerar os segredos." >&2; exit 1; }

# Hexadecimal: sem ';', '=' nem aspas, que quebrariam a string de conexão do PostgreSQL.
secret() { openssl rand -hex 32; }

# Só o dono lê o arquivo, desde a criação.
umask 077
cat > "$target" <<ENV
# Gerado por packaging/server/init-env.sh. Fica fora do repositório.
POSTGRES_PASSWORD=$(secret)
SERVER_PRELOGIN_SECRET=$(secret)
ENV

echo "Criado $target com segredos novos. Suba o servidor com: docker compose up -d --build --wait"
