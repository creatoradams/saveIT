# SaveIT architecture

Status: working local core and unconfigured Okta adapter, 2026-10-29. [Original enterprise brief](project-brief.md) remains the scope of the product. This is not a production release.

## Repository inspection and choices

The target repository started at `de89d01` with a short README and Visual Studio `.gitignore`. There were no application projects, tests, migrations, or instruction files. The existing ignore rules were retained and extended. Next.js subsequently generated `apps/web/AGENTS.md`; it is retained. Docker was installed but its daemon was unavailable, so the documented local path uses real, loopback-only PostgreSQL binaries through a pinned development dependency. This does not substitute an in-memory database.

Use .NET 10 LTS, ASP.NET Core, EF Core 10, PostgreSQL 18, Next.js 16 App Router, React 19, and strict TypeScript. Versions are pinned in `global.json`, project references, npm/NuGet lockfiles, and the local tool manifest. The installed SDK is 10.0.401; managed EF dependencies are 10.0.14. Update the SDK/runtime to current servicing patches before any release. The [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) and [Next.js documentation](https://nextjs.org/docs/app/getting-started/installation) were checked on 2026-10-28.

## Runtime and boundaries

```mermaid
flowchart LR
  B[Browser: HttpOnly session + CSRF] --> W[Next.js: same-origin API proxy]
  W --> A[ASP.NET Core: session + membership + organization policy]
  A --> D[(PostgreSQL: forced RLS + composite foreign keys)]
  D --- O[Transactional audit and outbox tables]
  O --> Workers[Restricted .NET search consumer]
  Workers --> P[(Derived PostgreSQL full-text projection)]
  P --> A
```

`apps/web` owns interaction and presentation. It never decides authorization. It uses TypeScript schemas generated from the API's OpenAPI document in `contracts/`. The API owns the core platform, basic documentation, and initial policy/audit code. Keep explicit interfaces and module-owned data as modules grow; the first small application shares a DbContext to commit core changes and audit/outbox atomically. No worker may query another module's tables ad hoc in the future.

`services/search` owns a rebuildable PostgreSQL full-text projection and its query/consumer implementation; `services/workers` runs the real tenant-scoped consumer. Core exposes a security-invoker `CoreSearchSources` read view with underlying RLS. The worker has no write privileges to core/outbox/audit/identity tables. `infra/terraform` is a validated structure for deployment decisions, with no purchased or deployable cloud resources. PostgreSQL is the only active external service. Redis, OpenSearch, OpenFGA, Temporal, private S3, KMS and OpenTelemetry remain planned. The Okta adapter is implemented but unconfigured and live-unverified (ADR 010). See [ADR 006](adr/006-search-and-delivery.md) for the intentional first-search implementation choice.

The first UI uses a small original stylesheet and native accessible controls. Tailwind and a larger reusable component system are deferred UI implementation work; no commercial editor/component entitlement is assumed.

## Tenant and authorization boundary

A subscribing tenant is distinct from its managed organizations. The encrypted/signed ASP.NET cookie carries a server-selected membership, tenant, and subject. Each protected request opens a PostgreSQL transaction, applies transaction-local `app.tenant_id`, and checks that the membership is still active. Every organization endpoint looks up a current organization grant. No tenant header, payload field, organization selection, or browser state grants authority.

There are two scoped roles: Viewer reads assets/history/relationships, locations/contacts, document bodies/revisions/comparisons and authorized search; Editor additionally creates/revises/restores assets/documents, edits/archives/reactivates directory records, creates/removes links and reads organization audit history. Unknown or inaccessible organizations and records return 404; forbidden actions inside a readable organization return 403. Revoked membership returns 401. No permission cache exists. Search joins live membership/grants and current exact source state before returning any title, plain-text preview or cursor. Stale/mismatched projections are excluded; committed revocation takes effect on the next request without waiting for indexing. Asset relationships never grant access. See [ADR 002](adr/002-authorization-consistency.md) for in-flight concurrency limits.

All fifteen tenant tables have forced PostgreSQL RLS. API and worker roles are neither table owners, superusers, nor BYPASSRLS roles; startup rejects ownership and reachable privileged/owner roles. Worker validation also rejects core table/column writes reachable through NOINHERIT membership. EF tenant filters and write guards add application defenses. Tenant-qualified foreign keys prevent documents/projections, asset placements/history and either relationship endpoint from crossing tenant **or managed organization** boundaries. The application role cannot alter identity grants, update/delete revisions/audit, rewrite relationship endpoints/type/creation metadata, physically delete directory records/links, or directly write projections/consumer receipts. Worker tenants come from an explicit operator-configured allowlist; each job transaction sets its own tenant context. Database owners remain privileged; this is not an immutable archive. See [PostgreSQL RLS semantics](https://www.postgresql.org/docs/18/ddl-rowsecurity.html).

## Implemented model

| Record | Scope and constraints |
| --- | --- |
| Tenant | Subscribing account; RLS on Id |
| Membership | Tenant + canonical subject; active flag; separately provisioned CanManageOrganizations capability; nullable exact external issuer/subject pair with scoped uniqueness |
| Organization | Managed entity; alternate key (TenantId, Id), bounded name, optimistic version/update time |
| OrganizationGrant | Tenant/membership/organization key; checked Viewer/Editor role |
| Location / Contact | Tenant/organization-qualified IDs; bounded plain-text fields, optimistic versions, retained archival |
| Asset | Tenant/organization/Id key; bounded name/type, optional scoped location; optimistic version, created/updated timestamps |
| AssetRevision | Append-only tenant/asset/version key; organization, exact name/type/location, actor/time, source/reason provenance with scoped FKs/check |
| AssetRelationship | Two tenant/organization-qualified asset FKs; directed type, active uniqueness, optimistic lifecycle version, creation/removal actor/time; restricted update columns |
| Document | Tenant/organization/asset FK; title, plain text, optimistic integer version |
| DocumentRevision | Append-only tenant/document/version key; actor, exact title/content snapshot; nullable restore source/reason with composite FK/check |
| AuditEvent | Tenant, organization, actor, target, action, timestamp, outcome, correlation; optional numeric source/target versions; no content/reason |
| OutboxEvent | Tenant, organization, opaque target, type, schema version, timestamp, delivery marker |
| SearchEntry | Derived tenant/type/target key, scoped asset FK, version, current source snapshot, generated GIN-indexed full-text vector |
| SearchDelivery | Tenant/event FK, attempt/backoff, completion/dead-letter timestamps, safe error code; search-v1 consumer only |

The remaining required entities are explicitly planned: User/identity directory, Group, AssetType and typed custom-field schema, Attachment, Checklist, CredentialReference, PermissionGrant with inheritance/denies, IntegrationConnection, SyncCheckpoint, WorkflowDefinition, WorkflowRun. Memberships support development identity or preprovisioned Okta subject mappings; this does not implement a user lifecycle system. Basic asset links are implemented; richer relationship types/graphs and relationship-based authorization remain separate future work.

## Transactions, concurrency, and contracts

Endpoint filters commit before executing the HTTP result. Exceptions, stale versions, and failed writes roll back the request transaction. A business write, its revision, its audit event, and its outbox event share a single atomic commit. Document body/revision reads also require a successful audit insert. Revision updates use an expected version in both API validation and the EF update predicate. A conflict returns 409 without overwriting user text or inserting a successful change event.

Organizations/assets/directories/documents/revisions/audit use cursor pages of at most 50 rows. Organization pages use protected tenant/membership-bound name/UUID cursors and current grants before labels or continuation; authorized lookup by ID supports search navigation beyond loaded choices. All organization list/read metadata is audited before serialization. Document lists return metadata only; bodies are loaded through an audited endpoint. No hard delete, export, share, custom fields, secrets, HTML rendering, or uploads are exposed.

Limited organization administration uses a live, separately provisioned capability. Creation gives only its creator an Editor grant to a server-generated brand-new organization; renaming additionally requires an existing Editor grant. Checked SECURITY DEFINER functions validate and lock the current tenant/membership/capability and rename grant, enforce plain creation/expected versions, and share the request's atomic audit/outbox transaction. No direct API organization/grant writes are allowed. The worker guard rejects execution of either function through direct/PUBLIC/reachable roles. No existing permission/grant/capability administration or revocation endpoint is exposed. Synthetic seed restores Alice/Bob's local capability; real provisioning and production definer/forced-RLS operation remain externally controlled/unverified. See [ADR 012](adr/012-organization-administration.md).

Locations/contacts support audited create/read/edit/archive/reactivate with optimistic versions. Assets support versioned metadata changes, active scoped placement, audited history and confirmed restore as a new version; existing archived placements remain readable. Revisions retain exact name/type/location and restoration provenance, with no app update/delete permission. Migration backfill establishes an initial snapshot, not fabricated earlier history. See [ADR 009](adr/009-directory-and-asset-history.md).

Document restoration appends a new revision copied from an earlier snapshot, preserving all history. Expected-version concurrency, current Editor access and CSRF apply again at submission; revision/audit/outbox commit atomically. Restore source/reason are confidential revision metadata; audit records only numeric source/target versions. A composite self-reference and check enforce provenance. Comparison reads are audited and return at most 200 numbered plain-text rows, using bounded LCS or faithful replacement blocks for large edits. See [ADR 007](adr/007-document-recovery.md). This is not deletion recovery or backup restoration.

Directed DependsOn/Contains asset links stay inside one organization. Lists join both currently scoped endpoint records, audit reads and return at most 50 active links. Editor creation/removal commits the relationship/audit/outbox together; active uniqueness and lifecycle version predicates handle duplicate/remove races. Removal retains its row and both assets/documents; a later recreation has a new ID. Search-v1 explicitly acknowledges known schema-v1 link events without projection changes and retains separate receipts. The worker has no relationship-table access, and its startup guard rejects column-level core writes too. See [ADR 008](adr/008-asset-relationships.md); custom types, cross-organization links, cycle enforcement and graph navigation remain planned.

Events are schema-v1 **invalidation metadata**, without document/secret contents. A restricted fixed trigger atomically enqueues a pending search receipt with the outbox write; enqueue failure rolls back the business transaction. The consumer selects due receipts using a matching partial index, avoiding retained completed history and timestamp checkpoint gaps. It rehydrates latest source through the tenant-aware core read contract; invalidations do not reconstruct intermediate versions. Projection and completion receipt commit together. Per-tenant advisory locks serialize consumers/rebuilds, idempotent rehydration and version guards tolerate duplicates/reordering, and failed attempts use bounded exponential backoff/jitter before dead-letter. Trusted CLI commands rebuild or reset scoped receipts; the worker cannot delete receipts. Global `OutboxEvents.DeliveredAt` stays untouched. Generic external dispatch/Temporal workflows remain unimplemented. See [ADR 011](adr/011-transactional-search-queue.md), including production definer/backfill prerequisites.

Search returns at most 25 authorized records with query/principal/scope-bound keyset cursors. Scope/type filters and per-document ranking precede pagination. Current authoritative data supplies all returned labels/previews; stale or inaccessible candidates do not create counts/cursors. Every returned result is audited atomically, without search text/content in events. Projection/source outages return unavailable; there is no stale-cache or direct-query fallback. Bodies remain plain text. Full-text matches complete words, not fuzzy/prefix suggestions, facets, total counts or files/vault values.

## UX, operations, and assumptions

Persistent organization selection, bounded inventory, linked plain-text documents, conflict recovery, revision inspection/comparison, confirmed restoration with reasons, role labels, audit history, and permission-aware search are implemented. Search opens current records through authorized endpoints, clears stale results on scope changes/failures, and reports asynchronous indexing limits. Forms use labels, native controls/dialogs, focus outlines, status/error regions, and a skip link. Responsive/keyboard and automated accessibility checks are part of browser tests. These checks do not constitute a complete WCAG audit or screen-reader/user research evidence.

Local sign-in requires both the Development environment and explicit identity opt-in. It has synthetic profiles, a two-hour HttpOnly SameSite=Strict cookie, antiforgery tokens on mutations including login/logout, and login rate limits. Okta uses code/PKCE, verified issuer/subject mapped to a preprovisioned server-selected tenant, Secure HttpOnly SameSite=Lax cookies and optional exact acr validation; it ignores token tenant/role claims. Nondevelopment startup fails closed without Okta settings and a certificate-protected persisted key ring. No live Okta/MFA or key recovery verification exists. The local Next proxy binds only loopback; no tokens are stored in localStorage. CSP permits inline framework scripts/styles; nonce-based CSP and operated HTTPS/proxy/session-key lifecycle remain release work. See [ADR 010](adr/010-okta-identity.md).

The default deployment direction is AWS regional cells on ECS/Fargate with private RDS, OpenSearch, Redis, S3, KMS, least-privilege identities, and Terraform. No active-active writes. Dedicated deployment/residency options stay on the roadmap. The user chose Okta and has no nonproduction AWS/integration account yet. Self-hosting, licensing, budget, regions, regulated data and support commitments remain unresolved.

Targets remain proposals: API p95 <300 ms, search p95 <700 ms, usable main screen <2 s on a defined client/network, availability 99.95%, regional RPO <=15 min and RTO <=4 h. No load, availability, backup restore, or disaster recovery results are claimed. Record tenant sizes, concurrency, cold/warm state, region, and measurement boundaries when testing them.
