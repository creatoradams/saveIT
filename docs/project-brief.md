# Enterprise IT Documentation Platform — Codex Implementation Brief

Prepared: October 28, 2026
Status: Proposed architecture and execution requirements, not an implemented or security-certified system.

## 1. Mission and instructions to Codex

Build an original enterprise IT documentation and operations platform targeting functional parity with IT Glue and room for future features. The product must be fast, approachable, optimized for IT professionals, extensible, and highly secure. Treat credentials and infrastructure documentation as high-value sensitive data.

Use this document as the project brief. Inspect the existing repository and its instructions before changing files. Preserve useful existing work. If starting from an empty repository, scaffold the architecture below. Make reversible implementation decisions autonomously and record assumptions. Ask only about decisions that materially block implementation or require external authorization. Do not stop at a plan or a static interface: deliver working, tested vertical slices.

The entire target scope remains in the backlog even when delivery is phased. Do not describe a partial release as feature-complete or enterprise-ready. Do not invent integration support, security guarantees, performance results, certifications, or completed tests. Never silently substitute mock behavior for working functionality.

Use original branding, interface design, and implementation. IT Glue is a functional benchmark, not a source of proprietary code or copied visual assets.

## 2. Product assumptions

- Default deployment: commercial multi-tenant SaaS, supporting internal IT departments and MSPs.
- A tenant is a subscribing account. A managed organization is an entity inside that account; these are distinct scopes.
- Support multiple teams, managed organizations, locations, delegated users, and controlled cross-account collaboration.
- Plan for regional hosting cells and dedicated enterprise deployments. Do not build active-active global writes initially.
- Default hosting recommendation: AWS. Provider identity must not be confused with customer identity: customers may use Entra ID, Okta, or another compatible IdP.
- End-state parity includes mobile, browser extensions, offline credentials, customer portals, discovery, automation, and companion-product capabilities. Exact parity requires a verified inventory; the list below is a baseline, not proof of completeness.
- Real production credentials, customer data, cloud purchases, external publication, and production migrations require appropriate authorization. Local development with synthetic data can proceed.
- Self-hosting, commercial licensing, budget, target regions, regulated-data scope, and support commitments remain product decisions. Isolate their impact through configuration and documented architecture decisions.

## 3. Recommended stack

| Layer | Default decision | Implementation intent |
|---|---|---|
| Web | React, TypeScript, Next.js | Interactive enterprise application; keep domain logic in backend modules |
| UI | Tailwind and accessible reusable components | Consistent tables, forms, dialogs, shortcuts, and focus behavior |
| Backend | C#, ASP.NET Core, supported .NET LTS | Typed APIs and modular domain logic; verify supported versions at implementation time |
| Data access | Entity Framework Core; explicit SQL for justified hotspots | Migrations, concurrency control, transactional operations |
| System of record | PostgreSQL | Relational core with schema-validated JSONB custom fields |
| Search | OpenSearch | Derived index with relevance, filtering, and permission-aware retrieval |
| Cache | Redis | Disposable caches; never authoritative permissions or records |
| Editor | Tiptap/ProseMirror | Structured content, versioning, attachments, and later collaboration |
| Workflows | Temporal | Durable integration, approval, rotation, and import workflows |
| Authorization | OpenFGA plus backend policy checks | Relationships and granular actions, supplemented by tenant and contextual rules |
| Identity | Enterprise-capable identity broker/provider | OIDC/SAML, MFA, enterprise connections, and SCIM; choose through an ADR |
| Files | S3 | Private attachments, versions, exports, audit archives |
| Keys | AWS KMS | Envelope encryption and separate key-access controls |
| Runtime | Containers on ECS/Fargate and managed data services | Independent scaling without initially operating Kubernetes |
| Infrastructure | Terraform | Reviewed, reproducible environment definitions |
| Observability | OpenTelemetry and centralized metrics/logs/traces | Redaction, actionable alerts, tenant-aware capacity diagnostics |
| Validation | .NET tests, frontend tests, Playwright, load-test tooling | Security boundaries, workflows, accessibility, performance |

