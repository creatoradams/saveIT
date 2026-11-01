#!/usr/bin/env bash
set -euo pipefail
# Synthetic container fixture only; never use this as an unreviewed production key operation.
root="$(cd "$(dirname "$0")/.." && pwd)"
fixture="$root/.local/container-fixture"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$fixture/rotated.key" -out "$fixture/rotated.crt" -days 2 -subj '/CN=SaveIT synthetic rotated key protector' >/dev/null 2>&1
openssl pkcs12 -export -out "$fixture/rotated.pfx" -inkey "$fixture/rotated.key" -in "$fixture/rotated.crt" -passout pass:synthetic-container-only >/dev/null 2>&1
chmod 644 "$fixture/rotated.pfx"
mv "$fixture/rotated.pfx" "$fixture/protection.pfx"
