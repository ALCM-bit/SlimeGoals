#!/usr/bin/env bash
# Validação do TAILNET_CERT_DIR, para ser lida com `.` pelo tailnet-cert.sh e pelo install-systemd.sh.
# Roda como root: só aceita caminho absoluto de caracteres seguros, sem "..", sem componente vazio, com pelo menos dois
# componentes e fora da lista de pastas do sistema, para nunca gravar nem apagar nada em /etc, /usr e afins.
# cert_dir_error CAMINHO: imprime o motivo e devolve 1 se o caminho não serve; não imprime nada e devolve 0 se serve.

cert_dir_error() {
  local dir="$1" comp
  if ! [[ "$dir" =~ ^/[A-Za-z0-9/._-]*$ ]]; then
    echo "TAILNET_CERT_DIR deve ser um caminho absoluto só com letras, números e /._-."
    return 1
  fi
  dir="${dir%/}"
  case "$dir/" in
    *//* | */../* | */./*) echo "TAILNET_CERT_DIR não pode ter componentes vazios, '.' ou '..'."; return 1 ;;
  esac
  if ! [[ "$dir" =~ ^/[^/]+/[^/]+ ]]; then
    echo "TAILNET_CERT_DIR precisa de pelo menos dois componentes (por exemplo /etc/slimegoals/tls)."
    return 1
  fi
  for comp in / /etc /usr /var /root /home /boot /opt /srv /tmp /bin /sbin /lib /run /proc /sys /dev; do
    if [ "$dir" = "$comp" ]; then
      echo "TAILNET_CERT_DIR não pode ser uma pasta do sistema ($comp)."
      return 1
    fi
  done
  return 0
}
