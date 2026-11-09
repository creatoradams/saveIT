# SaveIT completion register

This is a current capability summary, not a release approval. [Progress](progress.md) owns exact verification and security snapshots; [readiness](production-readiness.md) owns production gates. The [original brief](project-brief.md), [full backlog](backlog.md) and all 146 [inventory entries](feature-parity.md) retain the complete enterprise scope.

## Implemented and unfinished

| Area | Completed and tested | Still required |
| --- | --- | --- |
| Foundation / repository | SaveIT branding, ASP.NET/Next/TypeScript/PostgreSQL monorepo, dependency locks, migrations, generated schemas, ignore rules, CI; seed-free deployment tool, nonroot packaged runtime/readiness, source security and actual four-image unsigned inventories/vulnerability gates | Signing/provenance, protected immutable registry promotion/rollback, image license policy, operated servicing and remaining lower findings |
| Tenant boundary | Protected server-selected tenant, live membership/org roles, composite FKs, all 28 tenant tables forced RLS, non-owner roles and reachable-privilege guards | Advanced groups/deny inheritance, delegated permission UI, operated fencing/recovery and cross-account authority |
| Identity | Development adapter and separate Okta code/PKCE adapter, exact subject mapping, signed synthetic success/denial tests, secure cookie/key guards; restricted audited operator onboarding/offboarding/grants/capability and durable response drain (ADR 025) | Real Okta app/issuer, assignment and MFA/assurance verification, lifecycle/SCIM/SAML, multiple connections, break-glass and key recovery operations |
| Core records | Audited paged organization selection and limited create/rename administration, persistent assets/documents/relationships, scoped nested folders/independent filing, scoped locations/contacts, archival/reactivation, asset placement/versions, bounded typed fields, immutable asset/document template versions and reviewed SOP starters/provenance | Folder history/lifecycle/ACLs and larger/bulk hierarchies, broader organization lifecycle/delegated permission administration, richer person/location/configuration schemas, nested/computed fields, bulk schema migrations/operations/graphs |
| History | Immutable organization-name/document/asset/location/contact snapshots, historical asset-location name pins with honest legacy unknowns, audited document/document-template/asset/directory comparison, confirmed exact restore including directory lifecycle, races/conflicts/atomic rollback | Document-template restore/reapply, organization restoration/lifecycle, rich content/merges, deletion recovery, retention and managed/offsite backup recovery |
| Search / events | Real PostgreSQL full-text projection, current-source/grant checks, ranked bounded pages, restricted worker, atomic pending queue, retries/dead letters/replay/rebuild | OpenSearch, relevance/load evidence, files/facets/autocomplete, generic dispatch/Temporal/connectors and production operator controls |
| Browser | Responsive accessible native controls, request cancellation/retry, edit/restore conflict UX, role controls, real browser/axe checks | Expert WCAG/screen-reader assessment, representative user research, command palette, saved views and virtualization |
| Reviews | Separate efficiency and security agents reviewed code; reported defects fixed and regressions added | Independent penetration/deployment/cryptographic assessment, load tests and operational drills |
| Enterprise expansion | Architecture and complete phased inventory retained | Rich editor/private files, integrations/workflows, vault/OTP/rotation/offline, portals/co-management, discovery/mobile/extensions, Office editing, AI |
| Hosting / operations | Nonroot runtime and isolated synthetic logical DB/key recovery verified in CI; unapplied mocked state/private network/RDS/EFS, workload/relay-IAM and configuration references, image/log stores, guarded task/endpoint/discovery definitions and own-log-key bindings; bounded injected protectors and local synthetic read-load harness | Compute/ingress/egress, scheduler/PassRole/operator/signing/promotion, collection/redaction/telemetry/quotas engineering, then actual AWS/Okta/secret injection/log ingestion/managed mount/backup/restore/fencing/capacity verification |

## External prerequisites confirmed by the user

- **Okta** is selected. A real issuer/application, assignment and MFA/assurance configuration have not been supplied. The adapter is implemented; live provider behavior remains unverified. See [ADR010](adr/010-okta-identity.md).
- **No nonproduction AWS account or initial integration service exists yet.** No paid service, connector, cloud resource or public application has been created.
- Region/residency, budget, licensing/entitlements, regulated-data scope, support commitments and connector priorities remain product decisions. They do not prevent the next local engineering work.

## Delivery order

1. Complete private service/network attachment, registry/health/drain and ingress/egress bindings consuming the unapplied [ADR047](adr/047-isolated-relay-task-and-web-target.md) task contracts, ADR046 network/discovery and ADR045 endpoint filters.
2. Complete protected population, signing/promotion/rollback, scheduler/PassRole/operator paths, health/drain/fencing and telemetry/quotas/audit archive engineering.
3. With authorized staging accounts, verify managed PostgreSQL roles/TLS/KMS, actual workload isolation, real Okta/access lifecycle, multi-instance keys and managed/offsite recovery.
4. Measure capacity/recovery, remediate findings and obtain independent assessment and an approved pilot scope.
5. Continue remaining product scope: richer documentation/private files, template restore/reapply, deletion recovery, actual connectors/workflows and the separately reviewed vault/key/recovery design, followed by broader SaaS and companion capabilities.

Missing implementation is not blocked solely by absent AWS credentials. The next step remains account-free. Historical release counts and superseded checkpoints are in [delivery history](progress-history.md); the [previous register snapshot](https://github.com/creatoradams/saveIT/blob/3711107/docs/completion-register.md) is retained in Git.
