#!/usr/bin/env bash
set -euo pipefail
# Isolated synthetic CA only. Keep this exact CA through build, smoke and recovery.
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
fixture="$root/.local/container-fixture"
mkdir -p "$fixture"
if [[ -e "$root/.local/public-trust" || -e "$root/.local/client-trust" || -e "$fixture/ca.key" || -e "$fixture/ca.crt" || -e "$fixture/client-ca.key" || -e "$fixture/client-ca.crt" ]]; then
  echo 'Use a fresh isolated synthetic trust fixture; existing CA/output files will not be replaced.' >&2
  exit 1
fi
umask 077
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$fixture/ca.key" -out "$fixture/ca.crt" -days 2 \
  -subj '/CN=SaveIT synthetic fixture CA' -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1
chmod 644 "$fixture/ca.crt"
node scripts/prepare-public-trust.mjs "$fixture/ca.crt" "$fixture/ca.crt" "$root/.local/public-trust"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$fixture/client-ca.key" -out "$fixture/client-ca.crt" -days 2 \
  -subj '/CN=SaveIT synthetic client CA' -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1
chmod 644 "$fixture/client-ca.crt"
node scripts/prepare-client-trust.mjs "$fixture/client-ca.crt" "$root/.local/client-trust"
