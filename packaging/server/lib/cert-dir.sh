#!/usr/bin/env bash
# Validação de pasta onde um script root vai gravar (TAILNET_CERT_DIR, pasta do backup), para ser lida com `.`
# pelo tailnet-cert.sh, pelo backup.sh e pelo install-systemd.sh.
# Roda como root: só aceita caminho absoluto de caracteres seguros, sem "..", sem componente vazio, com pelo menos dois
# componentes e fora da lista de pastas do sistema, para nunca gravar nem apagar nada em /etc, /usr e afins.
# safe_dir_error NOME CAMINHO: imprime o motivo (com NOME) e devolve 1 se o caminho não serve; não imprime nada e
# devolve 0 se serve. cert_dir_error CAMINHO é o mesmo, com o nome TAILNET_CERT_DIR.

safe_dir_error() {
  local name="$1" dir="$2" comp
  if ! [[ "$dir" =~ ^/[A-Za-z0-9/._-]*$ ]]; then
    echo "$name deve ser um caminho absoluto só com letras, números e /._-."
    return 1
  fi
  dir="${dir%/}"
  case "$dir/" in
    *//* | */../* | */./*) echo "$name não pode ter componentes vazios, '.' ou '..'."; return 1 ;;
  esac
  if ! [[ "$dir" =~ ^/[^/]+/[^/]+ ]]; then
    echo "$name precisa de pelo menos dois componentes (por exemplo /etc/slimegoals/tls)."
    return 1
  fi
  for comp in / /etc /usr /var /root /home /boot /opt /srv /tmp /bin /sbin /lib /run /proc /sys /dev; do
    if [ "$dir" = "$comp" ]; then
      echo "$name não pode ser uma pasta do sistema ($comp)."
      return 1
    fi
  done
  return 0
}

cert_dir_error() { safe_dir_error TAILNET_CERT_DIR "$1"; }
