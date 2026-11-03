#!/usr/bin/env bash
# Gera os ic_launcher.png (mipmap-*) e os ic_notification.png (drawable-*) a partir do ícone do app.
# Requer rsvg-convert e sed.
set -euo pipefail
cd "$(dirname "$0")"
svg=../SlimeGoals.App/Assets/icon.svg
for spec in mdpi:48 hdpi:72 xhdpi:96 xxhdpi:144 xxxhdpi:192; do
  dir="Resources/mipmap-${spec%%:*}"
  mkdir -p "$dir"
  rsvg-convert -w "${spec##*:}" -h "${spec##*:}" "$svg" -o "$dir/ic_launcher.png"
done

# Ícone pequeno da notificação: o Android só usa o alfa, então vira uma silhueta branca
# (todas as cores do SVG trocadas por branco; opacidades e transparência preservadas).
white=$(mktemp --suffix=.svg)
trap 'rm -f "$white"' EXIT
sed -E 's/#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{3}\b/#FFFFFF/g' "$svg" > "$white"
for spec in mdpi:24 hdpi:36 xhdpi:48 xxhdpi:72 xxxhdpi:96; do
  dir="Resources/drawable-${spec%%:*}"
  mkdir -p "$dir"
  rsvg-convert -w "${spec##*:}" -h "${spec##*:}" "$white" -o "$dir/ic_notification.png"
done
