#!/usr/bin/env bash
# Remove o SlimeGoals instalado em ~/.local. Mantém os dados e o autostart.
set -euo pipefail

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
echo "Seus dados continuam em ~/.local/share/SlimeGoals."
autostart="${XDG_CONFIG_HOME:-$HOME/.config}/autostart/slimegoals.desktop"
[ -e "$autostart" ] && \
    echo "$autostart aponta para o app removido e pode ser apagado."
exit 0
