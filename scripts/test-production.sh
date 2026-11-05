#!/usr/bin/env bash
set -euo pipefail
# Linux CI/local Docker only. Every credential and identity below is synthetic and isolated.
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
fixture="$root/.local/container-fixture"
mkdir -p "$fixture"
compose=(docker compose -p saveit-release -f tests/production/compose.yml)
# The custom minimal web runtime must execute the actual patched binary under its
# declared nonroot identity, before any application/network fixture is started.
test "$(docker run --rm saveit-web:test --version)" = 'v24.22.2'
# Exact image VOLUME path must be private, writable and nonroot even with a read-only root.
docker run --rm --read-only saveit-web:test -e "const fs=require('node:fs'),a=require('node:assert/strict');const p='/var/saveit/runtime',s=fs.statSync(p);a.equal(process.getuid(),1654);a.equal(s.uid,1654);a.equal(s.gid,1654);a.equal(s.mode&0o777,0o700);fs.writeFileSync(p+'/probe','synthetic');fs.unlinkSync(p+'/probe');"
cleanup() {
  status=$?
  if [[ "$status" != 0 ]]; then
    mkdir -p test-results
    "${compose[@]}" logs --no-color > test-results/production-containers.log 2>&1 || true
  fi
  "${compose[@]}" down --volumes --remove-orphans
  # The base service graph no longer references the override-only recovery volume.
  if docker volume inspect saveit-release_recovered_keyring >/dev/null 2>&1; then
    owner="$(docker volume inspect saveit-release_recovered_keyring --format '{{index .Labels "com.docker.compose.project"}}')"
    if [[ "$owner" != saveit-release ]]; then echo 'Refusing to remove an unowned recovery volume.' >&2; return 1; fi
    docker volume rm saveit-release_recovered_keyring >/dev/null
  fi
  return "$status"
}
trap cleanup EXIT
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$fixture/ca.key" -out "$fixture/ca.crt" -days 2 -subj '/CN=SaveIT synthetic fixture CA' >/dev/null 2>&1
for name in tls pg api; do
  if [[ "$name" == tls ]]; then san='DNS:saveit.localhost,DNS:idp.localhost'; elif [[ "$name" == api ]]; then san='DNS:api'; else san='DNS:db'; fi
  openssl req -newkey rsa:2048 -nodes -keyout "$fixture/$name.new.key" -out "$fixture/$name.csr" -subj "/CN=$name" >/dev/null 2>&1
  mv -f "$fixture/$name.new.key" "$fixture/$name.key"
  printf 'subjectAltName=%s\nextendedKeyUsage=serverAuth\n' "$san" > "$fixture/$name.ext"
  openssl x509 -req -in "$fixture/$name.csr" -CA "$fixture/ca.crt" -CAkey "$fixture/ca.key" -CAcreateserial -out "$fixture/$name.crt" -days 2 -extfile "$fixture/$name.ext" >/dev/null 2>&1
done
openssl pkcs12 -export -out "$fixture/api.pfx" -inkey "$fixture/api.key" -in "$fixture/api.crt" -passout pass:synthetic-transport-only >/dev/null 2>&1
chmod 644 "$fixture/api.pfx" "$fixture/api.crt"
chmod 600 "$fixture/api.key"
# Separate synthetic leaf for local redirect/expiry canaries, never mounted into
# the ordinary web service. Only the isolated test runner receives this key.
openssl req -newkey rsa:2048 -nodes -keyout "$fixture/canary.key" -out "$fixture/canary.csr" -subj '/CN=localhost' >/dev/null 2>&1
printf 'subjectAltName=DNS:localhost\nextendedKeyUsage=serverAuth\n' > "$fixture/canary.ext"
for validity in valid expired; do
  days=2; if [[ "$validity" == expired ]]; then days=-1; fi
  openssl x509 -req -in "$fixture/canary.csr" -CA "$fixture/ca.crt" -CAkey "$fixture/ca.key" -CAcreateserial -out "$fixture/canary-$validity.crt" -days "$days" -extfile "$fixture/canary.ext" >/dev/null 2>&1
