#!/usr/bin/env bash
# Remove o SlimeGoals instalado em ~/.local. Mantém o autostart.
# Os dados só saem com confirmação, ou com --apagar-dados; sem terminal e sem a opção, eles ficam.
set -euo pipefail

[ -n "${HOME:-}" ] || { echo "HOME está vazio; não sei o que remover." >&2; exit 1; }

purge=0
case "${1:-}" in
    "") ;;
    --apagar-dados) purge=1 ;;
    *) echo "Uso: ./uninstall.sh [--apagar-dados]" >&2; exit 2 ;;
esac

# Mesma regra de XdgPaths.DatabasePath
data="${XDG_DATA_HOME:-$HOME/.local/share}/SlimeGoals"

if [ "$purge" = 0 ] && [ -d "$data" ] && [ -t 0 ]; then
    echo "Seus dados (metas e histórico) estão em $data."
    read -r -p "Apagar também os dados? Isso não pode ser desfeito. [s/N] " answer || answer=""
    case "$answer" in
        s | S | sim | Sim | SIM) purge=1 ;;
    esac
fi

# Com o app aberto, ele seguiria usando o banco apagado. Falha antes de remover qualquer coisa.
if [ "$purge" = 1 ] && pgrep -u "$(id -u)" -x slimegoals >/dev/null; then
    echo "O SlimeGoals está aberto. Feche-o (Sair) antes de apagar os dados." >&2
    exit 1
fi

rm -rf "$HOME/.local/opt/slimegoals"
rm -f "$HOME/.local/bin/slimegoals"
rm -f "$HOME/.local/share/applications/slimegoals.desktop"
icons="$HOME/.local/share/icons/hicolor"
for f in "$icons"/*/apps/slimegoals.png "$icons"/*/apps/slimegoals.svg; do
    [ -e "$f" ] && rm -f "$f"
done

apps="$HOME/.local/share/applications"
[ -d "$apps" ] && command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" || true
[ -d "$icons" ] && command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$icons" || true

echo "SlimeGoals removido."
if [ "$purge" = 1 ]; then
    rm -rf "$data"
    echo "Seus dados foram apagados."
elif [ -d "$data" ]; then
    echo "Seus dados continuam em $data."
fi
autostart="${XDG_CONFIG_HOME:-$HOME/.config}/autostart/slimegoals.desktop"
[ -e "$autostart" ] && \
    echo "$autostart aponta para o app removido e pode ser apagado."
exit 0
