#!/usr/bin/env bash
# Gera artifacts/slimegoals-<versão>-linux-x64.tar.gz (self-contained) e, ao lado, o .sha256 para conferir o pacote.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cd "$root"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n1)"
[ -n "$version" ] || { echo "Versão não encontrada em Directory.Build.props" >&2; exit 1; }

name="slimegoals-$version"
out="artifacts/$name"
tarball="$name-linux-x64.tar.gz"
rm -rf "$out" "artifacts/$tarball" "artifacts/$tarball.sha256"
mkdir -p "$out"

dotnet publish src/SlimeGoals.Desktop -c Release -r linux-x64 --self-contained -o "$out/app"

cp "$here/install.sh" "$here/uninstall.sh" "$here/slimegoals.desktop.in" "$out/"
cp -r "$here/icons" "$out/"
cp -r src/SlimeGoals.App/Licenses "$out/licenses"
cp LICENSE.md THIRD-PARTY-NOTICES.md "$out/"
chmod +x "$out/install.sh" "$out/uninstall.sh"

tar -C artifacts -czf "artifacts/$tarball" "$name"
# Dentro de artifacts/, para o .sha256 trazer só o nome do arquivo e o sha256sum -c funcionar de qualquer pasta
# que tenha os dois juntos.
(cd artifacts && sha256sum "$tarball" > "$tarball.sha256")
echo "Pacote: artifacts/$tarball"
echo "Checksum: artifacts/$tarball.sha256"
