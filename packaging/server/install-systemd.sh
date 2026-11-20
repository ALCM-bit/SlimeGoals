#!/usr/bin/env bash
# Instala na VM (como root) as unidades do systemd e o sysctl do SlimeGoals na tailnet.
# Uso: sudo packaging/server/install-systemd.sh
# Recusa se TAILNET_IP ou SLIMEGOALS_DOMAIN faltarem no .env da raiz do repositório (lido linha a linha, sem source).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
src="$root/packaging/server/systemd"
env_file="$root/.env"

[ "$(id -u)" -eq 0 ] || { echo "Rode como root (sudo)." >&2; exit 1; }
[ -f "$env_file" ] || { echo "Não achei o .env em $root. Rode packaging/server/init-env.sh e preencha a seção da tailnet." >&2; exit 1; }
for name in TAILNET_IP SLIMEGOALS_DOMAIN; do
  grep -Eq "^$name=.+" "$env_file" || { echo "$name falta no .env. Preencha antes de instalar." >&2; exit 1; }
done

for unit in slimegoals-tls.service slimegoals-tls.timer slimegoals-firewall.service; do
  sed "s|@ROOT@|$root|g" "$src/$unit" > "/etc/systemd/system/$unit"
  chmod 644 "/etc/systemd/system/$unit"
done
install -m 644 "$src/60-slimegoals.conf" /etc/sysctl.d/60-slimegoals.conf
sysctl --system > /dev/null

systemctl daemon-reload
systemctl enable --now slimegoals-tls.timer slimegoals-firewall.service
systemctl start slimegoals-tls.service
echo "Instalado: certificado por timer, sysctl e regra de firewall."