Check current licensing and feature entitlements before committing to commercial editor/collaboration features, identity services, and managed services. Record exact versions in lockfiles and maintain a patch policy. Do not introduce another language or database without a concrete requirement and ADR.

## 4. Architecture and module boundaries

Start with a modular core backend and separately deployable services where security or workload isolation warrants it. Avoid both a single undifferentiated codebase and dozens of premature microservices.

| Boundary | Owned responsibilities |
|---|---|
| Core platform | Tenants, organizations, locations, contacts, assets, relationships, templates |
| Documentation | Editor content, revisions, folders, checklists, review dates, collaboration |
| Identity/authorization | Membership, groups, enterprise identity mappings, grants, delegated access |
| Credential subsystem | Passwords, OTP seeds, encrypted notes, reveal, sharing, rotation, recovery |
| Integration workers | Imports, exports, external connectors, reconciliation, discovery agents |
| Search/document processing | Indexing, attachment parsing, OCR, search projections |
| Automation/AI | Rules, approvals, SOP capture, approved document retrieval and suggestions |
| Audit | Sensitive access and change events, retention, SIEM export |

Modules own their data and expose explicit interfaces; prohibit ad hoc cross-module writes. Use versioned REST APIs and OpenAPI-generated clients. Reserve real-time channels for collaboration and event notifications.

Persist business changes and outbox events atomically. Deliver events with retries and deduplication. Assume at-least-once delivery; external effects must be idempotent or reconciled. Temporal durability does not make external API calls exactly-once. Define event schema versions, ordering expectations, replay behavior, dead-letter handling, and operator recovery.

PostgreSQL is authoritative. Search indexes and caches must be rebuildable. Schema migrations should support rolling releases using expand/migrate/contract patterns. Backups are not a substitute for safe migrations.

Scale stateless APIs and worker pools independently. Apply per-tenant quotas and workload concurrency limits. Plan independently operated regional cells with explicit tenant placement, residency, backup, and failover policies. Minimize sensitive information in any shared control plane.

## 5. Feature-parity baseline

Create docs/feature-parity.md with one row per verified capability: source/date, behavior, roles, plan/add-on scope, dependencies, implementation status, tests, known gaps, and phase. Use statuses such as unverified, planned, implemented, verified, blocked. Validate official documentation and authorized product access; never mark parity from marketing wording alone.

| Area | Target capabilities |
|---|---|
| Structured assets | Organizations, contacts, locations, configurations, flexible assets, templates, bulk updates, relationships |
| Documentation | Knowledge base, SOPs, folders, rich editor, attachments, templates, flags, ownership, review schedules, collaboration |
| History | Document/asset revisions, comparison, restore, rollback, deletion recovery, conflict detection |
| Operations | Checklists, runbooks, workflow rules, notifications, completion/engagement reporting |
| Search | Global and scoped search, filters, asset/document/file retrieval, permission-aware suggestions |
| Credentials | Password management, OTP, encrypted notes, restricted vault, access tracking, sharing, offline access, rotation |
| Integrations | Native PSA/RMM/backup/cloud connectors, public API, webhooks, import/export, reconciliation |
| Tracking | Domain, certificate, warranty, and other expiry tracking with notifications |
| Files/editing | External file references, file access controls, Office cloud-editing equivalent subject to licensing |
| Identity | SSO, MFA, lifecycle provisioning, granular permissions, teams, restricted API identities |
| Collaboration | Customer help portal, delegated cross-account access, co-managed IT and partner workflows |
| Discovery | Network inventory, topology/diagramming, approved collectors, SNMPv3 and directory/cloud discovery |
| Clients | Responsive web, mobile applications, browser extensions, controlled offline functions |
| AI | SOP capture, relationship suggestions, stale/duplicate content detection, summaries, permission-aware assistance |
| Enterprise administration | Audit exports, branding, retention, residency, recovery, access reviews, onboarding/migration |

Inventory each native integration independently. A generic connector framework does not establish parity with dozens of implemented connectors. UI feature flags do not satisfy unfinished backend requirements.

## 6. Data model and extensibility