done
chmod 644 "$fixture/canary.key" "$fixture/canary-valid.crt" "$fixture/canary-expired.crt"
openssl pkcs12 -export -out "$fixture/protection.pfx" -inkey "$fixture/tls.key" -in "$fixture/tls.crt" -passout pass:synthetic-container-only >/dev/null 2>&1
cp "$fixture/protection.pfx" "$fixture/previous.pfx"
chmod 644 "$fixture/ca.crt" "$fixture/pg.crt" "$fixture/tls.crt" "$fixture/protection.pfx"
chmod 644 "$fixture/previous.pfx"
chmod 600 "$fixture/pg.key" "$fixture/tls.key" "$fixture/ca.key"
sudo chown 999:999 "$fixture/pg.key"
"${compose[@]}" up -d --wait db
"${compose[@]}" exec -T db psql -U postgres -d saveit -v ON_ERROR_STOP=1 < infra/postgres/bootstrap-roles.sql
"${compose[@]}" exec -T db psql -U postgres -d saveit -v ON_ERROR_STOP=1 <<'SQL'
ALTER DATABASE saveit OWNER TO saveit_migration_owner;
CREATE ROLE synthetic_deployer LOGIN INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-deployer-only';
GRANT saveit_migration_owner TO synthetic_deployer;
CREATE ROLE synthetic_access LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-access-only';
GRANT saveit_access_operator TO synthetic_access;
ALTER ROLE saveit_app LOGIN PASSWORD 'synthetic-app-only';
ALTER ROLE saveit_search_worker LOGIN PASSWORD 'synthetic-worker-only';
SQL
"${compose[@]}" run --rm deployment migrate
"${compose[@]}" run --rm deployment provision /run/provision.json
"${compose[@]}" run --rm deployment provision /run/foreign-provision.json
# Initialize only the isolated test keyring volume; production storage ownership is deployment configuration.
"${compose[@]}" create api
docker run --rm --user 0 --entrypoint /bin/chown -v saveit-release_keyring:/keys mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 1654:1654 /keys
# A timeout is not startup rejection: fail if a misconfigured process remains alive.
refuses_startup() {
  expected="$1"; shift
  if timeout 30s "${compose[@]}" run --rm --no-deps "$@" > "$fixture/rejected-startup.log" 2>&1; then
    echo 'An invalid Production configuration unexpectedly started.' >&2; exit 1
  else
    result=$?
    if [[ "$result" == 124 ]]; then echo 'Invalid Production startup did not fail promptly.' >&2; exit 1; fi
    if ! grep -Fq "$expected" "$fixture/rejected-startup.log"; then echo 'Startup failed without the expected configuration guard.' >&2; exit 1; fi
  fi
}
refuses_startup 'Development identity requires the Development environment.' -e DevelopmentIdentity__Enabled=true -e Identity__Okta__Enabled=false api
refuses_startup 'Production identity is unconfigured.' -e Identity__Okta__Enabled=false api
refuses_startup 'Enterprise identity requires an explicit persisted, certificate-protected Data Protection key ring.' -e Identity__KeyDirectory= api
refuses_startup 'Data Protection requires exactly one bounded protected PFX source' -e Identity__KeyCertificateBase64=synthetic-invalid-source api
refuses_startup 'Synthetic migration/seed command requires explicit development identity opt-in.' api --migrate
refuses_startup 'Synthetic identity fixture requires ContainerTest environment.' -e ASPNETCORE_ENVIRONMENT=Production -e DOTNET_ENVIRONMENT=Production identity
refuses_startup 'API TLS requires a separate bounded valid server certificate' -e Deployment__TrustedProxies__0=172.30.100.20 api
refuses_startup 'API TLS requires a separate bounded valid server certificate' -e Kestrel__Endpoints__Plain__Url=http://0.0.0.0:9999 api
refuses_startup 'API TLS requires a separate bounded valid server certificate' -e Deployment__ApiTls__CertificatePassword=wrong-synthetic-password api
"${compose[@]}" up -d api worker identity web edge
for attempt in $(seq 1 60); do
  if curl --silent --fail --cacert "$fixture/ca.crt" --resolve saveit.localhost:8443:127.0.0.1 https://saveit.localhost:8443/health/ready >/dev/null; then break; fi
  if [[ "$attempt" == 60 ]]; then echo 'Production readiness did not become healthy.' >&2; exit 1; fi
  sleep 2
