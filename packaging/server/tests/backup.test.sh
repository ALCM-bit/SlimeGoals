#!/usr/bin/env bash
# Testa o backup.sh com um docker falso e pastas temporárias. Nada de Docker nem PostgreSQL reais.
# Uso: bash packaging/server/tests/backup.test.sh
set -uo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
fail() { echo "FALHOU: $1" >&2; failures=$((failures + 1)); }

setup() {
  local name="$1"
  work="$tmp/$name"
  mkdir -p "$work/root/packaging/server/lib" "$work/bin"
  cp "$src/backup.sh" "$work/root/packaging/server/backup.sh"
  cp "$src/lib/cert-dir.sh" "$work/root/packaging/server/lib/"
  : > "$work/root/compose.yaml"
  : > "$work/root/compose.tailnet.yaml"
  # FAKE_FAIL=1: pg_dump falha depois de escrever parte; FAKE_EMPTY=1: sai 0 sem escrever nada; FAKE_BAD=1: lixo;
  # FAKE_TRUNCATED=1: cabeçalho certo mas sem o final (o pg_restore -l, que só lê o índice, deixaria passar).
  # FAKE_NOT_READY=N: o pg_isready falha nas N primeiras chamadas (999 = nunca fica pronto).
  cat > "$work/bin/docker" <<'FAKE'
#!/usr/bin/env bash
echo "docker $*" >> "$FAKE_LOG"
case " $* " in
  *" pg_isready "*)
    n="$(cat "$FAKE_LOG.ready" 2>/dev/null || echo 0)"; echo $((n + 1)) > "$FAKE_LOG.ready"
    [ "$n" -ge "${FAKE_NOT_READY:-0}" ]; exit $? ;;
  # Só "-f /dev/null" lê o arquivo inteiro; o falso exige o cabeçalho e o final. Com "-l" (só o índice) aceitaria tudo.
  *" pg_restore -f /dev/null "*) c="$(cat)"; [[ "$c" == PGDMP*FIM ]]; exit $? ;;
  *" pg_restore "*) cat > /dev/null; exit 0 ;;
esac
if [ "${FAKE_EMPTY:-0}" = 1 ]; then exit 0; fi
[ "${FAKE_BAD:-0}" = 1 ] && { printf 'lixo'; exit 0; }
printf 'PGDMP-FALSO'
[ "${FAKE_FAIL:-0}" = 1 ] && exit 1
[ "${FAKE_TRUNCATED:-0}" = 1 ] || printf 'FIM'
exit 0
FAKE
  chmod +x "$work/bin/docker"
  : > "$work/log"
  out="$work/out"
  dest="$work/backups"
}

run() { # roda o script com a pasta $dest; saída em $out
  FAKE_LOG="$work/log" DOCKER_BIN="$work/bin/docker" READY_WAIT_SECONDS=2 READY_POLL_SECONDS=1 \
    bash "$work/root/packaging/server/backup.sh" "$dest" > "$out" 2>&1
}
mode() { stat -c %a "$1"; }
dumps() { find "$dest" -maxdepth 1 -name 'slimegoals-*.dump' | wc -l; }

# 1
setup writes_dump_600_in_700_dir
run || fail "writes: o script devia sair 0"
[ "$(mode "$dest")" = 700 ] || fail "writes: a pasta devia ser 700"
[ "$(dumps)" = 1 ] || fail "writes: devia haver um .dump"
f="$(find "$dest" -name 'slimegoals-*.dump')"
[[ "$(basename "$f")" =~ ^slimegoals-[0-9]{8}-[0-9]{6}\.dump$ ]] || fail "writes: nome fora do padrão ($f)"
[ "$(mode "$f")" = 600 ] || fail "writes: o .dump devia ser 600"
[ "$(cat "$f")" = "PGDMP-FALSOFIM" ] || fail "writes: conteúdo errado"
[ -z "$(find "$dest" -mindepth 1 -not -name 'slimegoals-*.dump' -not -name .lock)" ] || fail "writes: sobrou arquivo temporário"
grep -q ' pg_restore -f /dev/null' "$work/log" || fail "writes: faltou a conferência com pg_restore -f /dev/null"
grep -q ' pg_restore -l' "$work/log" && fail "writes: pg_restore -l só lê o índice; não pode ser a conferência"
grep -q ' exec -T db pg_isready -U postgres' "$work/log" || fail "writes: faltou esperar o banco com pg_isready"
grep -q 'compose -f .*/compose.yaml -f .*/compose.tailnet.yaml --project-directory .* exec -T db pg_dump -U postgres -d slimegoals -Fc' "$work/log" \
  || fail "writes: comando do docker inesperado ($(cat "$work/log"))"
