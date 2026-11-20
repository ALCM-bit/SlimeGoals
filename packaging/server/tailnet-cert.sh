#!/usr/bin/env bash
# Busca o certificado do nome da tailnet com `tailscale cert` e o grava em TAILNET_CERT_DIR (tailnet.crt e tailnet.key).
# Se o conteúdo mudou, troca os arquivos e recarrega o Caddy; se não mudou, não faz nada. Roda como root na VM, pelo
# timer do systemd (packaging/server/systemd/). Saída 0 sem mudança ou com renovação; diferente de zero se o
# `tailscale cert` falha (os arquivos atuais ficam intactos e o Caddy não recarrega).
# SLIMEGOALS_DOMAIN e TAILNET_CERT_DIR vêm do .env da raiz do repositório, lido linha a linha (nunca com source).
# TAILSCALE_BIN e DOCKER_BIN existem para o teste trocar por falsos.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
env_file="$root/.env"
tailscale_bin="${TAILSCALE_BIN:-tailscale}"
docker_bin="${DOCKER_BIN:-docker}"

# Última linha NOME=valor do .env, sem aspas nas pontas. Nada do arquivo é executado.
env_value() {
  local line
  line="$(grep -E "^$1=" "$env_file" | tail -n 1 || true)"
  line="${line#*=}"
  line="${line%\"}"; line="${line#\"}"
  line="${line%\'}"; line="${line#\'}"
  printf '%s' "$line"
}

[ -f "$env_file" ] || { echo "tailnet-cert: não achei o .env da raiz do repositório." >&2; exit 1; }
domain="$(env_value SLIMEGOALS_DOMAIN)"
cert_dir="$(env_value TAILNET_CERT_DIR)"
cert_dir="${cert_dir:-/etc/slimegoals/tls}"
[ -n "$domain" ] || { echo "tailnet-cert: SLIMEGOALS_DOMAIN falta no .env." >&2; exit 1; }

# Só o dono lê e entra, desde a criação. O contêiner do Caddy roda como uid 0 sem DAC_OVERRIDE: lê pelos bits do
# dono, e os arquivos são do root (o serviço roda como root), então 600 basta e a chave nunca fica legível a mais ninguém.
umask 077
mkdir -p "$cert_dir"
chmod 700 "$cert_dir"

# Temporário dentro da própria pasta: mesma partição, então o mv é um rename atômico.
tmp="$(mktemp -d "$cert_dir/.novo.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT

# A saída do tailscale vai para o journal; o conteúdo da chave não passa pelo script.
if ! "$tailscale_bin" cert --cert-file "$tmp/tailnet.crt" --key-file "$tmp/tailnet.key" "$domain"; then
  echo "tailnet-cert: o tailscale cert falhou; o certificado atual foi mantido." >&2
  exit 1
fi
if [ ! -s "$tmp/tailnet.crt" ] || [ ! -s "$tmp/tailnet.key" ]; then
  echo "tailnet-cert: o tailscale cert não gerou os arquivos." >&2
  exit 1
fi
chmod 600 "$tmp/tailnet.crt" "$tmp/tailnet.key"

changed=0
# Chave primeiro, certificado depois: o Caddy só lê os dois no reload logo abaixo, e a janela com o par trocado pela
# metade é a do intervalo entre dois renames (e a chave costuma nem mudar na renovação).
for name in tailnet.key tailnet.crt; do
  if [ ! -f "$cert_dir/$name" ] || ! cmp -s "$tmp/$name" "$cert_dir/$name"; then
    mv -f "$tmp/$name" "$cert_dir/$name"
    changed=1
  fi
done

if [ "$changed" -eq 0 ]; then
  echo "tailnet-cert: o certificado não mudou."
  exit 0
fi
echo "tailnet-cert: certificado atualizado."

compose=("$docker_bin" compose -f "$root/compose.yaml" -f "$root/compose.tailnet.yaml" --project-directory "$root")
if [ -n "$("${compose[@]}" ps --status running -q caddy 2>/dev/null || true)" ]; then
  "${compose[@]}" exec -T caddy caddy reload --config /etc/caddy/Caddyfile
  echo "tailnet-cert: Caddy recarregado."
else
  echo "tailnet-cert: o Caddy não está rodando; ele lê o certificado ao subir."
fi
