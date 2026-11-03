#!/usr/bin/env bash
# Instala o SlimeGoals no usuário (~/.local), sem root. Rode de dentro da pasta extraída.
set -euo pipefail

[ -n "${HOME:-}" ] || { echo "HOME está vazio; não sei onde instalar." >&2; exit 1; }

src="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
opt="$HOME/.local/opt/slimegoals"
bin="$HOME/.local/bin"
apps="$HOME/.local/share/applications"
icons="$HOME/.local/share/icons/hicolor"

# Mesma regra de XdgAutostart.QuoteExec (como escrito no arquivo): aspas duplas; " ` $ \ levam uma barra a mais
# (o Desktop Entry desfaz o escape de string antes da regra de aspas do Exec) e % vira %%.
quote_exec() {
    local s="$1" out="" c i
    for (( i = 0; i < ${#s}; i++ )); do
        c="${s:i:1}"
        case "$c" in
            '"' | '`' | '$') out+="\\\\$c" ;;
            '\') out+='\\\\' ;;
            '%') out+='%%' ;;
            *) out+="$c" ;;
        esac
    done
    printf '"%s"' "$out"
}

# Não mexe em nada com o app aberto (a instalação anterior seria apagada por baixo dele).
if pgrep -u "$(id -u)" -x slimegoals >/dev/null 2>&1; then
    echo "Feche o SlimeGoals (Sair) e rode o install.sh de novo." >&2
    exit 1
fi

# Não apaga a própria origem (ex.: rodado de dentro de ~/.local/opt/slimegoals).
opt_real="$(realpath -m "$opt")"
case "$src/" in
    "$opt_real"/* | "$opt_real/")
        echo "Rode o install.sh a partir da pasta extraída do pacote, não de $opt." >&2
        exit 1 ;;
esac
case "$opt_real/" in
    "$src"/*)
        echo "A pasta do pacote contém o destino da instalação ($opt); extraia o pacote em outro lugar." >&2
        exit 1 ;;
esac

[ -x "$src/app/slimegoals" ] || { echo "Não achei app/slimegoals ao lado do install.sh." >&2; exit 1; }

# Substitui a instalação anterior.
rm -rf "$opt"
mkdir -p "$opt" "$bin" "$apps"
cp -a "$src/app/." "$opt/"
ln -sfn "$opt/slimegoals" "$bin/slimegoals"

exec_value="$(quote_exec "$opt/slimegoals")"
template="$(cat "$src/slimegoals.desktop.in")"
printf '%s\n' "${template//@EXEC@/"$exec_value"}" > "$apps/slimegoals.desktop"
chmod 644 "$apps/slimegoals.desktop"

for dir in "$src"/icons/hicolor/*/apps; do
    size="$(basename "$(dirname "$dir")")"
    mkdir -p "$icons/$size/apps"
    cp -f "$dir"/slimegoals.* "$icons/$size/apps/"
done

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$icons" || true

echo "SlimeGoals instalado em $opt"
