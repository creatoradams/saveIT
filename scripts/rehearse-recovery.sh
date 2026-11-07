#!/usr/bin/env bash
set -euo pipefail
umask 077
# This deliberately targets only the synthetic, disposable packaged test graph.
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
fixture="$root/.local/container-fixture"
[[ -f "$fixture/ca.crt" && -f "$fixture/protection.pfx" && -f "$fixture/previous.pfx" ]]
compose=(docker compose -p saveit-release -f tests/production/compose.yml)
recovered=("${compose[@]}" -f tests/production/recovery.compose.yml)
sdk='mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317'
backup="$fixture/recovery-backup"
mkdir -p "$backup"
started="$(date +%s)"
"${compose[@]}" stop api api-server-only worker
for service in api api-server-only worker; do
  stopped_id="$("${compose[@]}" ps -aq "$service")"
  [[ -n "$stopped_id" && "$(docker inspect --format '{{.State.Running}}' "$stopped_id")" == false ]]
done
"${compose[@]}" exec -T db psql -XAt -U postgres -d saveit -v ON_ERROR_STOP=1 < tests/production/inventory.sql > "$backup/before-counts.txt"
[[ "$(wc -l < "$backup/before-counts.txt")" == 29 ]]
"${compose[@]}" exec -T db pg_dump -U postgres -d saveit --format=custom > "$backup/database.dump"
docker run --rm --user 0 --entrypoint /bin/sh -v saveit-release_keyring:/keys:ro -v "$fixture:/config:ro" -v "$backup:/backup" "$sdk" -euc 'umask 077; set -- /keys/key-*.xml; test -f "$1"; tar -cf /backup/keyring.tar -C /keys .; cp /config/protection.pfx /backup/protection.pfx; cp /config/previous.pfx /backup/previous.pfx; cd /backup; sha256sum database.dump keyring.tar protection.pfx previous.pfx > SHA256SUMS'
# Restore into a new DB, retaining the dump's original owners/grants. Never overwrite the source.
"${compose[@]}" exec -T db createdb -U postgres -O saveit_migration_owner -T template0 saveit_recovery
docker run --rm --user 0 --entrypoint /bin/sh -v "$backup:/backup:ro" "$sdk" -euc 'cd /backup; sha256sum --check --status SHA256SUMS'
"${compose[@]}" exec -T db pg_restore -U postgres -d saveit_recovery --exit-on-error < "$backup/database.dump"
"${compose[@]}" exec -T db psql -XAt -U postgres -d saveit_recovery -v ON_ERROR_STOP=1 < tests/production/inventory.sql > "$backup/restored-counts.txt"
cmp --silent "$backup/before-counts.txt" "$backup/restored-counts.txt"
"${recovered[@]}" create api
owner="$(docker volume inspect saveit-release_recovered_keyring --format '{{index .Labels "com.docker.compose.project"}}')"
[[ "$owner" == saveit-release ]]
docker run --rm --user 0 --entrypoint /bin/sh -v saveit-release_recovered_keyring:/restore -v "$backup:/backup:ro" "$sdk" -euc 'test -z "$(ls -A /restore)"; mkdir -p /restore/ring; tar -xf /backup/keyring.tar -C /restore/ring; cp /backup/protection.pfx /restore/protection.pfx; cp /backup/previous.pfx /restore/previous.pfx; chown -R 1654:1654 /restore; chmod 700 /restore /restore/ring; chmod 600 /restore/protection.pfx /restore/previous.pfx'
"${recovered[@]}" run --rm --no-deps worker --rebuild --once
"${recovered[@]}" up -d --force-recreate api worker
for attempt in $(seq 1 45); do
  if curl --silent --fail --cacert "$fixture/ca.crt" --resolve saveit.localhost:8443:127.0.0.1 https://saveit.localhost:8443/health/ready >/dev/null; then break; fi
  if [[ "$attempt" == 45 ]]; then echo 'Recovered application did not become ready.' >&2; exit 1; fi
  sleep 1
done
echo "Synthetic logical database and separate key-ring targets ready after $(( $(date +%s) - started )) seconds; managed recovery objectives remain unmeasured."
