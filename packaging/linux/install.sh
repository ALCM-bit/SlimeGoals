#!/usr/bin/env bash
# Instala o SlimeGoals no usuário (~/.local), sem root. Rode de dentro da pasta extraída.
set -euo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
opt="$HOME/.local/opt/slimegoals"
bin="$HOME/.local/bin"
apps="$HOME/.local/share/applications"
icons="$HOME/.local/share/icons/hicolor"

[ -x "$src/app/slimegoals" ] || { echo "Não achei app/slimegoals ao lado do install.sh." >&2; exit 1; }

# Mesma regra de XdgAutostart.QuoteExec: aspas duplas e escape de " ` $ \.
quote_exec() {
    local s="$1" out="" c i
    for (( i = 0; i < ${#s}; i++ )); do
        c="${s:i:1}"
        case "$c" in
            '"' | '`' | '$' | '\') out+="\\$c" ;;
            *) out+="$c" ;;
        esac
    done
    printf '"%s"' "$out"
}

# Substitui a instalação anterior.
rm -rf "$opt"
mkdir -p "$opt" "$bin" "$apps"
cp -a "$src/app/." "$opt/"
ln -sfn "$opt/slimegoals" "$bin/slimegoals"

exec_value="$(quote_exec "$opt/slimegoals")"
template="$(cat "$src/slimegoals.desktop.in")"
printf '%s\n' "${template//@EXEC@/$exec_value}" > "$apps/slimegoals.desktop"
chmod 644 "$apps/slimegoals.desktop"

for dir in "$src"/icons/hicolor/*/apps; do
    size="$(basename "$(dirname "$dir")")"
    mkdir -p "$icons/$size/apps"
    cp -f "$dir"/slimegoals.* "$icons/$size/apps/"
done

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$icons" || true

echo "SlimeGoals instalado em $opt"
if pgrep -x slimegoals >/dev/null 2>&1; then
    echo "Feche o SlimeGoals (Sair) e abra de novo para usar a versão nova."
fi
