#!/usr/bin/env bash
# Backup do banco: pg_dump em formato custom, pelo contêiner db, gravado em PASTA (padrão /var/backups/slimegoals) como
# slimegoals-AAAAMMDD-HHMMSS.dump. Guarda os 14 mais recentes. Roda como root na VM, pelo timer do systemd.
# O dump vai para um temporário na própria pasta e só vira .dump (mv, atômico) se o pg_dump saiu com 0 e não está vazio.
# Uso: packaging/server/backup.sh [PASTA]
# COMPOSE_FILES (padrão "compose.yaml compose.tailnet.yaml") escolhe os arquivos do Compose, relativos à raiz do
# repositório; COMPOSE_PROJECT_NAME, se definido, vale para o docker compose. DOCKER_BIN existe para o teste.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
root="$(cd "$here/../.." && pwd -P)"
docker_bin="${DOCKER_BIN:-docker}"
keep=14

# shellcheck source=packaging/server/lib/cert-dir.sh
. "$here/lib/cert-dir.sh"

die() { echo "backup: $1" >&2; exit 1; }

dest="${1:-/var/backups/slimegoals}"
# Roda como root: a mesma validação da pasta do certificado impede gravar e apagar em /etc, /usr e afins.
if ! msg="$(safe_dir_error "A pasta do backup" "$dest")"; then die "$msg"; fi
dest="${dest%/}"

umask 077
if [ -e "$dest" ]; then
  # Pasta que já existia: o script não decide a permissão dela, só confere que ninguém mais escreve nela.
  if [ ! -d "$dest" ] || [ -L "$dest" ]; then die "a pasta do backup não é uma pasta."; fi
  [ "$(stat -c %u "$dest")" = "$(id -u)" ] || die "a pasta do backup não é do usuário que roda o script; recusado."
  (( (8#$(stat -c %a "$dest")) & 8#022 )) && die "a pasta do backup aceita escrita de grupo ou outros; recusado."
else
  mkdir -p "$dest"
  chmod 700 "$dest"
fi

compose=("$docker_bin" compose)
for f in ${COMPOSE_FILES:-compose.yaml compose.tailnet.yaml}; do
  case "$f" in /*) compose+=(-f "$f") ;; *) compose+=(-f "$root/$f") ;; esac
done
compose+=(--project-directory "$root")

# Uma execução por vez: o lock fica aberto (fd 9) até o script terminar.
exec 9> "$dest/.lock"
flock -n 9 || die "outro backup está rodando; nada foi feito."

# Sobras de uma execução morta no meio.
find "$dest" -mindepth 1 -maxdepth 1 -type f -name '.novo.*' -delete

tmp="$(mktemp "$dest/.novo.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# O dump vai direto para o arquivo, nunca para a saída nem para o log. Só o estado de saída do pg_dump decide o sucesso.
if ! "${compose[@]}" exec -T db pg_dump -U postgres -d slimegoals -Fc > "$tmp"; then
  die "o pg_dump falhou; nenhum backup novo foi gravado."
fi
[ -s "$tmp" ] || die "o pg_dump não gerou dados; nenhum backup novo foi gravado."
# Um dump cortado (disco cheio, escrita falha) sairia 0 e seria guardado, e giraria as cópias boas para fora.
# O pg_restore -l lê o índice do arquivo (pelo stdin, dentro do contêiner) e falha se estiver truncado ou corrompido.
if ! "${compose[@]}" exec -T db pg_restore -l < "$tmp" > /dev/null; then
  die "o dump gerado não passou na conferência do pg_restore; nenhum backup novo foi gravado."
fi
final="$dest/slimegoals-$(date +%Y%m%d-%H%M%S).dump"
[ ! -e "$final" ] || die "já existe um backup com este nome ($(basename "$final")); nada foi gravado."
mv "$tmp" "$final"
echo "backup: gravado $(basename "$final")."

# Só arquivos slimegoals-*.dump entram na contagem; ficam os 14 mais novos pelo nome, o novo incluso. O novo nunca é
# apagado, mesmo que o relógio tenha voltado e o nome dele ordene abaixo dos antigos.
n=1
while IFS= read -r -d '' f; do
  [ "$f" = "$final" ] && continue
  n=$((n + 1))
  [ "$n" -le "$keep" ] || rm -f -- "$f"
done < <(find "$dest" -mindepth 1 -maxdepth 1 -type f -name 'slimegoals-*.dump' -print0 | sort -z -r)
