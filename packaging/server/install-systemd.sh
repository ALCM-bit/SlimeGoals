#!/usr/bin/env bash
# Instala na VM (como root) as unidades do systemd e o sysctl do SlimeGoals na tailnet.
# Uso: sudo packaging/server/install-systemd.sh
# Recusa se TAILNET_IP (um IPv4 de 100.64.0.0/10) ou SLIMEGOALS_DOMAIN faltarem no .env da raiz do repositório (lido
# linha a linha, sem source). SYSTEMCTL_BIN, SYSCTL_BIN, ID_BIN, UNIT_DIR e SYSCTL_DIR existem só para o teste.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
src="$root/packaging/server/systemd"
env_file="$root/.env"
systemctl_bin="${SYSTEMCTL_BIN:-systemctl}"
sysctl_bin="${SYSCTL_BIN:-sysctl}"
id_bin="${ID_BIN:-id}"
unit_dir="${UNIT_DIR:-/etc/systemd/system}"
sysctl_dir="${SYSCTL_DIR:-/etc/sysctl.d}"

# shellcheck source=packaging/server/lib/env.sh
. "$root/packaging/server/lib/env.sh"
# shellcheck source=packaging/server/lib/cert-dir.sh
. "$root/packaging/server/lib/cert-dir.sh"
# shellcheck source=packaging/server/lib/tailnet-ip.sh
. "$root/packaging/server/lib/tailnet-ip.sh"

[ "$("$id_bin" -u)" -eq 0 ] || { echo "Rode como root (sudo)." >&2; exit 1; }
[ -f "$env_file" ] || { echo "Não achei o .env em $root. Rode packaging/server/init-env.sh e preencha a seção da tailnet." >&2; exit 1; }
# O caminho entra no ExecStart e no sed: espaço, |, & ou \ quebrariam as unidades.
[[ "$root" =~ ^/[A-Za-z0-9/._-]*$ ]] || { echo "O caminho do repositório ($root) tem caracteres fora de A-Z a-z 0-9 / . _ -; mova o repositório." >&2; exit 1; }
cert_dir="$(env_value TAILNET_CERT_DIR "$env_file")"
if [ -n "$cert_dir" ]; then
  if ! msg="$(cert_dir_error "$cert_dir")"; then echo "$msg" >&2; exit 1; fi
  if ! msg="$(cert_dir_contents_error "$cert_dir")"; then echo "$msg" >&2; exit 1; fi
fi
for name in TAILNET_IP SLIMEGOALS_DOMAIN; do
  [ -n "$(env_value "$name" "$env_file")" ] || { echo "$name falta (ou está vazio) no .env. Preencha antes de instalar." >&2; exit 1; }
done
if ! msg="$(tailnet_ip_error "$(env_value TAILNET_IP "$env_file")")"; then echo "$msg" >&2; exit 1; fi

for unit in slimegoals-tls.service slimegoals-tls.timer slimegoals-firewall.service \
  slimegoals-backup.service slimegoals-backup.timer; do
  sed "s|@ROOT@|$root|g" "$src/$unit" > "$unit_dir/$unit"
  chmod 644 "$unit_dir/$unit"
done
install -m 644 "$src/60-slimegoals.conf" "$sysctl_dir/60-slimegoals.conf"
"$sysctl_bin" --system > /dev/null

"$systemctl_bin" daemon-reload
"$systemctl_bin" enable --now slimegoals-tls.timer slimegoals-firewall.service slimegoals-backup.timer
# O serviço do certificado também sobe com o docker.service, na partida da VM.
"$systemctl_bin" enable slimegoals-tls.service
"$systemctl_bin" start slimegoals-tls.service
echo "Instalado: certificado por timer, sysctl, regra de firewall e backup diário (/var/backups/slimegoals)."
