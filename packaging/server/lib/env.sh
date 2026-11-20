#!/usr/bin/env bash
# Leitura de uma variável do .env sem dar source (nada do arquivo é executado). Para ser lido com `.` pelos scripts.
# env_value NOME ARQUIVO: imprime o valor da última linha `NOME=...` (a última vence, como no Compose). Tira o CR do
# fim de linha, espaços nas pontas, um par de aspas iguais em volta e, em valor sem aspas, o comentário ` #...`.

env_value() {
  local name="$1" file="$2" line rest
  line="$(grep -E "^${name}=" "$file" | tail -n 1 || true)"
  line="${line#*=}"
  line="${line//$'\r'/}"
  line="${line#"${line%%[![:space:]]*}"}"
  case "$line" in
    \"*)
      rest="${line#\"}"
      line="${rest%%\"*}"
      ;;
    \'*)
      rest="${line#\'}"
      line="${rest%%\'*}"
      ;;
    *)
      line="${line%%[[:space:]]#*}"
      line="${line%"${line##*[![:space:]]}"}"
      ;;
  esac
  printf '%s' "$line"
}
