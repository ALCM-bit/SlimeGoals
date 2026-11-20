#!/usr/bin/env bash
# Validação do IP da tailnet, para ser lida com `.` pelos scripts que recebem TAILNET_IP.
# tailnet_ip_error IP: imprime o motivo e devolve 1 se o IP não é um IPv4 da faixa da tailnet (100.64.0.0/10, ou seja,
# de 100.64.0.0 a 100.127.255.255); não imprime nada e devolve 0 se serve. IPv6 e qualquer outro IPv4 são recusados.

tailnet_ip_error() {
  local ip="$1" a b c d
  if ! [[ "$ip" =~ ^(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})$ ]]; then
    echo "TAILNET_IP deve ser um IPv4 da tailnet (100.64.0.0 a 100.127.255.255)."
    return 1
  fi
  a="${BASH_REMATCH[1]}" b="${BASH_REMATCH[2]}" c="${BASH_REMATCH[3]}" d="${BASH_REMATCH[4]}"
  if [ "$a" -ne 100 ] || [ "$b" -lt 64 ] || [ "$b" -gt 127 ] || [ "$c" -gt 255 ] || [ "$d" -gt 255 ]; then
    echo "TAILNET_IP deve ser um IPv4 da tailnet (100.64.0.0 a 100.127.255.255)."
    return 1
  fi
  return 0
}
