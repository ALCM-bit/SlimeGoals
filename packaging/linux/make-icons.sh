#!/usr/bin/env bash
# Gera os PNG do ícone a partir de src/SlimeGoals.App/Assets/icon.svg (requer rsvg-convert).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
svg="$root/src/SlimeGoals.App/Assets/icon.svg"
icons="$root/packaging/linux/icons/hicolor"
for n in 16 32 48 64 128 256 512; do
  mkdir -p "$icons/${n}x${n}/apps"
  rsvg-convert -w "$n" -h "$n" "$svg" -o "$icons/${n}x${n}/apps/slimegoals.png"
done
mkdir -p "$icons/scalable/apps"
cp "$svg" "$icons/scalable/apps/slimegoals.svg"
rsvg-convert -w 256 -h 256 "$svg" -o "$root/src/SlimeGoals.App/Assets/icon-256.png"
rsvg-convert -w 64 -h 64 "$svg" -o "$root/src/SlimeGoals.App/Assets/tray.png"
cp "$svg" "$root/src/SlimeGoals.App/Assets/tray.svg"