done
for attempt in $(seq 1 30); do
  if curl --silent --fail --cacert "$fixture/ca.crt" --resolve idp.localhost:8443:127.0.0.1 https://idp.localhost:8443/.well-known/openid-configuration > "$fixture/discovery.json"; then
    node --input-type=module -e "import fs from 'node:fs'; import assert from 'node:assert/strict'; assert.equal(JSON.parse(fs.readFileSync('.local/container-fixture/discovery.json','utf8')).issuer,'https://idp.localhost:8443');"
    break
  fi
  if [[ "$attempt" == 30 ]]; then echo 'Synthetic HTTPS discovery did not become available.' >&2; exit 1; fi
  sleep 1
done
wait_for_worker() {
  for attempt in $(seq 1 35); do
    if "${compose[@]}" exec -T worker dotnet SaveIT.Workers.dll --healthcheck >/dev/null 2>&1; then return; fi
    if [[ "$attempt" == 35 ]]; then echo 'Worker readiness never became healthy.' >&2; exit 1; fi
    sleep 1
  done
}
wait_for_worker
"${compose[@]}" run --rm --no-deps \
  -v "$root/tests/production/transport-canaries.mjs:/app/transport-canaries.mjs:ro" \
  -v "$fixture/canary.key:/run/canary/key:ro" -v "$fixture/canary-valid.crt:/run/canary/cert:ro" \
  -v "$fixture/canary-expired.crt:/run/canary/expired:ro" web transport-canaries.mjs
"${compose[@]}" exec -T api dotnet SaveIT.Api.dll --healthcheck
if "${compose[@]}" exec -T api dotnet SaveIT.Api.dll --healthcheck --migrate; then echo 'Mixed health arguments unexpectedly succeeded.' >&2; exit 1; fi
if "${compose[@]}" exec -T -e SAVEIT_HEALTH_HOST=http://api api dotnet SaveIT.Api.dll --healthcheck; then echo 'Malformed health host unexpectedly succeeded.' >&2; exit 1; fi
for image in api worker deployment web; do
  user="$(docker image inspect "saveit-$image:test" --format '{{.Config.User}}')"
  [[ -n "$user" && "$user" != 0 && "$user" != root ]]
done
npx playwright test --config playwright.production.config.ts
# Real DB outage must invalidate worker progress readiness, independently of process liveness.
wait_for_worker
"${compose[@]}" stop db
for attempt in $(seq 1 15); do
  if ! "${compose[@]}" exec -T api dotnet SaveIT.Api.dll --healthcheck >/dev/null 2>&1; then break; fi
  if [[ "$attempt" == 15 ]]; then echo 'API readiness stayed healthy after database failure.' >&2; exit 1; fi
  sleep 1
done
for attempt in $(seq 1 35); do
  if ! "${compose[@]}" exec -T worker dotnet SaveIT.Workers.dll --healthcheck >/dev/null 2>&1; then break; fi
  if [[ "$attempt" == 35 ]]; then echo 'Worker readiness stayed healthy after database failure.' >&2; exit 1; fi
  sleep 1
done
echo 'Production container smoke checks passed with synthetic HTTPS identity and verified database TLS.'
