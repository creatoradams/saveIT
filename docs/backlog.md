# SaveIT enterprise backlog

Updated 2026-10-30. Preserve all scope in the [original brief](project-brief.md) and [capability inventory](feature-parity.md). A local slice is not feature parity or enterprise readiness. Each item requires persisted behavior, server authorization, appropriate audit, contracts/migrations, success/denial/recovery tests, accessible loading/empty/error/conflict UX, and updated evidence before being marked done. See the [completion register](completion-register.md) for implemented code, external prerequisites and next delivery dependencies.

## Phase 0 — specification and inventory

- [x] Inspect existing repository and preserve initial work; establish SaveIT branding.
- [x] Record architecture/data model, threats/abuse cases, release risks, initial UX flows and ADRs for tenancy, authorization consistency, identity, deployment and vault design comparison.
- [x] Preserve every enterprise roadmap area and create an explicitly unverified parity inventory.
- [ ] Verify individual capabilities/roles/plan entitlements through official product documentation and authorized access; independently inventory each connector. Marketing wording is insufficient.
- [ ] Decide regions/residency, self-hosting, licensing, budget, regulated-data scope, customer-key ownership/recovery, and support commitments.

## Phase 1 — secure foundation

- [x] ASP.NET/Next.js/PostgreSQL monorepo with locked dependencies, migrations, generated API schemas, test and Terraform/worker scaffolds.
- [x] Development-only allowlisted identity, separate tenants and organizations, live membership/grants, Viewer/Editor actions, composite FKs, forced RLS and restricted runtime-role checks.
- [x] Two synthetic tenants with overlapping Acme Manufacturing/Core switch names, asymmetric organization permissions, cookie/CSRF controls, and meaningful isolation/revocation/concurrency/atomicity tests.
- [x] Atomic successful change audit/outbox baseline; audited document-body/history reads; pending events retained without fake delivery.
- [ ] Enterprise OIDC/SAML broker, Entra/Okta compatibility, phishing-resistant MFA, SCIM, break-glass, session revocation, scoped expiring API tokens, secure production key storage/HTTPS/proxies/CSP.
- [x] First Okta OIDC code/PKCE adapter, exact preprovisioned issuer/subject mapping in a server-selected tenant, ignored provider tenant/role claims, state/nonce/signature/issuer/audience/lifetime checks, secure cookie and configuration/key-ring guards; signed synthetic protocol tests (ADR 010).
- [ ] Connect a real authorized Okta tenant/application; verify assignments, phishing-resistant MFA/assurance and lifecycle policy, TLS/proxies, key persistence/rotation/recovery and real two-tenant browser behavior. User selected Okta; no live tenant/account has been supplied. SAML/multi-connection/SCIM remain unimplemented.
- [ ] User directory, groups/teams, action-level PermissionGrant rules, inheritance/override/deny precedence, OpenFGA adapter and authoritative consistency protocol.
- [ ] Permission administration with revocation barrier for in-flight reads; failed/denied/authentication/admin audit and separate immutable archives/retention/gap detection/SIEM exports.
- [ ] Per-tenant quotas, API/workload rate limits, workload identities, private services, restricted administration and egress, OpenTelemetry redaction and actionable alerts.
- [ ] Patch policy, automated secret/SAST scanning, SBOMs, signed artifacts and reviewed deployment/release pipeline. CI dependency audits are a baseline only.

## Phase 2 — core vertical slices

