#!/usr/bin/env bash
# Instala na VM (como root) as unidades do systemd e o sysctl do SlimeGoals na tailnet.
# Uso: sudo packaging/server/install-systemd.sh
# Recusa se TAILNET_IP ou SLIMEGOALS_DOMAIN faltarem no .env da raiz do repositório (lido linha a linha, sem source).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
src="$root/packaging/server/systemd"
env_file="$root/.env"

# shellcheck source=packaging/server/lib/env.sh
. "$root/packaging/server/lib/env.sh"
# shellcheck source=packaging/server/lib/cert-dir.sh
. "$root/packaging/server/lib/cert-dir.sh"

[ "$(id -u)" -eq 0 ] || { echo "Rode como root (sudo)." >&2; exit 1; }
[ -f "$env_file" ] || { echo "Não achei o .env em $root. Rode packaging/server/init-env.sh e preencha a seção da tailnet." >&2; exit 1; }
# O caminho entra no ExecStart e no sed: espaço, |, & ou \ quebrariam as unidades.
[[ "$root" =~ ^/[A-Za-z0-9/._-]*$ ]] || { echo "O caminho do repositório ($root) tem caracteres fora de A-Z a-z 0-9 / . _ -; mova o repositório." >&2; exit 1; }
cert_dir="$(env_value TAILNET_CERT_DIR "$env_file")"
if [ -n "$cert_dir" ] && ! msg="$(cert_dir_error "$cert_dir")"; then echo "$msg" >&2; exit 1; fi
for name in TAILNET_IP SLIMEGOALS_DOMAIN; do
  [ -n "$(env_value "$name" "$env_file")" ] || { echo "$name falta (ou está vazio) no .env. Preencha antes de instalar." >&2; exit 1; }
done

for unit in slimegoals-tls.service slimegoals-tls.timer slimegoals-firewall.service \
  slimegoals-backup.service slimegoals-backup.timer; do
  sed "s|@ROOT@|$root|g" "$src/$unit" > "/etc/systemd/system/$unit"
  chmod 644 "/etc/systemd/system/$unit"
done
install -m 644 "$src/60-slimegoals.conf" /etc/sysctl.d/60-slimegoals.conf
sysctl --system > /dev/null

systemctl daemon-reload
systemctl enable --now slimegoals-tls.timer slimegoals-firewall.service slimegoals-backup.timer
# O serviço do certificado também sobe com o docker.service, na partida da VM.
systemctl enable slimegoals-tls.service
systemctl start slimegoals-tls.service
echo "Instalado: certificado por timer, sysctl, regra de firewall e backup diário (/var/backups/slimegoals)."
