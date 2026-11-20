#!/usr/bin/env bash
# Testa o tailnet-cert.sh com binários falsos (tailscale e docker) e pastas temporárias. Nada de rede, Docker nem tailnet reais.
# Uso: bash packaging/server/tests/tailnet-cert.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

# Monta uma raiz de repositório falsa com .env, o script copiado e os binários falsos.
setup() {
  local name="$1"
  work="$tmp/$name"
  mkdir -p "$work/root/packaging/server" "$work/bin" "$work/certs"
  cp "$src/tailnet-cert.sh" "$work/root/packaging/server/tailnet-cert.sh" 2>/dev/null || true
  # O .env é lido por linha, nunca com source: o '$(touch ...)' abaixo provaria o contrário.
  cat > "$work/root/.env" <<ENV
OUTRA=valor
SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net
TAILNET_CERT_DIR=$work/certs
INJECAO=\$(touch $work/injetado)
ENV
  cat > "$work/bin/tailscale" <<'FAKE'
#!/usr/bin/env bash
echo "tailscale $*" >> "$FAKE_LOG"
[ "${FAKE_FAIL:-0}" = 1 ] && { echo "erro falso do tailscale" >&2; exit 1; }
while [ $# -gt 0 ]; do
  case "$1" in
    --cert-file) printf '%s' "$FAKE_CERT" > "$2"; shift ;;
    --key-file) printf '%s' "$FAKE_KEY" > "$2"; shift ;;
  esac
  shift
done
FAKE
  cat > "$work/bin/docker" <<'FAKE'
#!/usr/bin/env bash
echo "docker $*" >> "$FAKE_LOG"
case " $* " in *" ps "*) echo "abc123" ;; esac
FAKE
  chmod +x "$work/bin/tailscale" "$work/bin/docker"
  : > "$work/log"
  out="$work/out"
}

run() { # roda o script; saída (stdout e stderr) em $out
  FAKE_LOG="$work/log" TAILSCALE_BIN="$work/bin/tailscale" DOCKER_BIN="$work/bin/docker" \
    bash "$work/root/packaging/server/tailnet-cert.sh" > "$out" 2>&1
}
reloads() { grep -c ' exec ' "$work/log" || true; }
mode() { stat -c %a "$1"; }

export FAKE_CERT="CERT-FALSO-1" FAKE_KEY="CHAVE-SECRETA-FALSA-1"

# 1
setup first_run_writes_files_600_and_reloads
if run; then :; else fail "first_run: o script devia sair 0"; fi
[ "$(cat "$work/certs/tailnet.crt" 2>/dev/null)" = "$FAKE_CERT" ] || fail "first_run: tailnet.crt ausente ou errado"
[ "$(cat "$work/certs/tailnet.key" 2>/dev/null)" = "$FAKE_KEY" ] || fail "first_run: tailnet.key ausente ou errado"
[ "$(mode "$work/certs/tailnet.crt")" = 600 ] || fail "first_run: tailnet.crt deve ser 600"
[ "$(mode "$work/certs/tailnet.key")" = 600 ] || fail "first_run: tailnet.key deve ser 600"
[ "$(mode "$work/certs")" = 700 ] || fail "first_run: a pasta deve ser 700"
[ "$(reloads)" = 1 ] || fail "first_run: esperava 1 reload, houve $(reloads)"
grep -q 'caddy reload' "$work/log" || fail "first_run: faltou o caddy reload"
[ ! -e "$work/injetado" ] || fail "first_run: o .env foi executado (source)"
[ -z "$(find "$work/certs" -mindepth 1 -not -name 'tailnet.*')" ] || fail "first_run: sobrou temporário na pasta"

# 1b: a pasta não existe
setup creates_missing_dir
rmdir "$work/certs"
run || fail "missing_dir: o script devia sair 0"
[ "$(mode "$work/certs")" = 700 ] || fail "missing_dir: a pasta criada deve ser 700"

# 2
setup same_cert_does_not_reload
run || fail "same_cert: 1ª execução"
: > "$work/log"
run || fail "same_cert: 2ª execução devia sair 0"
[ "$(reloads)" = 0 ] || fail "same_cert: não devia recarregar"
! grep -q '^docker' "$work/log" || fail "same_cert: não devia chamar o docker"
[ -z "$(find "$work/certs" -mindepth 1 -not -name 'tailnet.*')" ] || fail "same_cert: sobrou temporário"

# 3
setup new_cert_replaces_and_reloads
run || fail "new_cert: 1ª execução"
: > "$work/log"
FAKE_CERT="CERT-FALSO-2" FAKE_KEY="CHAVE-SECRETA-FALSA-2" run || fail "new_cert: 2ª execução devia sair 0"
[ "$(cat "$work/certs/tailnet.crt")" = "CERT-FALSO-2" ] || fail "new_cert: crt não mudou"
[ "$(cat "$work/certs/tailnet.key")" = "CHAVE-SECRETA-FALSA-2" ] || fail "new_cert: key não mudou"
[ "$(mode "$work/certs/tailnet.key")" = 600 ] || fail "new_cert: key deve continuar 600"
[ "$(reloads)" = 1 ] || fail "new_cert: esperava 1 reload"

# 4
setup cert_failure_keeps_files_and_does_not_reload
run || fail "cert_failure: preparo"
cp "$work/certs/tailnet.crt" "$work/crt.antes"; cp "$work/certs/tailnet.key" "$work/key.antes"
: > "$work/log"
if FAKE_FAIL=1 run; then fail "cert_failure: devia sair diferente de zero"; fi
cmp -s "$work/crt.antes" "$work/certs/tailnet.crt" || fail "cert_failure: crt mudou"
cmp -s "$work/key.antes" "$work/certs/tailnet.key" || fail "cert_failure: key mudou"
[ "$(reloads)" = 0 ] || fail "cert_failure: não devia recarregar"
[ -z "$(find "$work/certs" -mindepth 1 -not -name 'tailnet.*')" ] || fail "cert_failure: sobrou temporário"

# 5
setup key_is_never_printed
run || fail "key_printed: execução"
if grep -q "$FAKE_KEY" "$out"; then fail "key_printed: a chave apareceu na saída"; fi
FAKE_CERT="CERT-FALSO-2" FAKE_KEY="CHAVE-SECRETA-FALSA-2" run
if grep -q "CHAVE-SECRETA-FALSA" "$out"; then fail "key_printed: a chave apareceu na renovação"; fi
FAKE_FAIL=1 run
if grep -q "CHAVE-SECRETA-FALSA" "$out"; then fail "key_printed: a chave apareceu na falha"; fi

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: tailnet-cert.sh confere."