Model at minimum: Tenant, Organization, Location, User, Membership, Group, AssetType, Asset, AssetRelationship, Document, DocumentRevision, Attachment, Checklist, CredentialReference, PermissionGrant, IntegrationConnection, SyncCheckpoint, WorkflowDefinition, WorkflowRun, AuditEvent, and OutboxEvent.

- Tenant-owned records require a non-null tenant identifier and enforceable tenant constraints. Use tenant-qualified foreign keys/uniqueness where appropriate.
- Model subscribing tenant scope separately from managed organization scope. Cross-account sharing requires explicit grants and auditing.
- Core searchable fields remain relational. Custom field definitions have types, validation, schema versions, and migration rules. JSONB must not become an unvalidated dumping ground.
- Credentials belong to the credential subsystem. Core records contain references and safe metadata, not plaintext secret values.
- Use explicit relationship tables before introducing a graph database.
- Enforce optimistic concurrency, stable IDs, cursor pagination, consistent error responses, and deletion/retention semantics.
- Expose extension points through versioned APIs, documented events, a connector SDK, scoped service identities, and audited feature flags.
- Run third-party extensions outside the core application process. Restrict network egress, resources, tenant access, and secret access.

## 7. Security requirements and acceptance evidence

Security is an implementation and operational property, not an attribute guaranteed by a programming language. Maintain a threat model, data classification, abuse cases, security ADRs, and a release-risk register.

### Tenant isolation and authorization

- Enforce authorization server-side on every API, job, file retrieval, export, search result, websocket subscription, and integration operation.
- Derive tenant context from verified identity and membership. Never trust a request tenant ID by itself.
- Deny by default. Separate metadata read, secret reveal, edit, export, share, delete, permission administration, and rotation actions.
- Combine role/group/relationship rules with contextual restrictions as needed. Document inheritance, override, and deny precedence.
- Use PostgreSQL row-level security where appropriate as defense in depth. Test using actual application DB roles; owners and privileged roles may bypass RLS.
- Define authorization consistency across PostgreSQL, OpenFGA, caches, and search. Revocation must take effect before acknowledging success for affected protected reads; fail closed when current authority cannot be established.
- Do not rely only on indexed ACLs. Prevent disclosure through snippets, counts, facets, autocomplete, filenames, and AI retrieval. Validate permission changes during indexing lag.

### Identity and application protection

- Support OIDC/SAML enterprise SSO, SCIM lifecycle management, phishing-resistant MFA, controlled break-glass access, and session revocation.
- Keep browser sessions in secure HttpOnly cookies where appropriate; implement CSRF protection and deliberate origin/CORS rules. Avoid long-lived bearer tokens in browser storage.
- Apply least-privilege workload identities, scoped expiring API tokens, private data services, TLS, rate limits, and restricted administration paths.
- Isolate rich document rendering and uploads. Sanitize content; enforce CSP; scan attachments and quarantine until safe; constrain parsers and outbound fetches against SSRF.
- Browser extensions and discovery collectors require signed releases, minimal permissions, safe updates, and their own threat models. Restrict discovery to explicitly configured networks.

### Credential architecture

- Before production implementation, write an ADR comparing server-managed secrets and client-side-only encrypted secrets. Use reviewed cryptographic designs and maintained libraries; do not invent a cryptographic protocol.
- Server-managed secrets: envelope encryption, restricted key service permissions, key rotation, step-up reveal, complete access auditing, and carefully scoped automation.
- Client-encrypted secrets: explicit key distribution, device enrollment, revocation limitations, recovery design, and independent review. Do not promise server-side search, AI, or unattended rotation over ciphertext that the server cannot decrypt.
- No plaintext secrets in logs, traces, search, analytics, prompts, crash reports, or workflow histories. Pass opaque references into workflows; resolve secrets only in authorized execution contexts.
- Offline access is tenant-controlled, encrypted, device-bound where feasible, and time-limited. A disconnected device cannot immediately enforce revocation; disclose this limitation.
- Rotation must reconcile uncertain external outcomes and preserve recovery access. Handle failure between changing a remote password and storing the resulting value.
- Use synthetic secrets during development. A prototype vault is not approved for production credentials.

