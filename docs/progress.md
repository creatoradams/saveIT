# SaveIT implementation progress

Date: 2026-10-28. First section-11 working slice. Original repository: `de89d01` (README + Visual Studio ignore rules). No application or instruction files existed before implementation.

## Completed functionality

- ASP.NET Core/.NET 10 API, strict Next.js/TypeScript UI, PostgreSQL/EF migrations, generated shared OpenAPI response schemas, NuGet/npm lockfiles, runnable worker host and non-provisioning Terraform scaffold.
- Allowlisted development sign-in with explicit opt-in/environment guards, protected HttpOnly session, CSRF on mutations, login rate limit, active membership rechecks and current organization Viewer/Editor policy.
- Two synthetic tenants with overlapping organization/asset names, asymmetric organization permissions, tenant-qualified foreign keys, EF scope filters/write guards, forced RLS on every tenant table and a non-owner runtime role enforced at startup.
- Persistent organization selection/create/list assets, linked plain-text documentation, versioned updates, immutable revision inspection, conflict recovery preserving unsaved text, organization audit history and bounded cursor pages.
- Business changes, revision snapshots, audit events and schema-v1 outbox metadata commit atomically. Document body/history reads require durable read audit. Pending outbox rows are retained; no dispatcher is claimed.
- Root ignore rules extended for ASP.NET outputs, Next/Node caches/dependencies, secret env files, test artifacts, Terraform state/plans/local vars and IDE files. Lockfiles, EF migrations and sanitized examples stay trackable; automated ignore-policy assertions pass.
- Full original brief retained, architecture/threat model/release risks, five ADRs, phased enterprise backlog and capability/integration inventory. Vendor parity/entitlements remain explicitly unverified.

## Evidence recorded locally

Windows x64, Node 24.16.0, .NET SDK 10.0.401, real PostgreSQL 18.4 through a pinned development wrapper. No Docker daemon, cloud accounts, provider credentials or paid AI services were needed.

- 17 API integration tests passed against an isolated actual PostgreSQL database using the restricted application role: full persistence, every write role, guessed/cross-tenant IDs, mismatched organization links, untrusted tenant input, organization switching, current-cookie revocation, concurrent/stale versions, CSRF/cookie forgery, validation, RLS without context, composite FKs, append-only audit/revisions/grants, injected outbox rollback, cursor bounds, startup identity/role guards, and fail-closed body/history reads when read auditing fails.
- Five Playwright Chromium checks passed: persistent asset/document/revision/audit flow and org switch; read-only UI; concurrent-edit recovery; keyboard/responsive/axe checks; live OpenAPI matching the committed schema.
- TypeScript check and optimized Next.js build passed. Automated axe login/workspace checks found color-contrast issues during development; corrected styles passed. Manual screenshot inspection confirmed the desktop workspace layout. This is not a complete WCAG/screen-reader or user-research review.
- npm audit reported zero vulnerabilities; NuGet transitive vulnerability audit reported no vulnerable packages.
- Terraform 1.13.5 binary was checked against its official SHA256 manifest; `fmt -check`, `init -backend=false`, and `validate` passed. No provider/resource/backend exists, so no provider lockfile is generated yet.

Clean `npm ci` and locked NuGet restore passed. Full .NET build completed with zero warnings/errors. The documented `npm run dev` path started PostgreSQL/API/web together and retained existing data after restart. Final API/browser suites passed against that runtime. One intermediate test invocation ran while the database was intentionally stopped and failed connection setup; the database was restarted through the documented command before the passing run. Earlier dependency/analyzer, Windows build-file-lock, test-selector and contrast failures were corrected.

Implementation commit **0c11529b622a5890824497054747e41a362dbbb1** was pushed to `creatoradams/saveIT` on `main`. [GitHub Actions run 40791006969](https://github.com/creatoradams/saveIT/actions/runs/40791006969) **completed successfully** on Ubuntu 24.04: clean locked restore, zero-warning backend build, all 17 real-PostgreSQL tests, ignore policy, generated-contract drift check, TypeScript, optimized web build, npm/NuGet vulnerability checks, Terraform formatting/init/validation, all five Chromium browser checks, and evidence artifact upload. The following documentation-only commit records these results and corrects the inventory's test count; it does not change application or test code.

## Remaining gaps and release risks

This is a local foundation preview. Production startup deliberately refuses unconfigured enterprise identity. Real OIDC/SAML/MFA/SCIM, groups/relationship denies, permission administration/concurrent revocation barrier, HTTPS/session-key operations, full denied/admin/authentication audit, immutable archives and quotas remain absent. Subsequent requests observe revoked membership/grants; already-authorized in-flight requests are not synchronously revoked.

Outbox dispatch, idempotent consumers/replay/retries/dead letters, Temporal, actual connectors/import/export, permission-aware search, attachments/rich editing/restore, credential/OTP/rotation/offline/key recovery, portals/cross-account collaboration, discovery, mobile/extensions, Office editing and AI are unimplemented. Workers/Terraform are scaffolds. AI has no configured provider. The UI uses native controls/custom CSS; Tailwind/component extraction is deferred.

No production cloud resources/public application deployment, load/SLO measurements, backup restore/recovery drills, independent pentest/vault review, SBOM/signed artifacts or certification is claimed. Local runtime servicing updates are required before release. Organization selection currently caps at 100 grants. See the complete [backlog](backlog.md) and [threat/risk register](threat-model.md).

## Next concrete task

Implement permission-aware asset/document search with rebuildable projections and a genuine transactional outbox consumer. Define event ordering/deduplication/recovery before delivery; recheck authoritative permissions for every result, snippet, count, facet and suggestion. Test two-tenant retrieval, revoked access while indexing lags, replay/duplicates and dependency outages. Production identity selection remains a separate release prerequisite; no paid service is required for local search development.

Startup and test commands are in the [README](../README.md). Preserve all enterprise roadmap scope during later slices.
