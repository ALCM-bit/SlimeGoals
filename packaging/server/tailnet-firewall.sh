#!/usr/bin/env bash
# Garante uma única regra em DOCKER-USER: a porta 443 publicada no IP da tailnet só aceita conexões vindas do tailscale0.
# A regra de NAT do Docker olha o destino, não a interface de entrada; sem esta regra, um aparelho da rede de casa com rota
# manual para o 100.x chegaria ao Caddy. Roda como root, pelo slimegoals-firewall.service (a cada partida do Docker).
# TAILNET_IP vem do .env da raiz do repositório, lido linha a linha (nunca com source). IPTABLES_BIN é para teste.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
env_file="$root/.env"
iptables_bin="${IPTABLES_BIN:-iptables}"

[ -f "$env_file" ] || { echo "tailnet-firewall: não achei o .env da raiz do repositório." >&2; exit 1; }
ip="$(grep -E '^TAILNET_IP=' "$env_file" | tail -n 1 | cut -d= -f2- | tr -d "\"'" || true)"
[ -n "$ip" ] || { echo "tailnet-firewall: TAILNET_IP falta no .env." >&2; exit 1; }
[[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || { echo "tailnet-firewall: TAILNET_IP não parece um IPv4." >&2; exit 1; }

rule=(DOCKER-USER -p tcp -m conntrack --ctorigdst "$ip" --ctorigdstport 443 ! -i tailscale0 -j DROP)
if "$iptables_bin" -C "${rule[@]}" 2>/dev/null; then
  echo "tailnet-firewall: a regra já existe."
else
  "$iptables_bin" -I "${rule[@]}"
  echo "tailnet-firewall: regra inserida em DOCKER-USER."
fi