- [x] Authorized organization selection, persistent create/list assets, linked plain-text documents, append-only revisions with conflict detection, revision inspection, organization audit history.
- [x] Cursor limits for assets/documents/revisions/audit; responsive workspace, native keyboard navigation, error/status regions and initial automated accessibility checks.
- [x] Initial permission-aware asset/document full-text search, scoped/type filters, ranked keyset pages and plain-text previews through a rebuildable PostgreSQL projection and real outbox consumer (ADR 006). Current grants/source/version rechecked before all fields/pagination; two-tenant, stale content, revocation during indexing lag, outage, replay, deduplication, worker-role and audit failure tests pass. No credential content in retrieval.
- [ ] Enterprise OpenSearch adapter, measured relevance/scaling, authorized facets/counts/autocomplete and file search. PostgreSQL is the intentional first implementation; distributed search remains in scope. Preserve final authority checks for every new side channel.
- [x] Document comparison and audited restoration as a new revision: bounded plain-text diff/pages, exact historical content, source/reason provenance, expected-version races, atomic revision/audit/outbox and search catch-up, current tenant/role/revocation checks, fail-closed read audits, confirmation/conflict/retry UX (ADR 007).
- [x] Explicit directed DependsOn/Contains asset relationships with tenant/organization-qualified endpoints/FKs, forced RLS, current Viewer/Editor actions, bounded linked-record navigation, soft removal with version conflicts, atomic audit/outbox and denial/concurrency/recovery tests (ADR 008). Cross-organization links, richer types, graphs/cycle rules and relationship-based authorization remain planned.
- [x] Organization-scoped locations/contacts with bounded plain-text fields, audited pages/reads, create/edit/archive/reactivate, current Viewer/Editor roles, version conflicts, atomic audit/outbox and raw RLS/FK/denial/recovery tests (ADR 009).
- [x] Asset name/type/location editing, tenant/organization-qualified active placement, immutable history and confirmed earlier-snapshot restore as a new version; provenance, optimistic races, atomic revision/audit/outbox, current-source search and browser recovery tests (ADR 009).
- [x] Limited organization creation/rename and paginated selection: separately provisioned live administrator capability, existing Editor requirement for rename, checked database functions, creator-only grant for a brand-new organization, protected 50-row cursors and audited current lookup; concurrency, denial, rollback and beyond-page search navigation tests (ADR 012).
- [ ] Broader organization lifecycle/archive/deletion and controlled administrator provisioning, permission administration with revocation consistency, onboarding and bulk operations. The API cannot manage existing grants or administrator capabilities.
- [x] Versioned organization-scoped typed asset templates and validated JSONB: five bounded field types, immutable pinned definitions, explicit per-asset upgrades, reviewed creation defaults, archive/reactivate, exact typed snapshot restore, atomic record/revision/audit/outbox, audited reads and denial/conflict/rollback tests (ADR 013).
- [ ] Richer contact/location schemas and merges; full configurations, richer/nested flexible schemas, bulk schema migration jobs/previews, cross-organization templates, richer asset relationship types/graph views and bulk updates/previews. Basic directory records/history/recovery/comparison, explicit AssetRelationship and bounded v1 typed assets/templates are implemented.
- [x] Audited asset revision comparison with bounded metadata/custom-field differences, immutable pinned schema labels/rules, explicit presence/definition/value changes, current grants, 20-row pages, literal rendering and fail-closed source/audit handling (ADR 014). Comparison never supplies restore authority or bypasses expected versions.
- [x] Location/contact revision history and recovery: scoped immutable exact snapshots including archive/reactivate, metadata-only 50-row history, audited exact review, current roles/CSRF/expected versions, reasoned restoration as a new version and atomic revision/audit/outbox/queue. Honest current-version migration backfill, worker directory-content exclusion and denial/concurrency/failure/browser regressions (ADR 015).
- [x] Historical location-name pins in asset snapshots: scoped fail-closed capture, exact known/unknown restore, audited literal history/comparison and honest unavailable legacy names (ADR 016). Placement FKs, worker content exclusions and atomic writes remain intact.
- [x] Bounded audited location/contact revision comparison: exact fields/empty text/line endings/lifecycle, two immutable sources, current roles and numeric-only read audit, four-row pages, literal responsive browser review and independent restore version checks (ADR 017).
- [x] Immutable organization name history for create/rename: exact persisted snapshots/actor/origin inside checked functions, current-version migration backfill, scoped FK/forced RLS/API SELECT-only grants, audited current-authorized 50-row pages, worker exclusions and atomicity/denial/migration/browser tests (ADR 018). Organization restoration, permission administration, merges and deletion/backup recovery remain separate work.
- [ ] **Next core task: organization-scoped document folders**, with audited create/move, bounded hierarchy reads, scoped parent/document FKs, cycle/concurrency checks and accessible failure/recovery UX.
- [ ] Rich Tiptap/ProseMirror content with sanitization/isolation, folders, knowledge base/SOP templates, flags, ownership/freshness/review dates, approvals and collaboration; verify commercial licensing before adding extensions.
- [ ] Richer document comparison/merges, deletion recovery and retention, audit coverage for all remaining metadata access as required by policy. Asset/directory scalar comparison, metadata/typed-field/directory history/restore and plain-text document comparison/restore as new versions are implemented; generalized rollback and backup recovery remain absent.
- [ ] Private/versioned attachments, scan/quarantine, isolated parsing/OCR, authorized delivery, safe file references, SSRF defenses and permission-aware file indexing.
- [ ] Command palette, product keyboard shortcuts, saved views, dense accessible tables, list virtualization, explicit relationships/ownership/freshness/sync states, long-job progress/recovery.

## Phase 3 — integrations and workflows

