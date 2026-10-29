# SaveIT threat model and release risks

Updated 2026-10-28. Scope: the local foundation, synthetic data. Owners: project maintainers. Reassess each added endpoint, consumer, identity adapter, and data classification.

## Data and trust boundaries

Public: product branding and health status. Internal: organization/asset metadata. Confidential: IT documentation, revisions, relationships, audit identities. Restricted: future passwords, OTP seeds, keys, recovery and export material. Treat all tenant content as untrusted. No real credentials belong in this prototype.

Boundaries: browser ↔ same-origin Next proxy ↔ ASP.NET session/policy ↔ restricted PostgreSQL API role; a separate tenant-configured worker role reads the core search-source contract and writes only derived projections/consumer receipts. Migration owner credentials are separate. Future distributed search, attachment, vault, extension, discovery, and AI boundaries require further threat models. The public Git repository contains source and synthetic fixtures only.

| Abuse case | Present control | Acceptance evidence / residual risk |
| --- | --- | --- |
| Tenant ID header/payload tampering | Server-selected signed session, live membership, EF filters, RLS | Integration: forged input cannot switch tenant; all protected tables deny without DB context |
| Guess another tenant's IDs | Tenant scope plus organization/target predicates | Cross-tenant assets, document bodies/revisions, and audit/organization routes tested |
| Link records across tenant/org boundaries | Scoped lookup and composite database FK | Both API and raw restricted-role FK tests |
| Viewer writes or reads privileged audit | Live per-organization action checks | API denial for every write; UI hides editing controls; audit read denied |
| Stale session after revocation | Membership and grants checked every request | Existing-cookie revocation test; in-flight request semantics in ADR 002 |
| Compromised app tries to alter audit/grants | Database grant restrictions; separate owner role | Raw-role tests; DB owner can still change data; external immutable archives unimplemented |
| Lost update / revision race | Expected version + EF concurrency predicate | Concurrent writers yield one success/one conflict; no extra revision/audit/outbox |
| Partial audit/outbox commit | Shared DB transaction; body reads fail closed on audit failure | Injected outbox failure rolls back business row and audit |
| CSRF/login CSRF | ASP.NET antiforgery header + cookie; Strict session; same-origin routing | Missing-token writes and login rejected; no permissive CORS |
| Cookie forgery | ASP.NET cookie protection; HttpOnly; two-hour expiry | Invalid cookie rejected; production session/key rotation still unconfigured |
| Stored XSS from documentation | Plain text only, React escaping; CSP, no raw HTML insertion | Browser flow renders content as text; rich editor/HTML security deferred |
| Secret/document leakage in logs | No body logging; no EF sensitive-data logging; safe errors | Custom handler emits exception type/correlation only; full observability redaction still needs review |
| Unbounded reads / parser abuse | Cursor limits, content/name limits; login rate limit | API validation and page-boundary tests; broad per-tenant quotas and load tests pending |
| Development sign-in deployed accidentally | Explicit opt-in + environment guard; production startup refuses unconfigured identity | Guard/default-disabled-route tests; loopback local script |
| SQL injection | EF parameterization; fixed server-side table/policy names | No concatenated request SQL; test/bootstrap SQL uses only server-controlled synthetic values |
| Prompt injection, SSRF, malicious attachments, extensions | These features are unavailable | Separate approval, egress, quarantine, parser and retrieval tests required when implemented |
| Revoked organization or stale content remains indexed | Current membership/grants and exact current source scope/version/content checked before fields and pagination | Revocation with unchanged index/cookie, stale revision, type/scope and cursor tests; in-flight request semantics unchanged |
| Projection outage or failed search audit leaks cached previews | No cached/direct-query fallback; failure prevents response serialization | Real missing-projection and failed read-audit tests; browser clears results/retries |
| Index/worker crosses tenant or alters core records | Explicit configured tenants, per-transaction context, invoker RLS, non-owner worker role, read-only core grants | Actual role tests for no-context denial, cross-tenant write/read denial and forbidden core/outbox/grant/audit access |
| Duplicate, reordered, interrupted or poison event processing | Latest-source rehydration, version guard, tenant lock, transactional projection/receipt, backoff/dead letters/replay | Concurrent/replay, receipt failure rollback, transient retry/dead-letter and scoped rebuild/missing-source tests |
| Search preview stored XSS / query logging | React text output, bounded plaintext preview, no query/body logging in application | Browser literal HTML payload and audit tests; future proxy/access logs must redact URL query strings |

## Release-risk register

All entries below block a production/enterprise-ready claim. There is no claim of SOC 2, independent review, production security, achieved SLO, or vendor parity.

1. **Identity**: enterprise OIDC/SAML, phishing-resistant MFA, SCIM, service tokens, break-glass, session revocation/key storage, real HTTPS and trusted proxy policy are unimplemented.
2. **Authorization**: no group/relationship denies, permission administration, cross-account sharing, OpenFGA, or concurrent revocation barrier. Current guarantees cover the implemented organization roles and subsequent requests.
3. **Audit**: successful changes and document-body/revision reads are durable; authentication, denied attempts, metadata reads, permission administration, support/emergency/export activity and failed-operation auditing are incomplete. No separate immutable archive, retention, SIEM export, or gap detection exists.
4. **Events/operations**: search-v1 consumption has tested idempotency/replay/retry/dead letters and rebuild commands; generic external dispatch, Temporal, operational administration/immutable replay audit, metrics/traces/alerts, quotas, backups and recovery drills remain pending. Search completion receipts never mark global outbox delivery. Do not manually mark events delivered to consumers that have not handled them.
5. **Credentials/AI**: vault remains absent. Resolve key ownership/recovery and independent crypto review before real credentials; AI is disabled, with no provider or paid service provisioned.
6. **Supply chain**: pinned npm/NuGet lockfiles and CI dependency audits establish a baseline. SBOMs, secret/SAST scanning at organization scope, signed images, release provenance, patch response and penetration testing remain required. Local SDK 10.0.401/runtime 10.0.12 requires servicing update before release.
7. **Data lifecycle**: deletion/recovery/retention, import/export, data residency, support access, restore-time access checks and legal obligations are unresolved.
8. **UX/performance**: automated axe/keyboard/responsive tests cover a few flows; real screen-reader testing, WCAG 2.2 AA review, representative IT-user research, virtualization, saved views, command palette, and documented load tests remain.

## Review checklist for the next slice

Keep the current search authority/stale-index/outage tests for every search extension, including future OpenSearch, facets/counts/autocomplete, filenames and AI retrieval. Never use indexed ACLs as final authority. Document comparison/restore must authorize and audit historical reads and create a new version atomically under concurrency checks. Private attachments still require scan/quarantine, isolated parsers, SSRF controls, authorized delivery and retention. Production rollout requires reviewed migrations and an actual isolated restore test.
