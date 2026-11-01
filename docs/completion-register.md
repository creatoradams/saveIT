# SaveIT completion register

Updated 2026-11-01. The original brief and all 145 prior capability entries remain in scope, with a separate organization-administration row bringing the inventory to 146. This register distinguishes working code from missing implementation and real-account prerequisites. It does not mark the enterprise program complete.

| Area | Completed and locally tested | Still required |
| --- | --- | --- |
| Foundation / repository | SaveIT branding, ASP.NET/Next/TypeScript/PostgreSQL monorepo, dependency locks, migrations, generated schemas, ignore rules, CI; seed-free deployment tool, nonroot image definitions, runtime configuration/readiness | Packaged synthetic CI passes; patch/runtime servicing, SBOM/signing and operated promotion pipeline remain |
| Tenant boundary | Protected server-selected tenant, live membership/org roles, composite FKs, all 25 tenant tables forced RLS, non-owner roles and reachable-privilege guards | Advanced groups/deny inheritance, permission administration and in-flight revocation barrier, cross-account authority |
| Identity | Development adapter and separate Okta code/PKCE adapter, exact subject mapping, signed synthetic success/denial tests, secure cookie/key guards | Real Okta app/issuer, assignment and MFA/assurance verification, lifecycle/SCIM/SAML, multiple connections, break-glass and key recovery operations |
| Core records | Audited paged organization selection and limited create/rename administration, persistent assets/documents/relationships, scoped nested folders/independent filing, scoped locations/contacts, archival/reactivation, asset placement/versions, bounded typed fields, immutable asset/document template versions and reviewed SOP starters/provenance | Folder history/lifecycle/ACLs and larger/bulk hierarchies, broader organization lifecycle/permission administration, richer person/location/configuration schemas, nested/computed fields, bulk schema migrations/operations/graphs |
| History | Immutable organization-name/document/asset/location/contact snapshots, historical asset-location name pins with honest legacy unknowns, audited document/document-template/asset/directory comparison, confirmed exact restore including directory lifecycle, races/conflicts/atomic rollback | Document-template restore/reapply, organization restoration/lifecycle, rich content/merges, deletion recovery, retention and managed/offsite backup recovery |
| Search / events | Real PostgreSQL full-text projection, current-source/grant checks, ranked bounded pages, restricted worker, atomic pending queue, retries/dead letters/replay/rebuild | OpenSearch, relevance/load evidence, files/facets/autocomplete, generic dispatch/Temporal/connectors and production operator controls |
| Browser | Responsive accessible native controls, request cancellation/retry, edit/restore conflict UX, role controls, real browser/axe checks | Expert WCAG/screen-reader assessment, representative user research, command palette, saved views and virtualization |
| Reviews | Separate efficiency and security agents reviewed code; reported defects fixed and regressions added | Independent penetration/deployment/cryptographic assessment, load tests and operational drills |
| Enterprise expansion | Architecture and complete phased inventory retained | Rich editor/private files, integrations/workflows, vault/OTP/rotation/offline, portals/co-management, discovery/mobile/extensions, Office editing, AI |
| Hosting / operations | Nonroot packaged runtime and isolated synthetic logical DB/key recovery verified in CI; unapplied Terraform state bootstrap has mocked tests; local stack needs no cloud account | Actual AWS environment, regions/residency/budget, private services/keys, quotas/telemetry/audit archives, backup/failover and measured SLOs |

## External prerequisites confirmed by the user

- Identity provider: **Okta**. A real tenant/application, issuer and client configuration have not been supplied. The setup procedure is in [ADR 010](adr/010-okta-identity.md); secrets must be provided through protected configuration.
- Nonproduction AWS account and initial integration service: **none yet**. No account, infrastructure, paid service, connector or public application was created. The application cell remains a scaffold; state bootstrap is defined but unapplied.
- Licensing/entitlements, budget, target regions/residency, regulated-data scope, support commitments and actual connector priorities remain product decisions. They do not prevent implementing the next local core features.

## Production-first implementation sequence

The [production-readiness reassessment](production-readiness.md) supersedes the previous feature-first ordering. SaveIT is a tested local preview, with no live deployment/Okta or operational recovery evidence. A small documentation/inventory pilot is proposed; confidential customer data still requires the applicable gates.

1. Release foundation implemented and packaged synthetic CI verified: separate seed-free migration/role/tenant bootstrap, production web/API/worker artifacts, configuration validation, liveness/readiness and packaged Production-mode CI. Preserve all current authority/RLS/atomicity/worker boundaries.
2. Establish authorized isolated AWS staging and verify target managed-PostgreSQL role/function/backfill behavior, TLS/private networking, secrets/persisted keys, immutable artifact deployment and rollback. No resources are provisioned by this plan.
3. Verify real Okta/MFA/assignment/session behavior and implement audited restricted onboarding/offboarding plus in-flight revocation. The current live adapter maps one configured issuer to one SaveIT tenant; multi-customer connections and lifecycle require further implementation.
4. Add production telemetry/quotas/authentication-denial-admin audit/archives and worker operations; exercise actual DB/config/key recovery, load tests, independent security/accessibility assessment and incident/support runbooks. Use the evidence to gate a controlled pilot and later broader launch.
5. Continue unfinished product scope, including append-only template restoration, richer documentation/private attachments and deletion recovery, followed by real connectors/workflows and the separately reviewed vault/key/recovery design. Dynamic/bulk/cross-organization templates, companion capabilities and verified enterprise parity remain in the full backlog.

The full [backlog](backlog.md) specifies every remaining area. Missing code is not described as blocked merely because no AWS account exists. The current work completes additional local features and review remediation; it does not complete the full enterprise request.
