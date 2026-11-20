#!/usr/bin/env bash
# Validação de pasta onde um script root vai gravar (TAILNET_CERT_DIR, pasta do backup), para ser lida com `.`
# pelo tailnet-cert.sh, pelo backup.sh e pelo install-systemd.sh.
# Roda como root: só aceita caminho absoluto de caracteres seguros, sem "..", sem componente vazio, com pelo menos dois
# componentes e fora da lista de pastas do sistema, para nunca gravar nem apagar nada em /etc, /usr e afins.
# safe_dir_error NOME CAMINHO: imprime o motivo (com NOME) e devolve 1 se o caminho não serve; não imprime nada e
# devolve 0 se serve. cert_dir_error CAMINHO é o mesmo, com o nome TAILNET_CERT_DIR.
# Recusa também caminho com "tailscale" e /etc/ssl e /etc/pki.

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
    echo "$name precisa de pelo menos dois componentes."
    return 1
  fi
  for comp in / /etc /usr /var /root /home /boot /opt /srv /tmp /bin /sbin /lib /run /proc /sys /dev; do
    if [ "$dir" = "$comp" ]; then
      echo "$name não pode ser uma pasta do sistema ($comp)."
      return 1
    fi
  done
  # Pastas do tailscaled (estado e chaves da máquina) e do repositório de certificados do sistema nunca servem.
  case "$dir" in
    *tailscale*) echo "$name não pode ficar em caminho com 'tailscale'."; return 1 ;;
    /etc/ssl | /etc/ssl/* | /etc/pki | /etc/pki/*) echo "$name não pode ficar em /etc/ssl nem em /etc/pki."; return 1 ;;
  esac
  return 0
}

# cert_dir_contents_error CAMINHO: se a pasta do certificado já existe, só aceita o que o tailnet-cert.sh grava
# (tailnet.crt, tailnet.key, .recarregar, .novo.*, o lock .lock); qualquer outra coisa indica a pasta errada.
cert_dir_contents_error() {
  local dir="$1" entry base
  [ -d "$dir" ] || return 0
  while IFS= read -r -d '' entry; do
    base="${entry##*/}"
    case "$base" in
      tailnet.crt | tailnet.key | .recarregar | .lock | .novo.*) ;;
      *) echo "TAILNET_CERT_DIR já existe e guarda algo que não é do certificado ($base); recusado."; return 1 ;;
    esac
  done < <(find "$dir" -mindepth 1 -maxdepth 1 -print0)
  return 0
}

cert_dir_error() { safe_dir_error TAILNET_CERT_DIR "$1"; }
