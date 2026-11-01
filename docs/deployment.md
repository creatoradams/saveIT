# SaveIT release artifacts and local production verification

The application is still a preproduction system. See [production readiness](production-readiness.md) and [ADR 022](adr/022-production-release-foundation.md). This guide describes artifacts/tooling and synthetic verification; it does not authorize cloud provisioning or use of customer credentials/data.

## Separate identities and migration

1. An authorized cluster administrator reviews `infra/postgres/bootstrap-roles.sql`. It creates safe fixed roles if absent and refuses elevated preexisting roles. There are no passwords, cloud calls or tenant records in that script.
2. Create an empty UTF8 PostgreSQL database owned by `saveit_migration_owner`. Grant that role only to a separately controlled deployment login. Provision workload logins/secrets separately. Never grant deployment ownership to the API/worker, including NOINHERIT reachability.
3. Provide `SAVEIT_DEPLOYMENT_CONNECTION` through protected job configuration with `SSL Mode=VerifyFull` and the trusted root certificate. No default connection or credential argument is accepted. `dotnet SaveIT.Deployment.dll migrate` applies forward migrations, validates the exact release boundary and grants runtime read access only to the schema-history marker column. Inspect migration SQL and rehearse upgrades before a real deployment.
4. Prepare a private bounded JSON manifest shaped like the synthetic `tests/production/provision.json`, using explicitly chosen tenant/member/org IDs and the actual verified issuer/subject. Do not commit it. `dotnet SaveIT.Deployment.dll provision <protected-file-path>` creates one initial tenant/administrator/org atomically. It never upserts existing authority; duplicates fail and require review. `operator` is declared privileged-operator attribution, not proof of an authenticated operator session.

The local test-only connection escape is `SAVEIT_DEPLOYMENT_ALLOW_LOOPBACK=true`; runtime equivalent `Deployment__AllowLoopbackDatabase=true`. Both permit plaintext only for literal loopback/localhost, never a remote service or multi-host connection. Real deployment uses certificate/hostname-verified TLS. Shared connection normalization explicitly disables GSS transport and refuses an explicit GSS Require setting, so default/ambient negotiation cannot replace the intended certificate TLS path. The packaged fixture does not use the escape.

Owner policies are explicitly restricted to the isolated migration owner. They do not replace tenant policies, remove forced RLS or grant runtime owner/DDL access. Production functions/backfill privileges must be verified on the chosen managed database before deployment. Development seed databases are not automatically adopted into this ownership model.

## Images and runtime configuration

Build from the repository root:

```sh
docker build -f infra/containers/dotnet.Dockerfile --target api -t saveit-api:review .
docker build -f infra/containers/dotnet.Dockerfile --target worker -t saveit-worker:review .
docker build -f infra/containers/dotnet.Dockerfile --target deployment -t saveit-deployment:review .
docker build -f infra/containers/web.Dockerfile -t saveit-web:review .
```

All four run as nonroot and use digest-pinned base images. Keep build locks tracked, audit/patch bases at release and promote the same reviewed images across environments. Never run `npm run dev` or the embedded development database in a deployment.

API: supply `ConnectionStrings__App`, explicit `AllowedHosts`, `DevelopmentIdentity__Enabled=false`, the real `Identity__Okta__*` settings from ADR 010, and persisted certificate-protected key configuration. It refuses unconfigured nondevelopment identity, dev identity, insecure remote database TLS, wrong schema and elevated/owner/DDL runtime roles. The key directory must be writable only by the application identity and survive container replacement. Optional `Identity__PreviousKeyCertificates__0__Path`/`Password` (at most four) retain prior private certificates for a controlled decryption window; expiry does not prevent decrypting old keys. Do not share rings across environments. This is not automated key rotation/recovery.

Configure `Deployment__TrustedProxies__0` and/or `Deployment__TrustedNetworks__0` with only actual ingress peers/subnets, backed by restricted network access. Forwarded protocol is processed before secure-cookie CSRF; an untrusted header alone must not change HTTPS authority. Unconfigured trust is disabled. A trusted network is not a replacement for service security groups and private upstream access. End-to-end deployed TLS/Host behavior still needs staging verification.

