# SaveIT implementation progress

Date: 2026-10-28. Foundation and permission-aware search slices. Original repository: `de89d01` (README + Visual Studio ignore rules). No application or instruction files existed before implementation.

## Completed functionality

- ASP.NET Core/.NET 10 API, strict Next.js/TypeScript UI, PostgreSQL/EF migrations, generated shared OpenAPI response schemas, NuGet/npm lockfiles, actual search worker and non-provisioning Terraform scaffold.
- Allowlisted development sign-in with explicit opt-in/environment guards, protected HttpOnly session, CSRF on mutations, login rate limit, active membership rechecks and current organization Viewer/Editor policy.
- Two synthetic tenants with overlapping organization/asset names, asymmetric organization permissions, tenant-qualified foreign keys, EF scope filters/write guards, forced RLS on every tenant table and a non-owner runtime role enforced at startup.
- Persistent organization selection/create/list assets, linked plain-text documentation, versioned updates, immutable revision inspection, conflict recovery preserving unsaved text, organization audit history and bounded cursor pages.
- Business changes, revision snapshots, audit events and schema-v1 outbox metadata commit atomically. Document body/history and returned search results require durable read audit. Immutable events are consumed by the real search worker using separate consumer receipts; generic external dispatch is not implemented.
- Root ignore rules extended for ASP.NET outputs, Next/Node caches/dependencies, secret env files, test artifacts, Terraform state/plans/local vars and IDE files. Lockfiles, EF migrations and sanitized examples stay trackable; automated ignore-policy assertions pass.
- Full original brief retained, architecture/threat model/release risks, six ADRs, phased enterprise backlog and capability/integration inventory. Vendor parity/entitlements remain explicitly unverified.
- Complete-word asset/document search with organization/type filters, ranked 25-item cursor pages, plain-text previews, live source/grant/version checks, stale-result exclusion, atomic read audit and current-record navigation. UI clears results on scope changes/outages and supports retry/empty/loading states.
- Actual non-owner tenant-configured search worker, RLS-protected derived PostgreSQL projection, core-owned security-invoker read contract, idempotent latest-state rehydration, per-tenant concurrency lock, transactional completion receipts, bounded retries/backoff/jitter/dead letters and tenant-scoped rebuild/replay commands. OpenSearch remains deliberately unimplemented under ADR 006.

## First-slice evidence

Windows x64, Node 24.16.0, .NET SDK 10.0.401, real PostgreSQL 18.4 through a pinned development wrapper. No Docker daemon, cloud accounts, provider credentials or paid AI services were needed.

- 17 API integration tests passed against an isolated actual PostgreSQL database using the restricted application role: full persistence, every write role, guessed/cross-tenant IDs, mismatched organization links, untrusted tenant input, organization switching, current-cookie revocation, concurrent/stale versions, CSRF/cookie forgery, validation, RLS without context, composite FKs, append-only audit/revisions/grants, injected outbox rollback, cursor bounds, startup identity/role guards, and fail-closed body/history reads when read auditing fails.
- Five Playwright Chromium checks passed: persistent asset/document/revision/audit flow and org switch; read-only UI; concurrent-edit recovery; keyboard/responsive/axe checks; live OpenAPI matching the committed schema.
- TypeScript check and optimized Next.js build passed. Automated axe login/workspace checks found color-contrast issues during development; corrected styles passed. Manual screenshot inspection confirmed the desktop workspace layout. This is not a complete WCAG/screen-reader or user-research review.
- npm audit reported zero vulnerabilities; NuGet transitive vulnerability audit reported no vulnerable packages.
- Terraform 1.13.5 binary was checked against its official SHA256 manifest; `fmt -check`, `init -backend=false`, and `validate` passed. No provider/resource/backend exists, so no provider lockfile is generated yet.

Clean `npm ci` and locked NuGet restore passed. Full .NET build completed with zero warnings/errors. The documented `npm run dev` path started PostgreSQL/API/web together and retained existing data after restart. Final API/browser suites passed against that runtime. One intermediate test invocation ran while the database was intentionally stopped and failed connection setup; the database was restarted through the documented command before the passing run. Earlier dependency/analyzer, Windows build-file-lock, test-selector and contrast failures were corrected.