- [x] Real tenant-scoped search-v1 consumer: immutable invalidations, separate transactional receipts, current-source/version-safe idempotent projection, advisory-lock concurrency, five-attempt backoff/jitter, safe-code dead letters, scoped rebuild/replay commands and failure recovery tests.
- [x] Atomic trigger-enqueued pending search queue, preserving backfill, matching partial due index and scoped reset replay; late-commit and enqueue-rollback regressions. Worker cannot delete receipts; no repeated scan through completed outbox history (ADR 011). Production definer/backfill authority remains a release prerequisite.
- [ ] Generic transactional outbox dispatch to additional consumers/external systems, event schema/version evolution, external ordering/reconciliation, production operator administration, scheduling and immutable operational audit. Local search success does not establish connector or exactly-once external effects.
- [ ] Temporal workflow adapter and separate worker pools; do not claim exactly-once external effects. Use opaque credential references, never secret values in workflow history.
- [ ] Connector SDK, scoped service identities, webhooks/public API, IntegrationConnection/SyncCheckpoint models, real imports/exports and migration tooling, checkpoint/reconciliation and explicit sync status.
- [ ] Build each native PSA/RMM/backup/cloud integration independently. See individual leads in the parity matrix, including Autotask, Intune, Entra/AD/M365, Google Workspace, Datto products, Spanning, Unitrends, Vorex, myITprocess, Dark Web ID and BullPhish ID; verify demand/entitlements first.
- [ ] Checklists/runbooks, WorkflowDefinition/WorkflowRun, rules/approvals, notifications, completion/engagement reporting, domain/certificate/warranty/other expiry tracking and alerts.
- [ ] Test retries/rate limits, duplicate events, crashes, uncertain remote outcomes, authentication expiry and safe resumption with a real first connector. Unconfigured connectors stay visibly unconfigured.

## Phase 4 — credential capabilities

- [ ] Resolve server-managed envelope vs client-only encryption through independent review; tenant data keys, KMS permissions/rotation, recovery and device enrollment/sharing design.
- [ ] CredentialReference model and isolated service; metadata/reveal/edit/export/share/delete/admin/rotate actions separated; step-up reveal and complete success/failure audit without secret contents.
- [ ] Password/OTP/encrypted notes/restricted vault, controlled sharing, access tracking, rotation reconciliation with recovery access and uncertain-outcome handling.
- [ ] Customer-controlled encrypted offline access, device binding where feasible, expiry and explicit disconnected-revocation limits.
- [ ] Independent cryptographic/vault and tenant-isolation review before using real credentials. No prototype-vault production claim.

## Phase 5 — broader parity and companion capabilities

- [ ] Customer/help portal, delegated cross-account grants/audit, co-managed IT and partner workflows; independently verify MyGlue/GlueConnect behavior and licensing.
- [ ] Approved signed collectors restricted to configured networks, SNMPv3/directory/cloud discovery, network inventory, topology/diagramming; separate egress/agent threat model and signed safe updates.
- [ ] Mobile applications, minimal-permission browser extensions, signed distribution/updates and controlled offline functionality.
- [ ] External file references, granular file permissions, Office cloud-editing equivalent subject to licensing.
- [ ] Optional replaceable AI: human-reviewed SOP capture, relationship suggestions, stale/duplicate detection, summaries/assistance with source references and current permission rechecks. Exclude vault/OTP/secret values; define provider retention/residency; prompt-injection and cross-tenant leakage tests. Provider remains unconfigured/disabled until authorized.

## Phase 6 — enterprise readiness

- [ ] AWS regional cells and dedicated enterprise deployments with explicit tenant placement/residency/failover, independent API/worker scaling, private managed data and minimal shared control plane; no initial active-active writes.
- [ ] Audit export, branding, retention/deletion policies, access reviews, onboarding/migration and explicit scoped/time-limited/logged support access.
- [ ] Reviewed expand/migrate/contract releases, backups of DB/files/config/key recovery, isolated actual restores, restored-access/deletion checks and regional recovery drills; replication lag/noisy-neighbor monitoring.
- [ ] Defined load workloads covering cold/warm reads, large tenants, imports/search and concurrent permission changes; report tenant distribution, concurrency, region, client/network, and measurement boundaries.
- [ ] Validate/revise proposed API p95 <300 ms, search p95 <700 ms, main screen <2 s, 99.95% availability objective, regional RPO <=15 min/RTO <=4 h. None are currently achieved claims.
- [ ] WCAG 2.2 AA expert and screen-reader testing, representative IT professional research, security penetration testing, threat-model/vault review, operational runbooks, incident/patch response and rollout/rollback evidence.
- [ ] Organizational controls and external examination for any future compliance claim. Source code does not establish SOC 2 or certification.

## Delivery gates

Complete phase 1 only after reproducible local setup/CI, two-tenant evidence and identity/authorization baseline are accepted. Complete a core slice only with real persistence and denial/revocation cases. Integrations need real credentials and recovery evidence. Vault release requires independent review. Enterprise readiness requires measured operations and remediated release blockers; do not remove unmet gates to ship a partial preview.
