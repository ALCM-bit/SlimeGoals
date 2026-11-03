#!/usr/bin/env bash
# Gera artifacts/slimegoals-<versão>-linux-x64.tar.gz (self-contained).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cd "$root"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n1)"
[ -n "$version" ] || { echo "Versão não encontrada em Directory.Build.props" >&2; exit 1; }

name="slimegoals-$version"
out="artifacts/$name"
rm -rf "$out" "artifacts/$name-linux-x64.tar.gz"
mkdir -p "$out"

dotnet publish src/SlimeGoals.Desktop -c Release -r linux-x64 --self-contained -o "$out/app"

cp "$here/install.sh" "$here/uninstall.sh" "$here/slimegoals.desktop.in" "$out/"
cp -r "$here/icons" "$here/licenses" "$out/"
cp LICENSE.md THIRD-PARTY-NOTICES.md "$out/"
chmod +x "$out/install.sh" "$out/uninstall.sh"

tar -C artifacts -czf "artifacts/$name-linux-x64.tar.gz" "$name"
echo "Pacote: artifacts/$name-linux-x64.tar.gz"