Implementation commit **0c11529b622a5890824497054747e41a362dbbb1** was pushed to `creatoradams/saveIT` on `main`. [GitHub Actions run 40791006969](https://github.com/creatoradams/saveIT/actions/runs/40791006969) **completed successfully** on Ubuntu 24.04: clean locked restore, zero-warning backend build, all 17 real-PostgreSQL tests, ignore policy, generated-contract drift check, TypeScript, optimized web build, npm/NuGet vulnerability checks, Terraform formatting/init/validation, all five Chromium browser checks, and evidence artifact upload. The following documentation-only commit records these results and corrects the inventory's test count; it does not change application or test code.

## Second-slice evidence: permission-aware search

The continuation implements search with a **real PostgreSQL full-text projection**, not OpenSearch. ADR 006 records the reversible local deployment choice and retains OpenSearch in the enterprise backlog. The runtime now includes a real restricted background consumer; worker configuration explicitly lists the two synthetic tenants. Existing data is rebuilt on startup.

- All **30** real-PostgreSQL integration tests passed (17 foundation + 13 search/worker cases). Search tests cover tenant/organization/type filters, stale index content after revision, current-cookie revocation while the index is unchanged, scope/principal/query-bound cursor pages and inaccessible-result pagination, unavailable projections, failed search audit, duplicate/reordered/concurrent processing, projection/receipt rollback, bounded retry/backoff/dead-letter/replay, missing source and tenant-scoped rebuild, raw worker/app privilege separation, startup role rejection, and invalid/anonymous search requests.
- All **seven** Chromium browser checks passed. The two added flows use real background indexing and overlapping tenant data, verify filters and current-document navigation, literal HTML previews, organization context reset, responsive layout and automated axe checks. The UI outage/retry case deliberately intercepts a browser response to exercise presentation; backend outage tests independently fail the real PostgreSQL projection. No mock index or connector is used.
- Full .NET solution build passed with zero warnings/errors; locked NuGet restore, TypeScript, optimized Next.js build, generated contracts, ignore policy and npm/NuGet vulnerability audits passed. Manual screenshot inspection confirmed the search layout. The documented launcher rebuilt existing local records and started API/web/worker together; scoped rebuild/one-shot commands also ran successfully.
- The final worker startup guard verifies both projection/receipt write privileges and absence of core write privileges, rejecting API credentials as well as owner/BYPASSRLS credentials. All 30 integration tests and both focused search browser flows passed again with that guard and the restarted complete stack.

Implementation commit **a14682395b4359f75ae8371489ecde147e242362** was pushed to `main`. [GitHub Actions run 40796061840](https://github.com/creatoradams/saveIT/actions/runs/40796061840) **completed successfully** on Ubuntu 24.04, including locked restore, complete solution build, all 30 PostgreSQL tests, generated-contract drift check, TypeScript/optimized web build, ignore policy, npm/NuGet audits, Terraform validation, all seven Chromium browser checks and evidence artifacts. This documentation-only follow-up records that verified result without changing application or test code.

No load/SLO, distributed search, cloud deployment, production identity, immutable operational audit or verified vendor parity result is claimed. Complete-word matching, first-240-character previews, asynchronous indexing, bounded local rebuilds, and the in-flight revocation limits in ADR 002 remain deliberate constraints.

## Remaining gaps and release risks

This is a local foundation preview. Production startup deliberately refuses unconfigured enterprise identity. Real OIDC/SAML/MFA/SCIM, groups/relationship denies, permission administration/concurrent revocation barrier, HTTPS/session-key operations, full denied/admin/authentication audit, immutable archives and quotas remain absent. Subsequent requests observe revoked membership/grants; already-authorized in-flight requests are not synchronously revoked.

Generic external outbox dispatch, Temporal, actual connectors/import/export, distributed OpenSearch, advanced search/autocomplete/facets/files, attachments/rich editing/restore, credential/OTP/rotation/offline/key recovery, portals/cross-account collaboration, discovery, mobile/extensions, Office editing and AI remain unimplemented. The search worker is implemented; Terraform remains a scaffold. AI has no configured provider. The UI uses native controls/custom CSS; Tailwind/component extraction is deferred. Large-tenant chunked search rebuilds, production job scheduling and immutable operator replay/rebuild audit remain gaps.

No production cloud resources/public application deployment, load/SLO measurements, backup restore/recovery drills, independent pentest/vault review, SBOM/signed artifacts or certification is claimed. Local runtime servicing updates are required before release. Organization selection currently caps at 100 grants. See the complete [backlog](backlog.md) and [threat/risk register](threat-model.md).

## Next concrete task

Implement document comparison and audited restoration as a new revision, with expected-version conflict handling and permission/tenant denial cases. Restoring must preserve existing history and atomically create a new revision, audit and outbox invalidation so search catches up safely. Production identity selection remains a separate release prerequisite; the full enterprise roadmap remains unchanged.

Startup and test commands are in the [README](../README.md). Preserve all enterprise roadmap scope during later slices.
