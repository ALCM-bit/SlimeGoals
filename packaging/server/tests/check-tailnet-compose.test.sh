#!/usr/bin/env bash
# Teste de mutação do check-tailnet-compose.sh: o overlay de verdade passa e cada overlay adulterado, que acrescenta
# uma exposição que a conferência precisa pegar, é reprovado. Precisa de docker compose e jq (como o próprio check).
# Uso: bash packaging/server/tests/check-tailnet-compose.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
root="$(cd "$src/../.." && pwd -P)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

overlay_ok() { # overlay (caminho absoluto): o check deve passar
  TAILNET_OVERLAY="$1" bash "$src/check-tailnet-compose.sh" > "$tmp/out" 2>&1
}

overlay_ok "$root/compose.tailnet.yaml" || fail "o overlay de verdade devia passar ($(cat "$tmp/out"))"

# F1: COMPOSE_FILE no ambiente (o da VM) não pode mudar o que o check confere: o -f explícito vence.
COMPOSE_FILE=compose.yaml overlay_ok "$root/compose.tailnet.yaml" || fail "COMPOSE_FILE no ambiente não devia afetar o check ($(cat "$tmp/out"))"

# mutant NOME 'linhas'...: o overlay de verdade com as linhas acrescentadas no fim (a última chave do arquivo é a lista de
# volumes do Caddy, com itens em 6 espaços; o que vem depois entra sob services:, em 2 ou 4 espaços).
mutant() {
  local name="$1"; shift
  { cat "$root/compose.tailnet.yaml"; printf '%s\n' "$@"; } > "$tmp/$name.yaml"
  if overlay_ok "$tmp/$name.yaml"; then fail "$name: devia reprovar"; fi
  grep -q '^FALHOU' "$tmp/out" || fail "$name: devia reprovar pelas conferências, não por erro do compose ($(head -c 300 "$tmp/out"))"
}

mutant server_port_on_lan \
  '  server:' '    ports:' '      - "192.168.1.50:8080:8080"'
mutant caddy_mounts_tailscale_socket_dir \
  '      - /var/run/tailscale:/var/run/tailscale'
mutant caddy_network_host \
  '    network_mode: host' '    networks: !reset []'
mutant caddy_privileged \
  '    privileged: true'
mutant caddy_pid_host \
  '    pid: host'
mutant server_extra_cap \
  '  server:' '    cap_add:' '      - NET_ADMIN'
mutant caddy_extra_cap \
  '    cap_add: !override' '      - NET_BIND_SERVICE' '      - NET_ADMIN'
mutant server_mounts_docker_sock \
  '  server:' '    volumes:' '      - /var/run/docker.sock:/var/run/docker.sock'
mutant db_mounts_run_dir \
  '  db:' '    volumes:' '      - /run/user:/mnt/run'
mutant caddy_extra_volume \
  '      - /srv/dados:/mnt/dados'
mutant caddy_cert_dir_writable \
  '      - /etc/slimegoals/tls:/etc/slimegoals/tls'
mutant server_expose \
  '  server:' '    expose:' '      - "8080"'
mutant db_port_published \
  '  db:' '    ports:' '      - "100.64.0.10:5432:5432"'

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: o check-tailnet-compose.sh reprova os overlays adulterados."
