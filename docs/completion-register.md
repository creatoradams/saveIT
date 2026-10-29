# SaveIT completion register

Updated 2026-10-29. The original brief and all 145 capability entries remain in scope. This register distinguishes working code from missing implementation and real-account prerequisites. It does not mark the enterprise program complete.

| Area | Completed and locally tested | Still required |
| --- | --- | --- |
| Foundation / repository | SaveIT branding, ASP.NET/Next/TypeScript/PostgreSQL monorepo, dependency locks, migrations, generated schemas, ignore rules, CI | Release artifacts, patch/runtime servicing, SBOM/signing, production configuration and operational pipeline |
| Tenant boundary | Protected server-selected tenant, live membership/org roles, composite FKs, all 15 tenant tables forced RLS, non-owner roles and reachable-privilege guards | Advanced groups/deny inheritance, permission administration and in-flight revocation barrier, cross-account authority |
| Identity | Development adapter and separate Okta code/PKCE adapter, exact subject mapping, signed synthetic success/denial tests, secure cookie/key guards | Real Okta app/issuer, assignment and MFA/assurance verification, lifecycle/SCIM/SAML, multiple connections, break-glass and key recovery operations |
| Core records | Authorized organization selection, persistent assets/documents/relationships, scoped locations/contacts, archival/reactivation, asset placement and versions | Organization administration/pagination, richer person/location/configuration schemas, flexible assets/templates, bulk operations/graphs |
| History | Immutable document and asset snapshots, audited history, plain-text document comparison, confirmed restore as a new version, races/conflicts/atomic rollback | Asset comparison, rich content, directory history, deletion recovery, retention and actual backup restore |
| Search / events | Real PostgreSQL full-text projection, current-source/grant checks, ranked bounded pages, restricted worker, atomic pending queue, retries/dead letters/replay/rebuild | OpenSearch, relevance/load evidence, files/facets/autocomplete, generic dispatch/Temporal/connectors and production operator controls |
| Browser | Responsive accessible native controls, request cancellation/retry, edit/restore conflict UX, role controls, real browser/axe checks | Expert WCAG/screen-reader assessment, representative user research, command palette, saved views and virtualization |
| Reviews | Separate efficiency and security agents reviewed code; reported defects fixed and regressions added | Independent penetration/deployment/cryptographic assessment, load tests and operational drills |
| Enterprise expansion | Architecture and complete phased inventory retained | Rich editor/private files, integrations/workflows, vault/OTP/rotation/offline, portals/co-management, discovery/mobile/extensions, Office editing, AI |
| Hosting / operations | Non-provisioning Terraform scaffold validated by CI; local stack needs no cloud account | Actual AWS environment, regions/residency/budget, private services/keys, quotas/telemetry/audit archives, backup/failover and measured SLOs |

## External prerequisites confirmed by the user

- Identity provider: **Okta**. A real tenant/application, issuer and client configuration have not been supplied. The setup procedure is in [ADR 010](adr/010-okta-identity.md); secrets must be provided through protected configuration.
- Nonproduction AWS account and initial integration service: **none yet**. No account, infrastructure, paid service, connector or public application was created. Terraform remains a scaffold.
- Licensing/entitlements, budget, target regions/residency, regulated-data scope, support commitments and actual connector priorities remain product decisions. They do not prevent implementing the next local core features.

## Next implementation sequence

1. Add trusted organization administration and a paginated organization picker, preserving denial/revocation/atomicity tests. Then add versioned typed asset schemas and templates with explicit JSONB validation/migration rules.
2. Expand identity administration/lifecycle and audited action-level grants, while configuring and testing actual Okta in an isolated authorized environment. Define an in-flight revocation protocol before permission administration is exposed.
3. Add rich documentation/private attachments with quarantine, isolated parsers, authorized delivery and retention; add deletion recovery with tested access semantics.
4. Establish an AWS nonproduction environment and operational controls before connecting external systems. Implement one selected connector with real credentials, checkpoint/reconciliation and uncertain-outcome recovery; then expand workflows and each inventory item independently.
5. Complete the separately reviewed vault/key/recovery design before any real secrets, followed by broader companion capabilities and measured enterprise readiness gates.

The full [backlog](backlog.md) specifies every remaining area. Missing code is not described as blocked merely because no AWS account exists. The current work completes additional local features and review remediation; it does not complete the full enterprise request.