grep -q 'PGDMP' "$out" && fail "writes: o dump não pode ir para a saída"

# 2
setup keeps_the_14_newest
mkdir -p "$dest"; chmod 700 "$dest"
for i in $(seq -w 1 16); do : > "$dest/slimegoals-202601$i-030000.dump"; done
: > "$dest/outro-arquivo.txt"; : > "$dest/slimegoals-notes.txt"
run || fail "keeps: o script devia sair 0"
[ "$(dumps)" = 14 ] || fail "keeps: devia sobrar 14, sobraram $(dumps)"
[ ! -e "$dest/slimegoals-20260101-030000.dump" ] || fail "keeps: o mais antigo devia ter sido apagado"
[ ! -e "$dest/slimegoals-20260103-030000.dump" ] || fail "keeps: o 3º mais antigo devia ter sido apagado"
[ -e "$dest/slimegoals-20260104-030000.dump" ] || fail "keeps: o 4º mais antigo devia ficar (14 contando o novo)"
[ -e "$dest/slimegoals-20260116-030000.dump" ] || fail "keeps: o mais novo antigo devia ficar"
{ [ -e "$dest/outro-arquivo.txt" ] && [ -e "$dest/slimegoals-notes.txt" ]; } || fail "keeps: apagou arquivo que não é dump"

# 3
setup failed_dump_leaves_no_partial_file
mkdir -p "$dest"; chmod 700 "$dest"
for i in 01 02 03; do printf 'velho' > "$dest/slimegoals-202601$i-030000.dump"; done
FAKE_FAIL=1 run && fail "failed: o script devia sair diferente de 0"
[ "$(dumps)" = 3 ] || fail "failed: nenhum .dump novo, sobraram $(dumps)"
[ -z "$(find "$dest" -mindepth 1 -name '.novo.*')" ] || fail "failed: sobrou temporário"
[ "$(cat "$dest/slimegoals-20260101-030000.dump")" = velho ] || fail "failed: o antigo mudou"

# 4
setup empty_dump_is_rejected
mkdir -p "$dest"; chmod 700 "$dest"
printf 'velho' > "$dest/slimegoals-20260101-030000.dump"
FAKE_EMPTY=1 run && fail "empty: o script devia sair diferente de 0"
[ "$(dumps)" = 1 ] || fail "empty: não devia criar .dump"
[ -z "$(find "$dest" -mindepth 1 -name '.novo.*')" ] || fail "empty: sobrou temporário"

# 4b
setup invalid_dump_is_rejected
mkdir -p "$dest"; chmod 700 "$dest"
for i in 01 02; do printf 'velho' > "$dest/slimegoals-202601$i-030000.dump"; done
FAKE_BAD=1 run && fail "invalid: o script devia sair diferente de 0"
[ "$(dumps)" = 2 ] || fail "invalid: não devia criar .dump, há $(dumps)"
[ "$(cat "$dest/slimegoals-20260101-030000.dump")" = velho ] || fail "invalid: o antigo mudou"
[ -z "$(find "$dest" -mindepth 1 -name '.novo.*')" ] || fail "invalid: sobrou temporário"

# 4b2: dump cortado na parte dos dados (o índice está inteiro): sai 0 do pg_dump, mas o pg_restore -f /dev/null reprova
setup truncated_dump_is_rejected
mkdir -p "$dest"; chmod 700 "$dest"
for i in 01 02; do printf 'velho' > "$dest/slimegoals-202601$i-030000.dump"; done
FAKE_TRUNCATED=1 run && fail "truncated: o script devia sair diferente de 0"
[ "$(dumps)" = 2 ] || fail "truncated: não devia criar .dump, há $(dumps)"
[ -z "$(find "$dest" -mindepth 1 -name '.novo.*')" ] || fail "truncated: sobrou temporário"

