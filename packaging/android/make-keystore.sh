#!/usr/bin/env bash
# Cria a chave de assinatura do APK. Nunca sobrescreve uma chave existente.
set -euo pipefail

keystore="${SLIMEGOALS_KEYSTORE:-$HOME/.android-keystores/slimegoals.keystore}"
export JAVA_HOME="${JAVA_HOME:-/usr/lib/jvm/java-21-openjdk}"

if [ -e "$keystore" ]; then
  echo "A chave já existe em $keystore; não vou sobrescrever." >&2
  exit 1
fi

if [ -z "${SLIMEGOALS_KEYSTORE_PASS:-}" ]; then
  read -r -s -p "Senha da chave (usada também como senha do alias): " SLIMEGOALS_KEYSTORE_PASS
  echo
  [ -n "$SLIMEGOALS_KEYSTORE_PASS" ] || { echo "Senha vazia." >&2; exit 1; }
  read -r -s -p "Repita a senha: " confirm
  echo
  [ "$SLIMEGOALS_KEYSTORE_PASS" = "$confirm" ] || { echo "As senhas não conferem." >&2; exit 1; }
  export SLIMEGOALS_KEYSTORE_PASS
fi

dir="$(dirname "$keystore")"
mkdir -p "$dir"
chmod 700 "$dir"

"$JAVA_HOME/bin/keytool" -genkeypair -v -keystore "$keystore" -alias slimegoals \
  -keyalg RSA -keysize 4096 -validity 10000 -dname "CN=SlimeGoals" \
  -storepass:env SLIMEGOALS_KEYSTORE_PASS -keypass:env SLIMEGOALS_KEYSTORE_PASS
chmod 600 "$keystore"

echo "Chave criada em $keystore"
echo "Guarde uma cópia da chave e da senha em lugar seguro: sem elas, não há como publicar atualizações que instalem por cima deste APK."
