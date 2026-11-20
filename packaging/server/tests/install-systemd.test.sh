#!/usr/bin/env bash
# Testa o install-systemd.sh com systemctl, sysctl e id falsos e pastas de destino temporárias. Nada é instalado de verdade.
# Uso: bash packaging/server/tests/install-systemd.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

# Monta uma raiz de repositório falsa (em $2, padrão $work/root) com o script, as libs e as unidades reais.
setup() {
  local name="$1" base="${2:-}"
  work="$tmp/$name"
  root="${base:-$work/root}"
  mkdir -p "$root/packaging/server/lib" "$work/bin" "$work/units" "$work/sysctl.d"
  cp "$src/install-systemd.sh" "$root/packaging/server/"
  cp "$src"/lib/*.sh "$root/packaging/server/lib/"
  cp -r "$src/systemd" "$root/packaging/server/systemd"
  printf 'TAILNET_IP=100.64.0.10\nSLIMEGOALS_DOMAIN=servidor.exemplo.ts.net\nTAILNET_CERT_DIR=%s/certs\n' "$work" > "$root/.env"
  for bin in systemctl sysctl; do
    cat > "$work/bin/$bin" <<FAKE
#!/usr/bin/env bash
echo "$bin \$*" >> "\$FAKE_LOG"
FAKE
  done
  # FAKE_UID: o id falso devolve este número (0 = root).
  cat > "$work/bin/id" <<'FAKE'
#!/usr/bin/env bash
echo "${FAKE_UID:-0}"
FAKE
  chmod +x "$work/bin/"*
  : > "$work/log"
  out="$work/out"
}

run() { # roda o script da raiz atual; saída em $out
  FAKE_LOG="$work/log" SYSTEMCTL_BIN="$work/bin/systemctl" SYSCTL_BIN="$work/bin/sysctl" ID_BIN="$work/bin/id" \
    UNIT_DIR="$work/units" SYSCTL_DIR="$work/sysctl.d" bash "$root/packaging/server/install-systemd.sh" > "$out" 2>&1
}
nothing_installed() { [ -z "$(find "$work/units" "$work/sysctl.d" -type f)" ] && [ ! -s "$work/log" ]; }

# 1: .env válido -> unidades com @ROOT@ trocado e as chamadas esperadas
setup valid_env
run || fail "valid: devia sair 0 ($(cat "$out"))"
for unit in slimegoals-tls.service slimegoals-tls.timer slimegoals-firewall.service slimegoals-backup.service slimegoals-backup.timer; do
  [ -f "$work/units/$unit" ] || fail "valid: faltou $unit"
  grep -q '@ROOT@' "$work/units/$unit" && fail "valid: $unit ainda tem @ROOT@"
  [ "$(stat -c %a "$work/units/$unit")" = 644 ] || fail "valid: $unit devia ser 644"
done
grep -q "ExecStart=$root/packaging/server/tailnet-cert.sh" "$work/units/slimegoals-tls.service" || fail "valid: o ExecStart do certificado não aponta para a raiz"
[ -f "$work/sysctl.d/60-slimegoals.conf" ] || fail "valid: faltou o sysctl"
expected="sysctl --system
systemctl daemon-reload
systemctl enable --now slimegoals-tls.timer slimegoals-firewall.service slimegoals-backup.timer
systemctl enable slimegoals-tls.service
systemctl start slimegoals-tls.service"
[ "$(cat "$work/log")" = "$expected" ] || fail "valid: chamadas inesperadas ($(cat "$work/log"))"

# 2: TAILNET_IP ausente
setup missing_ip
printf 'SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net\n' > "$root/.env"
run && fail "missing_ip: devia recusar"
grep -q TAILNET_IP "$out" || fail "missing_ip: a mensagem devia citar TAILNET_IP"
nothing_installed || fail "missing_ip: não devia instalar nada"

# 3: TAILNET_IP inválido (fora da faixa, IPv6)
for bad in 192.168.1.10 100.128.0.1 fd7a:115c:a1e0::1; do
  setup "bad_ip_$bad"
  printf 'TAILNET_IP=%s\nSLIMEGOALS_DOMAIN=servidor.exemplo.ts.net\n' "$bad" > "$root/.env"
  run && fail "bad_ip: devia recusar $bad"
  nothing_installed || fail "bad_ip: não devia instalar nada com $bad"
done

# 4: SLIMEGOALS_DOMAIN ausente
setup missing_domain
printf 'TAILNET_IP=100.64.0.10\n' > "$root/.env"
run && fail "missing_domain: devia recusar"
nothing_installed || fail "missing_domain: não devia instalar nada"

# 5: TAILNET_CERT_DIR ruim
for bad in /etc /etc/ssl/slimegoals relativa "/opt/var-lib-tailscale/tls"; do
  setup "bad_cert_dir_$bad"
  printf 'TAILNET_IP=100.64.0.10\nSLIMEGOALS_DOMAIN=servidor.exemplo.ts.net\nTAILNET_CERT_DIR=%s\n' "$bad" > "$root/.env"
  run && fail "bad_cert_dir: devia recusar $bad"
  nothing_installed || fail "bad_cert_dir: não devia instalar nada com $bad"
done
setup foreign_cert_dir
mkdir -m 700 "$work/certs"; : > "$work/certs/id_rsa"
run && fail "foreign_cert_dir: devia recusar pasta com outro arquivo"
nothing_installed || fail "foreign_cert_dir: não devia instalar nada"

# 6: raiz do repositório com caminho inseguro
setup unsafe_root "$tmp/unsafe_root/com espaco"
run && fail "unsafe_root: devia recusar"
nothing_installed || fail "unsafe_root: não devia instalar nada"

# 7: sem root
setup not_root
FAKE_UID=1000 run && fail "not_root: devia recusar"
nothing_installed || fail "not_root: não devia instalar nada"

# 8: sem .env
setup no_env
rm "$root/.env"
run && fail "no_env: devia recusar"
nothing_installed || fail "no_env: não devia instalar nada"

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: install-systemd.sh confere."