# 4b3: o banco ainda sobe (partida da VM): o backup espera o pg_isready e depois grava
setup waits_for_db
FAKE_NOT_READY=2 READY_WAIT_SECONDS=10 run || fail "wait: devia esperar o banco e sair 0 ($(cat "$out"))"
[ "$(cat "$work/log.ready")" = 3 ] || fail "wait: devia consultar o pg_isready 3 vezes"
[ "$(dumps)" = 1 ] || fail "wait: devia gravar o dump"

# 4b4: o banco não sobe a tempo: falha clara, nada é gravado e o pg_dump nem roda
setup db_never_ready
mkdir -p "$dest"; chmod 700 "$dest"
FAKE_NOT_READY=999 run && fail "not_ready: devia sair diferente de 0"
grep -q 'banco' "$out" || fail "not_ready: a mensagem devia falar do banco ($(cat "$out"))"
grep -q ' pg_dump ' "$work/log" && fail "not_ready: não devia rodar o pg_dump"
[ "$(dumps)" = 0 ] || fail "not_ready: não devia gravar"

# 4c
setup new_dump_survives_clock_stepping_back
mkdir -p "$dest"; chmod 700 "$dest"
for i in $(seq -w 1 16); do : > "$dest/slimegoals-299901$i-030000.dump"; done
run || fail "clock: o script devia sair 0"
[ "$(dumps)" = 14 ] || fail "clock: devia sobrar 14, sobraram $(dumps)"
[ -n "$(find "$dest" -name "slimegoals-$(date +%Y)*.dump")" ] || fail "clock: o dump novo foi apagado"

# 4d
setup concurrent_run_is_refused
mkdir -p "$dest"; chmod 700 "$dest"
( exec 9> "$dest/.lock"; flock -n 9 && sleep 3 ) &
sleep 0.5
run && fail "lock: devia recusar com outro backup rodando"
[ "$(dumps)" = 0 ] || fail "lock: não devia gravar"
wait

# 5
setup refuses_unsafe_dirs
for bad in /etc /usr/lib/../x relativa "/tmp/com espaco" /var; do
  FAKE_LOG="$work/log" DOCKER_BIN="$work/bin/docker" bash "$work/root/packaging/server/backup.sh" "$bad" > "$out" 2>&1 \
    && fail "unsafe: devia recusar $bad"
done
[ ! -s "$work/log" ] || fail "unsafe: não devia chamar o docker"

# 6
setup refuses_foreign_or_writable_existing_dir
mkdir -p "$dest"; chmod 770 "$dest"
run && fail "writable: devia recusar pasta com escrita de grupo"
[ "$(mode "$dest")" = 770 ] || fail "writable: não devia mexer na permissão"
[ ! -s "$work/log" ] || fail "writable: não devia chamar o docker"

# 7
setup compose_files_override
FAKE_LOG="$work/log" DOCKER_BIN="$work/bin/docker" COMPOSE_FILES="compose.yaml compose.dev.yaml" \
  bash "$work/root/packaging/server/backup.sh" "$dest" > "$out" 2>&1 || fail "override: devia sair 0"
grep -q -- '-f .*/compose.yaml -f .*/compose.dev.yaml ' "$work/log" || fail "override: COMPOSE_FILES ignorado"

# 8: o systemd executa os scripts direto (ExecStart), então o git precisa guardá-los como 100755.
if git -C "$src" rev-parse --git-dir > /dev/null 2>&1; then
  for unit in "$src"/systemd/*.service; do
    while IFS= read -r line; do
      script="${line#ExecStart=@ROOT@/}"
      [ "$script" != "$line" ] || continue
      git -C "$src" ls-files -s -- "../../$script" | grep -q '^100755 ' || fail "exec: $script não é 100755 no git"
    done < <(grep '^ExecStart=' "$unit")
  done
fi

if [ "$failures" -gt 0 ]; then echo "$failures falha(s)." >&2; exit 1; fi
echo "OK: todos os testes do backup passaram."
