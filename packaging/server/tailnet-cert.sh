#!/usr/bin/env bash
# Busca o certificado do nome da tailnet com `tailscale cert` e o grava em TAILNET_CERT_DIR (tailnet.crt e tailnet.key).
# Se o conteúdo mudou, troca os arquivos e recarrega o Caddy; se não mudou, não faz nada. Roda como root na VM, pelo
# timer do systemd (packaging/server/systemd/). Saída 0 sem mudança ou com renovação; diferente de zero se o
# `tailscale cert` falha (os arquivos atuais ficam intactos e o Caddy não recarrega).
# SLIMEGOALS_DOMAIN e TAILNET_CERT_DIR vêm do .env da raiz do repositório, lido linha a linha (nunca com source).
# TAILSCALE_BIN e DOCKER_BIN existem para o teste trocar por falsos.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
root="$(cd "$here/../.." && pwd -P)"
env_file="$root/.env"
tailscale_bin="${TAILSCALE_BIN:-tailscale}"
docker_bin="${DOCKER_BIN:-docker}"
systemctl_bin="${SYSTEMCTL_BIN:-systemctl}"

# shellcheck source=packaging/server/lib/env.sh
. "$here/lib/env.sh"
# shellcheck source=packaging/server/lib/cert-dir.sh
. "$here/lib/cert-dir.sh"

die() { echo "tailnet-cert: $1" >&2; exit 1; }

[ -f "$env_file" ] || die "não achei o .env da raiz do repositório."
domain="$(env_value SLIMEGOALS_DOMAIN "$env_file")"
cert_dir="$(env_value TAILNET_CERT_DIR "$env_file")"
cert_dir="${cert_dir:-/etc/slimegoals/tls}"
[ -n "$domain" ] || die "SLIMEGOALS_DOMAIN falta no .env."

# Roda como root: a validação (lib/cert-dir.sh) impede mexer em /etc, /usr e afins.
if ! msg="$(cert_dir_error "$cert_dir")"; then die "$msg"; fi

# Só o dono lê e entra, desde a criação. O contêiner do Caddy roda como uid 0 sem DAC_OVERRIDE: lê pelos bits do
# dono, e os arquivos são do root (o serviço roda como root), então 600 basta e a chave nunca fica legível a mais ninguém.
umask 077
if [ -e "$cert_dir" ]; then
  # Pasta que já existia: não é o script que decide a permissão dela, só confere que ninguém mais escreve nela.
  if [ ! -d "$cert_dir" ] || [ -L "$cert_dir" ]; then die "TAILNET_CERT_DIR não é uma pasta."; fi
  [ "$(stat -c %u "$cert_dir")" = "$(id -u)" ] || die "a pasta de TAILNET_CERT_DIR não é do usuário que roda o script; recusado."
  (( (8#$(stat -c %a "$cert_dir")) & 8#022 )) && die "a pasta de TAILNET_CERT_DIR aceita escrita de grupo ou outros; recusado."
else
  mkdir -p "$cert_dir"
  chmod 700 "$cert_dir"
fi

# Sobras de uma execução morta no meio.
find "$cert_dir" -mindepth 1 -maxdepth 1 -name '.novo.*' -exec rm -rf {} +

# Temporário dentro da própria pasta: mesma partição, então o mv é um rename atômico.
tmp="$(mktemp -d "$cert_dir/.novo.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# A saída do tailscale vai para o journal; o conteúdo da chave não passa pelo script.
if ! "$tailscale_bin" cert --cert-file "$tmp/tailnet.crt" --key-file "$tmp/tailnet.key" "$domain"; then
  die "o tailscale cert falhou; o certificado atual foi mantido."
fi
if [ ! -s "$tmp/tailnet.crt" ] || [ ! -s "$tmp/tailnet.key" ]; then
  die "o tailscale cert não gerou os arquivos; o certificado atual foi mantido."
fi
chmod 600 "$tmp/tailnet.crt" "$tmp/tailnet.key"

# O marcador registra que há certificado novo ainda não carregado pelo Caddy. Nasce antes da troca e só some depois de
# um reload bem-sucedido: se o Caddy estiver parado ou o reload falhar, a próxima execução tenta de novo.
marker="$cert_dir/.recarregar"
changed=0
for name in tailnet.key tailnet.crt; do
  if [ ! -f "$cert_dir/$name" ] || ! cmp -s "$tmp/$name" "$cert_dir/$name"; then
    changed=1
  fi
done
if [ "$changed" -eq 1 ]; then
  : > "$marker"
  chmod 600 "$marker"
  # Chave primeiro, certificado depois: o Caddy só lê os dois no reload logo abaixo, e a janela com o par trocado
  # pela metade é a do intervalo entre dois renames (e a chave costuma nem mudar na renovação).
  for name in tailnet.key tailnet.crt; do
    if [ ! -f "$cert_dir/$name" ] || ! cmp -s "$tmp/$name" "$cert_dir/$name"; then
      mv -f "$tmp/$name" "$cert_dir/$name"
    fi
  done
  echo "tailnet-cert: certificado atualizado."
else
  echo "tailnet-cert: o certificado não mudou."
fi

[ -e "$marker" ] || exit 0

# Na partida este serviço roda antes do docker.service (Before=). Se o Docker não está ativo, qualquer chamada ao CLI
# acordaria o docker.service pelo docker.socket e ficaria esperando este próprio serviço terminar: um impasse até o
# timeout. Então, sem Docker ativo, o reload fica pendente (marcador) e o Caddy lê o certificado ao subir.
if ! "$systemctl_bin" is-active --quiet docker.service; then
  echo "tailnet-cert: o Docker não está ativo; o reload fica pendente e o Caddy lê o certificado ao subir."
  exit 0
fi

compose=("$docker_bin" compose -f "$root/compose.yaml" -f "$root/compose.tailnet.yaml" --project-directory "$root")
if ! running="$("${compose[@]}" ps --status running -q caddy 2>/dev/null)"; then
  die "não consegui consultar o Docker; o reload do Caddy continua pendente."
fi
if [ -z "$running" ]; then
  echo "tailnet-cert: o Caddy não está rodando; o reload fica pendente e ele lê o certificado ao subir."
  exit 0
fi
# --force: sem ele o Caddy não recarrega quando o texto do Caddyfile é o mesmo, e o certificado novo não entraria.
if ! "${compose[@]}" exec -T caddy caddy reload --config /etc/caddy/Caddyfile --force; then
  die "o reload do Caddy falhou; continua pendente."
fi
rm -f "$marker"
echo "tailnet-cert: Caddy recarregado."
