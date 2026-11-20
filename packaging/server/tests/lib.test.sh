#!/usr/bin/env bash
# Testa as funções de lib/ (validação de pasta e do IP da tailnet) sem rodar nenhum script de verdade.
# Uso: bash packaging/server/tests/lib.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
# shellcheck source=packaging/server/lib/cert-dir.sh
. "$src/lib/cert-dir.sh"
# shellcheck source=packaging/server/lib/env.sh
. "$src/lib/env.sh"
# shellcheck source=packaging/server/lib/tailnet-ip.sh
. "$src/lib/tailnet-ip.sh"

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

# F5: caminhos recusados
for bad in /opt/tailscale /var/lib/tailscale/certs /opt/meu-tailscaled/x /etc/ssl/slimegoals /etc/pki/slimegoals /etc/ssl /etc/pki/tls; do
  safe_dir_error TAILNET_CERT_DIR "$bad" > /dev/null && fail "path: devia recusar $bad"
done
for ok in /etc/slimegoals/tls /var/backups/slimegoals /opt/slimegoals/tls; do
  safe_dir_error TAILNET_CERT_DIR "$ok" > /dev/null || fail "path: devia aceitar $ok"
done

# F5: conteúdo da pasta do certificado
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir "$tmp/vazia" "$tmp/certa" "$tmp/outra" "$tmp/sub"
: > "$tmp/certa/tailnet.crt"; : > "$tmp/certa/tailnet.key"; : > "$tmp/certa/.recarregar"; : > "$tmp/certa/.novo.abc"
: > "$tmp/outra/tailnet.crt"; : > "$tmp/outra/id_rsa"
: > "$tmp/sub/tailnet.key"; mkdir "$tmp/sub/pasta"
cert_dir_contents_error "$tmp/vazia" > /dev/null || fail "conteúdo: pasta vazia devia servir"
cert_dir_contents_error "$tmp/certa" > /dev/null || fail "conteúdo: pasta só com os arquivos do certificado devia servir"
cert_dir_contents_error "$tmp/nao-existe" > /dev/null || fail "conteúdo: pasta inexistente devia servir"
cert_dir_contents_error "$tmp/outra" > /dev/null && fail "conteúdo: devia recusar pasta com outro arquivo"
cert_dir_contents_error "$tmp/sub" > /dev/null && fail "conteúdo: devia recusar pasta com subpasta"

# F6: IP da tailnet em 100.64.0.0/10
for ok in 100.64.0.10 100.64.0.0 100.100.100.100 100.127.255.255; do
  tailnet_ip_error "$ok" > /dev/null || fail "ip: devia aceitar $ok"
done
for bad in 100.128.0.1 100.63.255.255 192.168.1.10 10.0.0.1 127.0.0.1 0.0.0.0 "fd7a:115c:a1e0::1" "::1" "" "100.64.0" "100.64.0.256" "100.64.0.1.5" "100.064.0.10" "100.64.0.10 " "abc" "100.64.0.-1"; do
  tailnet_ip_error "$bad" > /dev/null && fail "ip: devia recusar '$bad'"
done

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: lib/ confere."
