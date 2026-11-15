#!/usr/bin/env bash
# Gera artifacts/slimegoals-<versão>-android.apk (Release, assinado).
# Com --dev, gera artifacts/slimegoals-<versão>-android-dev.apk: o mesmo app com a conta e a sincronização
# (SyncDev), assinado com a mesma chave, para instalar por cima sem perder os dados.
set -euo pipefail

dev=false
case "$#:${1:-}" in
  0:) ;;
  1:--dev) dev=true ;;
  *) echo "Uso: packaging/android/build.sh [--dev]" >&2; exit 2 ;;
esac

# Caminho relativo vale a partir de onde o script foi chamado, não da raiz do repositório.
if [ -n "${SLIMEGOALS_KEYSTORE:-}" ]; then
  SLIMEGOALS_KEYSTORE="$(realpath -m -- "$SLIMEGOALS_KEYSTORE")"
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cd "$root"

keystore="${SLIMEGOALS_KEYSTORE:-$HOME/.android-keystores/slimegoals.keystore}"
android_home="${ANDROID_HOME:-$HOME/Android/Sdk}"
java_home="${JAVA_HOME:-/usr/lib/jvm/java-21-openjdk}"
# O apksigner precisa do java no PATH.
PATH="$java_home/bin:$PATH"

if [ -z "${SLIMEGOALS_KEYSTORE_PASS:-}" ]; then
  if [ -t 0 ]; then
    read -r -s -p "Senha da chave: " SLIMEGOALS_KEYSTORE_PASS
    echo
  fi
  [ -n "${SLIMEGOALS_KEYSTORE_PASS:-}" ] || { echo "Defina SLIMEGOALS_KEYSTORE_PASS com a senha da chave." >&2; exit 1; }
fi
[ -f "$keystore" ] || { echo "Chave não encontrada em $keystore; crie com packaging/android/make-keystore.sh." >&2; exit 1; }

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n1)"
[ -n "$version" ] || { echo "Versão não encontrada em Directory.Build.props" >&2; exit 1; }

export SLIMEGOALS_KEYSTORE_PASS
suffix=""
sync_dev=()
if [ "$dev" = true ]; then
  suffix="-dev"
  sync_dev=(-p:SyncDev=true)
fi
out="artifacts/slimegoals-$version-android$suffix.apk"
pub="src/SlimeGoals.Android/bin/Release/net10.0-android"
rm -f "$out"
find "$pub" -name '*-Signed.apk' -delete 2>/dev/null || true

dotnet publish src/SlimeGoals.Android -c Release -f net10.0-android \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$keystore" \
  -p:AndroidSigningKeyAlias=slimegoals \
  -p:AndroidSigningStorePass=env:SLIMEGOALS_KEYSTORE_PASS \
  -p:AndroidSigningKeyPass=env:SLIMEGOALS_KEYSTORE_PASS \
  -p:AndroidSdkDirectory="$android_home" \
  -p:JavaSdkDirectory="$java_home" \
  "${sync_dev[@]}"

apk="$(find "$pub" -name '*-Signed.apk' | head -n1)"
[ -n "$apk" ] || { echo "APK assinado não encontrado em $pub" >&2; exit 1; }
mkdir -p artifacts
cp "$apk" "$out"

apksigner="$(find "$android_home/build-tools" -name apksigner | sort | tail -n1)"
"$apksigner" verify --print-certs "$out"
echo "APK: $out"
