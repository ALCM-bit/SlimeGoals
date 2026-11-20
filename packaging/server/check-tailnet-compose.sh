#!/usr/bin/env bash
# Confere o compose.yaml + compose.tailnet.yaml sem segredos reais: só o Caddy publica porta, e só 100.64.0.10:443; os
# volumes do Caddy são exatamente os quatro esperados (o certificado entra somente leitura); nenhum serviço monta o
# tailscaled, /run, /var/run nem o docker.sock; nenhum usa rede do host, privileged, pid do host ou capacidade a mais.
# Uso: packaging/server/check-tailnet-compose.sh   (o CI roda este script; precisa de docker compose e jq)
# TAILNET_OVERLAY aponta para outro overlay, só para provar que a conferência reprova.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
overlay="${TAILNET_OVERLAY:-compose.tailnet.yaml}"

[ -f "$overlay" ] || { echo "FALHOU: $overlay não existe." >&2; exit 1; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Valores fictícios: nem o .env nem o secrets/ do repositório entram. O --env-file também impede o .env automático.
cat > "$tmp/env" <<'ENV'
TAILNET_IP=100.64.0.10
SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net
SMTP_HOST=smtp.exemplo.invalid
SMTP_PORT=587
SMTP_USERNAME=teste
SMTP_FROM=teste@exemplo.invalid
ENV

# Redefine os segredos do compose.yaml com arquivos fictícios (a chave é a mesma, só o caminho muda).
{
  echo "secrets:"
  for name in postgres_password owner_password app_password ConnectionStrings__Postgres \
    ConnectionStrings__PostgresMigrations Server__PreloginSecret Server__InviteCode Email__Smtp__Password; do
    printf 'x' > "$tmp/$name"
    printf '  %s:\n    file: %s\n' "$name" "$tmp/$name"
  done
} > "$tmp/secrets.yaml"

config="$(docker compose --env-file "$tmp/env" -f compose.yaml -f "$overlay" -f "$tmp/secrets.yaml" config --format json)"

fail=0
check() { # descrição, expressão jq que deve dar true
  if [ "$(jq -r "$2" <<< "$config")" != "true" ]; then
    echo "FALHOU: $1" >&2
    fail=1
  fi
}

check "o Caddy deve publicar exatamente uma porta, 100.64.0.10:443 -> 443" \
  '[.services.caddy.ports // [] | .[] | {host_ip, published, target}] == [{"host_ip":"100.64.0.10","published":"443","target":443}]'
check "nenhum serviço pode publicar em 0.0.0.0, em todas as interfaces ou na porta 80" \
  '[.services[] | .ports // [] | .[] | select((.host_ip // "") == "" or .host_ip == "0.0.0.0" or .host_ip == "::" or (.published | tostring) == "80" or .target == 80)] | length == 0'
check "só o Caddy pode ter portas publicadas" \
  '[.services | to_entries[] | select((.value.ports // []) | length > 0) | .key] == ["caddy"]'
check "nenhum serviço pode ter expose" \
  '[.services[] | select((.expose // []) | length > 0)] | length == 0'
check "o Caddy deve ter exatamente quatro volumes: Caddyfile.tailnet, certificado :ro, caddy_data e caddy_config" \
  '[.services.caddy.volumes[] | [.type, .target, (.read_only // false), (if .type == "bind" then (.source | sub("^.*/packaging/"; "packaging/")) else .source end)]] | sort
   == ([["bind","/etc/caddy/Caddyfile",true,"packaging/server/Caddyfile.tailnet"],
       ["bind","/etc/slimegoals/tls",true,"/etc/slimegoals/tls"],
       ["volume","/config",false,"caddy_config"],
       ["volume","/data",false,"caddy_data"]] | sort)'
check "nenhum serviço pode montar tailscale, /run, /var/run nem docker.sock" \
  '[.services[] | .volumes // [] | .[] | (.source // "") | select(contains("tailscale") or contains("docker.sock") or . == "/run" or . == "/var/run" or startswith("/run/") or startswith("/var/run/"))] | length == 0'
check "nenhum serviço pode usar rede do host, privileged, pid ou ipc do host, nem devices" \
  '[.services[] | select((.network_mode // "") == "host" or (.privileged // false) == true or (.pid // "") == "host" or (.ipc // "") == "host" or ((.devices // []) | length > 0))] | length == 0'
check "as capacidades a mais são só as do compose.yaml: db (CHOWN, DAC_OVERRIDE, FOWNER, SETGID, SETUID) e caddy (NET_BIND_SERVICE)" \
  '[.services | to_entries[] | select((.value.cap_add // []) | length > 0) | {(.key): (.value.cap_add | sort)}] | add
   == {"db": ["CHOWN","DAC_OVERRIDE","FOWNER","SETGID","SETUID"], "caddy": ["NET_BIND_SERVICE"]}'
check "o certificado deve estar montado somente leitura em /etc/slimegoals/tls" \
  '[.services.caddy.volumes[] | select(.target == "/etc/slimegoals/tls" and .read_only == true)] | length == 1'
check "nenhum volume pode conter tailscaled.sock" \
  '[.services[] | .volumes // [] | .[] | select((.source // "") + (.target // "") | contains("tailscaled.sock"))] | length == 0'
check "o Caddyfile montado deve ser o Caddyfile.tailnet" \
  '[.services.caddy.volumes[] | select(.target == "/etc/caddy/Caddyfile" and (.source | endswith("/packaging/server/Caddyfile.tailnet")))] | length == 1'

[ "$fail" -eq 0 ] || exit 1
echo "OK: o Compose da tailnet confere."
