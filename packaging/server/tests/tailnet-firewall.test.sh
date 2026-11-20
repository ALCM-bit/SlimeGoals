#!/usr/bin/env bash
# Testa o tailnet-firewall.sh com um iptables falso. Nada de regra real.
# Uso: bash packaging/server/tests/tailnet-firewall.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

mkdir -p "$tmp/root/packaging/server/lib" "$tmp/bin"
cp "$src/tailnet-firewall.sh" "$tmp/root/packaging/server/"
cp "$src/lib/env.sh" "$tmp/root/packaging/server/lib/"
printf 'TAILNET_IP="100.64.0.10"\r\n' > "$tmp/root/.env"
cat > "$tmp/bin/iptables" <<'FAKE'
#!/usr/bin/env bash
echo "$*" >> "$FAKE_LOG"
if [ "$1" = -C ]; then [ "${FAKE_EXISTS:-0}" = 1 ]; exit; fi
FAKE
chmod +x "$tmp/bin/iptables"

run() { FAKE_LOG="$tmp/log" IPTABLES_BIN="$tmp/bin/iptables" bash "$tmp/root/packaging/server/tailnet-firewall.sh" > "$tmp/out" 2>&1; }
regra='DOCKER-USER -p tcp -m conntrack --ctdir ORIGINAL --ctorigdst 100.64.0.10 --ctorigdstport 443 ! -i tailscale0 -j DROP'

: > "$tmp/log"
run || fail "regra ausente: devia sair 0"
[ "$(cat "$tmp/log")" = "-C $regra
-I $regra" ] || fail "regra ausente: esperava -C e depois -I com a lista exata de argumentos"

: > "$tmp/log"
FAKE_EXISTS=1 run || fail "regra presente: devia sair 0"
[ "$(cat "$tmp/log")" = "-C $regra" ] || fail "regra presente: não devia inserir de novo"

printf 'TAILNET_IP=\n' > "$tmp/root/.env"
if run; then fail "IP vazio: devia recusar"; fi

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: tailnet-firewall.sh confere."