### Auditing and operations

- Audit sensitive reads/reveals, changes, exports, permission changes, administration, and emergency access. Include actor, tenant, target, action, timestamp, outcome, and correlation ID without secret contents.
- Protect audit storage in a separate access boundary with retention-controlled immutable archives. Define how failed audit writes affect sensitive operations and how event delivery gaps are detected.
- Use SAST, dependency/secret scanning, SBOMs, signed artifacts, reviewed deployment pipelines, and patch response procedures.
- Conduct independent penetration testing and vault/tenant-isolation review before general production availability. SOC 2 requires organizational controls and external examination; code alone cannot establish compliance.
- Use synthetic data in development; customer support access must be explicit, scoped, time-limited, and logged.

## 8. Performance, resilience, and user experience

Proposed initial targets, to validate and revise through documented load testing:

| Measure | Initial target |
|---|---|
| Ordinary API reads | p95 below 300 ms |
| Global search | p95 below 700 ms including authorization |
| Main screen usable | Within 2 seconds on defined client hardware/network |
| Monthly availability | 99.95% service objective; define included paths and dependencies |
| Disaster recovery | Proposed regional-disaster RPO <=15 minutes and RTO <=4 hours, subject to validated recovery design |

Record workload size, tenant distribution, concurrency, cache state, region, client network, and measurement boundary. Include cold requests, large tenants, simultaneous imports, search, and permission changes. Never present these targets as achieved without evidence.

Use server-side filtering, indexed queries, cursor pagination, list virtualization, bounded payloads, and background processing. Avoid unauthorized prefetch and unsafe optimistic updates for security changes. Show progress and recovery controls for long-running jobs.

UX requirements: persistent organization context; command palette; keyboard shortcuts; saved views; accessible dense tables; clear asset relationships; ownership/freshness indicators; explicit sync status; actionable errors; bulk-action previews; revision recovery; responsive layouts. Target WCAG 2.2 AA and verify keyboard and screen-reader flows. Test with representative IT professionals.

Back up databases, files, configuration, and key recovery materials appropriately. Run actual isolated restores and regional recovery exercises. Verify restored access controls and deletion/retention obligations. Monitor replication lag and noisy-neighbor effects.

## 9. AI constraints

AI is an optional bounded subsystem, not the source of truth or a prerequisite for core functionality. Keep provider integration replaceable. Do not provision a paid AI service without authorization.

Retrieve only information the requesting user may access. Recheck permissions before displaying results, provide source references, and treat retrieved documents as untrusted content rather than executable instructions. Exclude credential values, OTP seeds, and encrypted-vault content. Define data retention and regional processing policies for each provider.

Use human review for generated SOPs and material changes. Automation invoking external actions requires explicit permissions and appropriate confirmation. Prevent prompt injection from granting tool authority. Test data leakage and cross-tenant retrieval. If no authorized provider exists, leave the capability explicitly disabled rather than claiming it works.

## 10. Delivery phases and gates

| Phase | Deliverables | Exit evidence |
|---|---|---|
| 0: Specification | Verified parity inventory, ADRs, threat model, data model, UX flows, prioritized backlog | Scope gaps and assumptions recorded; executable first-slice plan |
| 1: Secure foundation | Repository, local environment, CI, identity adapter, tenants, policy enforcement, migrations, audit/outbox baseline | Two-tenant isolation tests, local setup reproducible, CI passes |
| 2: Core vertical slices | Organizations, assets, relationships, documents/revisions, private attachments, authorized search | End-to-end flows work with persistence; deny cases and revocation tested |
| 3: Integrations and workflows | Connector SDK, first genuine connector, imports/exports, reconciliation, notifications | Retries, rate limits, duplicate events, crash recovery, and expired credentials tested |
| 4: Credential capabilities | Reviewed vault design, reveal/audit, sharing, OTP, rotation, recovery | Independent review and failure-mode tests before real secret use |
| 5: Broader parity | Portal, delegated accounts, discovery, mobile/extensions, offline, Office editing, automation/AI | Each capability verified against parity matrix; unsupported items remain visible |
| 6: Enterprise readiness | Load tests, regional deployment, recovery drills, security review, operations documentation | Evidence-based SLOs, restore results, remediation of release blockers |

