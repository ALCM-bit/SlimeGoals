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
# A pasta dos certificados não é criada aqui: quem cria (com 700) é o script.
setup() {
  local name="$1"
  work="$tmp/$name"
  mkdir -p "$work/root/packaging/server/lib" "$work/bin"
  cp "$src/tailnet-cert.sh" "$work/root/packaging/server/tailnet-cert.sh"
  cp "$src/lib/env.sh" "$work/root/packaging/server/lib/env.sh"
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
  # FAKE_CADDY_UP=0: Caddy parado. FAKE_PS_FAIL=1: o ps falha. FAKE_EXEC_FAIL=1: o reload falha.
  cat > "$work/bin/docker" <<'FAKE'
#!/usr/bin/env bash
echo "docker $*" >> "$FAKE_LOG"
case " $* " in
  *" ps "*)
    [ "${FAKE_PS_FAIL:-0}" = 1 ] && exit 1
    [ "${FAKE_CADDY_UP:-1}" = 1 ] && echo "abc123"
    exit 0 ;;
  *" exec "*) [ "${FAKE_EXEC_FAIL:-0}" = 1 ] && exit 1 ;;
esac
exit 0
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
sobras() { find "$work/certs" -mindepth 1 -not -name 'tailnet.*' -not -name '.recarregar'; }

export FAKE_CERT="CERT-FALSO-1" FAKE_KEY="CHAVE-SECRETA-FALSA-1"

# 1
setup first_run_writes_files_600_and_reloads
run || fail "first_run: o script devia sair 0"
[ "$(cat "$work/certs/tailnet.crt" 2>/dev/null)" = "$FAKE_CERT" ] || fail "first_run: tailnet.crt ausente ou errado"
[ "$(cat "$work/certs/tailnet.key" 2>/dev/null)" = "$FAKE_KEY" ] || fail "first_run: tailnet.key ausente ou errado"
[ "$(mode "$work/certs/tailnet.crt")" = 600 ] || fail "first_run: tailnet.crt deve ser 600"
[ "$(mode "$work/certs/tailnet.key")" = 600 ] || fail "first_run: tailnet.key deve ser 600"
[ "$(mode "$work/certs")" = 700 ] || fail "first_run: a pasta deve ser 700"
[ "$(reloads)" = 1 ] || fail "first_run: esperava 1 reload, houve $(reloads)"
grep -q 'caddy reload --config /etc/caddy/Caddyfile --force' "$work/log" || fail "first_run: o reload deve usar --force"
[ ! -e "$work/certs/.recarregar" ] || fail "first_run: o marcador devia sumir depois do reload"
[ ! -e "$work/injetado" ] || fail "first_run: o .env foi executado (source)"
[ -z "$(sobras)" ] || fail "first_run: sobrou temporário na pasta"

# 2
setup same_cert_does_not_reload
run || fail "same_cert: 1ª execução"
: > "$work/log"
run || fail "same_cert: 2ª execução devia sair 0"
[ "$(reloads)" = 0 ] || fail "same_cert: não devia recarregar"
! grep -q '^docker' "$work/log" || fail "same_cert: não devia chamar o docker"
[ -z "$(sobras)" ] || fail "same_cert: sobrou temporário"

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
[ -z "$(sobras)" ] || fail "cert_failure: sobrou temporário"

# 4b: tailscale sai 0 mas grava arquivos vazios
setup empty_files_keep_current
run || fail "empty_files: preparo"
cp "$work/certs/tailnet.crt" "$work/crt.antes"; cp "$work/certs/tailnet.key" "$work/key.antes"
: > "$work/log"
if FAKE_CERT="" FAKE_KEY="" run; then fail "empty_files: devia sair diferente de zero"; fi
cmp -s "$work/crt.antes" "$work/certs/tailnet.crt" || fail "empty_files: crt mudou"
cmp -s "$work/key.antes" "$work/certs/tailnet.key" || fail "empty_files: key mudou"
[ "$(reloads)" = 0 ] || fail "empty_files: não devia recarregar"
[ -z "$(sobras)" ] || fail "empty_files: sobrou temporário"