Web: set runtime `SAVEIT_API_ORIGIN` to the private API origin, `SAVEIT_PUBLIC_ORIGIN` to the exact public HTTPS origin, and `SAVEIT_OKTA_ISSUER` to the same validated issuer. These are server-only settings. The standalone server accepts `HOSTNAME`/`PORT` (image defaults 0.0.0.0/3000). Public Host validation applies to application/API requests; the generic readiness path supports load-balancer probes. Script nonces require dynamic/no-store rendering. Inline style support remains; broader CSP/client accessibility review is still needed.

Worker: supply `ConnectionStrings__SearchWorker` and the explicit bounded `SearchWorker__Tenants__0...` allowlist. Set a private writable `SearchWorker__HealthPath` if desired (default temp directory). Health command: `dotnet SaveIT.Workers.dll --healthcheck`. It must become healthy before failure-transition tests are meaningful. Committed progress updates health during long tenant cycles; unresolved tenant failures or a stale/malformed snapshot return nonzero. Startup/once/rebuild/replay use the same restricted role/TLS/schema checks. The probe is not a promise that the queue has no dead letters or that a delivery SLO is met.

`/health/live` reports API process liveness; `/health/ready` checks connectivity, runtime role and required forced-RLS schema/release (Production). The web `/health/ready` checks its private upstream. Dependency failure produces generic 503 without internal details. Health does not grant record access or bypass current membership/read audits.

## Synthetic packaged test

On Linux with Docker/Compose, OpenSSL, Node24 and Chromium available, build the four images above using `:test` tags, and also build the `identity-fixture` target as `saveit-identity-fixture:test`. Then run `bash scripts/test-production.sh`. The production-artifacts CI workflow does this in an isolated runner.

The script creates short-lived synthetic CA/TLS/key-protection material under ignored `.local/container-fixture`, starts real PostgreSQL with verified TLS, bootstraps the separate roles, runs the actual deployment image without seeding, provisions two synthetic tenant/org scopes and starts readonly nonroot application containers behind TLS. It checks actual signed OIDC/PKCE, secure session cookies, persistence, a populated foreign scope denial, prior-key/cookie recovery after restart, CSP nonce/no-store behavior and positive-to-negative API/worker health transitions. No official provider account is involved. Browser TLS uses a fixture-only certificate-error exception, while curl and database clients verify their fixture CA.

Only the dedicated test target includes the synthetic provider. It refuses environments other than ContainerTest; never deploy that target as identity. Test CA/PFX/private keys, database files, traces and screenshots must remain ignored and must never enter app images or Git. Current results and limitations belong in progress after actual execution.

## Remaining production work

Protected release promotion/SBOM/signing/scanning, real managed-database and provider tests, audited operational membership/grant lifecycle and in-flight revocation, telemetry/quotas/audit archives, actual backup/key recovery, measured capacity/SLOs and independent assessment remain separate gates. Terraform/cloud resources remain unapplied. All 146 enterprise scope entries remain tracked.

## Isolated recovery rehearsal

ADR 023 extends the packaged tests with a synthetic logical recovery drill. The third browser flow creates current/revision content, retains its session and invokes `scripts/rehearse-recovery.sh`. That helper targets only the disposable `saveit-release` Compose graph. It stops API/worker writers, dumps the database, snapshots persisted keys and both current/prior protectors, checks backup hashes and restores into `saveit_recovery` and a separate `recovered_keyring` volume. Original targets are retained. Protected-table/migration counts must match before restarting writes; the normal restricted worker rebuilds search and the same images start under the normal runtime guards. The browser checks its existing cookie, exact snapshots, tenant denial, rebuilt search and a new audited write.

Run this only through the synthetic packaged suite. It is not a general production restore tool. Backup/key material and test evidence stay under ignored `.local`/test output paths. This drill retains the same cluster/roles/CA and uses a fixture administrator to preserve object ownership/grants. It does not establish independent-cluster or managed point-in-time restoration, offsite/immutable backups, KMS recovery or achieved RPO/RTO. Execution status is recorded in progress after actual Linux CI, not inferred from these definitions.
