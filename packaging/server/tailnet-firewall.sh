#!/usr/bin/env bash
# Garante uma única regra em DOCKER-USER: a porta 443 publicada no IP da tailnet só aceita conexões vindas do tailscale0.
# A regra de NAT do Docker olha o destino, não a interface de entrada; sem esta regra, um aparelho da rede de casa com rota
# manual para o 100.x chegaria ao Caddy. Roda como root, pelo slimegoals-firewall.service (a cada partida do Docker).
# TAILNET_IP vem do .env da raiz do repositório, lido linha a linha (nunca com source). IPTABLES_BIN é para teste.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
root="$(cd "$here/../.." && pwd -P)"
env_file="$root/.env"
iptables_bin="${IPTABLES_BIN:-iptables}"

# shellcheck source=packaging/server/lib/env.sh
. "$here/lib/env.sh"

[ -f "$env_file" ] || { echo "tailnet-firewall: não achei o .env da raiz do repositório." >&2; exit 1; }
ip="$(env_value TAILNET_IP "$env_file")"
[ -n "$ip" ] || { echo "tailnet-firewall: TAILNET_IP falta no .env." >&2; exit 1; }
[[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || { echo "tailnet-firewall: TAILNET_IP não parece um IPv4." >&2; exit 1; }

# --ctdir ORIGINAL: --ctorigdst olha a tupla original da conexão, que nos pacotes de resposta (o SYN-ACK do Caddy
# voltando ao aparelho, que entra por br-*, não por tailscale0) é a mesma. Sem limitar ao sentido original, a resposta
# também cairia na regra e a conexão nunca fecharia.
rule=(DOCKER-USER -p tcp -m conntrack --ctdir ORIGINAL --ctorigdst "$ip" --ctorigdstport 443 ! -i tailscale0 -j DROP)
if "$iptables_bin" -C "${rule[@]}" 2>/dev/null; then
  echo "tailnet-firewall: a regra já existe."
else
  "$iptables_bin" -I "${rule[@]}"
  echo "tailnet-firewall: regra inserida em DOCKER-USER."
fi