# 5
setup key_is_never_printed
run || fail "key_printed: execução"
if grep -q "$FAKE_KEY" "$out"; then fail "key_printed: a chave apareceu na saída"; fi
FAKE_CERT="CERT-FALSO-2" FAKE_KEY="CHAVE-SECRETA-FALSA-2" run || fail "key_printed: renovação devia sair 0"
if grep -q "CHAVE-SECRETA-FALSA" "$out"; then fail "key_printed: a chave apareceu na renovação"; fi
FAKE_FAIL=1 run && fail "key_printed: a falha devia sair diferente de zero"
if grep -q "CHAVE-SECRETA-FALSA" "$out"; then fail "key_printed: a chave apareceu na falha"; fi

# 6: Caddy parado -> troca os arquivos, não executa, deixa o marcador; na próxima com o Caddy de pé, recarrega
setup caddy_down_keeps_marker_then_reloads
FAKE_CADDY_UP=0 run || fail "caddy_down: devia sair 0"
[ "$(cat "$work/certs/tailnet.crt")" = "$FAKE_CERT" ] || fail "caddy_down: os arquivos deviam ser trocados"
[ "$(reloads)" = 0 ] || fail "caddy_down: não devia executar reload"
[ -e "$work/certs/.recarregar" ] || fail "caddy_down: o marcador devia ficar"
[ "$(mode "$work/certs/.recarregar")" = 600 ] || fail "caddy_down: o marcador deve ser 600"
: > "$work/log"
run || fail "caddy_down: 2ª execução devia sair 0"
[ "$(reloads)" = 1 ] || fail "caddy_down: a 2ª execução devia recarregar (sem mudança de certificado)"
[ ! -e "$work/certs/.recarregar" ] || fail "caddy_down: o marcador devia sumir depois do reload"

# 7: reload falha -> saída diferente de zero, marcador fica, a próxima recarrega
setup exec_failure_keeps_marker
if FAKE_EXEC_FAIL=1 run; then fail "exec_failure: devia sair diferente de zero"; fi
[ -e "$work/certs/.recarregar" ] || fail "exec_failure: o marcador devia ficar"
: > "$work/log"
run || fail "exec_failure: 2ª execução devia sair 0"
[ "$(reloads)" = 1 ] || fail "exec_failure: a 2ª execução devia recarregar"
[ ! -e "$work/certs/.recarregar" ] || fail "exec_failure: o marcador devia sumir"

# 8: docker compose ps falha -> não é "Caddy parado": erro e marcador
setup ps_failure_is_an_error
if FAKE_PS_FAIL=1 run; then fail "ps_failure: devia sair diferente de zero"; fi
[ -e "$work/certs/.recarregar" ] || fail "ps_failure: o marcador devia ficar"
[ "$(reloads)" = 0 ] || fail "ps_failure: não devia executar reload"

# 9: .env com CRLF, espaços e comentário na linha resolve a mesma pasta
setup env_variants_resolve_same_dir
printf 'SLIMEGOALS_DOMAIN="servidor.exemplo.ts.net"  \r\nTAILNET_CERT_DIR=%s  # comentário\r\n' "$work/certs" > "$work/root/.env"
run || fail "env_variants: devia sair 0"
[ -f "$work/certs/tailnet.crt" ] || fail "env_variants: a pasta não foi resolvida"
grep -q 'servidor.exemplo.ts.net$' "$work/log" || fail "env_variants: o domínio chegou errado ao tailscale"

# 10: pastas recusadas, sem chamar o tailscale
refused() { # nome valor
  setup "refused_$1"
  printf 'SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net\nTAILNET_CERT_DIR=%s\n' "$2" > "$work/root/.env"
  if run; then fail "refused_$1: devia recusar"; fi
  [ ! -s "$work/log" ] || fail "refused_$1: não devia chamar o tailscale"
}
refused relative "certs/relativo"
refused dotdot "$tmp/a/../b"
refused unsafe_chars "$tmp/com espaco"
if [ "$(id -u)" != 0 ]; then
  refused foreign_owner "/usr"   # só testa a recusa: nada é escrito lá
  refused foreign_owner_root "/"
fi
setup group_writable
mkdir -m 775 "$work/certs"
if run; then fail "group_writable: devia recusar pasta com escrita de grupo"; fi
[ ! -s "$work/log" ] || fail "group_writable: não devia chamar o tailscale"

# 11: temporários velhos somem
setup stale_tmp_removed
mkdir -m 700 "$work/certs"; mkdir "$work/certs/.novo.velho"
run || fail "stale: devia sair 0"
[ ! -e "$work/certs/.novo.velho" ] || fail "stale: o temporário velho devia ser apagado"

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: tailnet-cert.sh confere."
