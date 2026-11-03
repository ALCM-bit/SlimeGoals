#!/usr/bin/env bash
# Gera os ic_launcher.png (mipmap-*) a partir do ícone do app. Requer rsvg-convert.
set -euo pipefail
cd "$(dirname "$0")"
svg=../SlimeGoals.App/Assets/icon.svg
for spec in mdpi:48 hdpi:72 xhdpi:96 xxhdpi:144 xxxhdpi:192; do
  dir="Resources/mipmap-${spec%%:*}"
  mkdir -p "$dir"
  rsvg-convert -w "${spec##*:}" -h "${spec##*:}" "$svg" -o "$dir/ic_launcher.png"
done