Phases are dependency guidance, not permission to discard later scope. Some designs, especially vault encryption and authorization, must be resolved early even when shipped later. Do not wait for every integration to exist before validating a working core.

## 11. First Codex execution session

1. Inspect the repository and instruction files. Report relevant existing stack, tests, constraints, and safe reuse opportunities.
2. Create docs/architecture.md, docs/threat-model.md, docs/feature-parity.md, docs/backlog.md, and ADRs for tenancy, authorization consistency, identity, deployment, and credential encryption. Separate defaults from unresolved decisions.
3. Scaffold web, API, workers, tests, infrastructure, and shared API contracts using the selected stack. Pin compatible versions and provide one documented local startup path.
4. Build a persistent first vertical slice: authenticate a development user, select an authorized organization, create/list an asset, create a linked document, revise it, and inspect the audit history.
5. Seed at least two synthetic tenants with deliberately overlapping names and different access permissions. Make local identity simulation unmistakably development-only and impossible to enable silently in production.
6. Test allowed and forbidden access, guessed IDs, cross-tenant relationships, organization switching, revision conflicts, and durable audit/outbox creation. Add permission-aware search in the next slice if it cannot be completed safely in this session.
7. Run applicable checks and report actual results, startup instructions, delivered functionality, remaining gaps, and the next concrete task. Update docs/progress.md for continuity.

If external credentials block a connector, implement its contract and local test harness, label it unconfigured, and continue independent work. Never hide blocked prerequisites behind success messages.

## 12. Definition of done for each feature

- Behavior is implemented end-to-end and persists correctly; loading, empty, error, and conflict states are usable.
- Tenant isolation, permissions, audit requirements, API contracts, and migrations are covered.
- Meaningful automated tests cover success, denial, and relevant recovery cases; tests exercise real boundaries rather than mirroring implementation.
- No secrets enter logs or fixtures. Dependencies and configurations are documented.
- Accessibility and performance implications are checked at appropriate scope.
- Backlog, parity matrix, API documentation, and progress notes reflect actual implementation status.
- Work is reproducible from documented setup. Mocked dependencies and untested production assumptions are clearly identified.
- Release claims are supported by recorded evidence. Human review gates remain explicit for production secrets and independent security assurance.

## 13. Reference material

These sources informed the conversation on October 28, 2026. Recheck them while building the parity inventory; product capabilities, licensing, and framework support can change. Recommendations in this brief are design judgments, not vendor guarantees.

- IT Glue feature overview: https://www.itglue.com/features/
- IT Glue security whitepaper: https://www.itglue.com/resources/itglue-security/
- IT Glue permissions: https://help.itglue.kaseya.com/help/Content/2-using/permissions/control-access-with-security-permissions.html
- IT Glue API: https://api.itglue.com/developer
- MyGlue: https://www.itglue.com/myglue/
- GlueConnect: https://www.itglue.com/features/glueconnect/
- IT Glue AI overview: https://www.itglue.com/blog/mixed-it-environments-upskill-for-the-future/
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- Next.js TypeScript: https://nextjs.org/docs/app/api-reference/config/typescript
- PostgreSQL row security: https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- OpenFGA: https://openfga.dev/
- Temporal: https://docs.temporal.io/temporal
- S3 Object Lock: https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-lock.html

## 14. Short kickoff prompt

Read Enterprise_IT_Platform_Codex_Brief.md and follow it as the project brief. Inspect the repository and its instructions, record necessary assumptions and architecture decisions, then implement the first secure working vertical slice described in section 11. Preserve the complete enterprise feature roadmap. Do not stop at a plan or a UI mockup. Use synthetic data, enforce tenant isolation from the beginning, run meaningful tests, and report what actually works, what remains unimplemented, and the next step. Do not deploy publicly, purchase services, or use production credentials without authorization.
