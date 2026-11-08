# Build reassessment and simplification — 2026-11-07

## Assessment

SaveIT has a working persistent local core and verified synthetic packaged behavior. It does not have a running cloud application, live Okta connection or operated production recovery. Nine mock-tested Terraform roots define separate resource/authority boundaries; they are not deployed services. Production configuration delivery, integrated task/service/network bindings, protected release operations, telemetry/quotas and managed verification remain substantial engineering work. The next delivery should close an integrated deployment dependency rather than add more isolated infrastructure roots or product features.

The full enterprise brief, all 146 capability entries and backlog remain intact. No percentage-complete, launch-date, capacity, compliance or vendor-parity claim is supported.

## Bloat found and addressed

| Finding | Refactor | Boundary retained |
| --- | --- | --- |
| Current status repeated old checkpoints and conflicting pending notes across several guides | One concise progress/evidence page, current completion/readiness summaries and architecture links | Previous progress copied verbatim to progress-history.md; original readiness/register remain in Git |
| Two equivalent request-ownership hooks | One shared client hook | Owned controller, null-before-abort unmount cleanup, guarded failure/finally and exactly owned busy release; per-screen conflict/authority state stays local |
| Nine navigation buttons and three repeated view-description branches | One typed presentation metadata source and navigation component | Same labels/order, busy disabling and organization-administrator UI condition; no authority derives from metadata |
| Two endpoint-local audit/outbox constructors | Existing ChangeLog.Add reused for eight calls | Same targets/actions/publish=false behavior, schema 1 defaults, save/transaction boundaries and atomicity |
| Historical comparison loaded the complete current document but used only its version | Nullable scoped Version projection | Current live organization check, exact immutable revision reads, numeric audit and audit-before-output behavior |
| Mutation cancellation omitted the CSRF request | Forward the same AbortSignal through token retrieval | Fresh token per mutation, same-origin/no-store transport and no later write after cancelled token request/body |

Current progress/readiness/completion text shrank from 217,585 to 19,171 bytes in the initial cleanup (91%). The old 165,088-byte progress archive has SHA256 **91c2dacabbecd93b1e4589cb679b82415eb3c7ccdfcbdaaa7e78e93492355df4**, matching the previous file byte-for-byte. Workspace page size fell from 1,549 to 1,422 lines; total handwritten runtime lines across the nine touched/new files fell from 3,590 to 3,534. Added regression tests are counted separately. These are maintainability measures, not bundle-size, latency or capacity benchmarks.

## Deliberately retained

No runtime dependency, service, cloud resource, API/schema contract or migration is added. Frontend runtime packages remain Next.js/React/React DOM; the API-client regressions reuse the existing TypeScript development tool. No clearly unused backend dependency/script was identified in the bounded review.

Generated migration history, independently reviewed policy fixtures and real PostgreSQL/security tests retain useful evidence. Separate task/execution identities, relay process, seed-free migration/access tools, source/configuration checks and response-drain protocol serve actual authority/operational boundaries. Do not collapse these for line-count savings. Small public-CA script similarities do not justify another abstraction. Differing validation rules are not interchangeable.

The workspace page still owns substantial feature state. This cleanup reduces repeated presentation and lifecycle logic; it does not prove the whole repository is free of debt. Future feature-state extraction should preserve tenant/org cancellation, sticky conflicts, failure clearing, keyboard focus and screen-specific authority checks, with existing browser coverage. A generic repository/service/event framework would add cost without resolving the current deployment gap.

## Verification and review

TypeScript, optimized web build, ignore policy, seven security groups and three new real-loopback API-client regressions pass locally. The cancellation tests cover both a held token request and a partially streamed token body, verify no later mutation, and check fresh token/same-origin/no-store behavior.

The initial backend invocation failed at setup because PostgreSQL was stopped. Startup then found Windows denied the default 55432 bind (also reproduced by a Node loopback bind check); the known synthetic stack was started on available 58432 without changing retained data. All 42 affected real-PostgreSQL tests pass with zero skips on 58432. All 19 relevant Chromium tests also pass. Exact source c2c477972e059d1a08e39839e54f90b537e549ca passes general42442584491 (job128614758861), source42442584593 and packaged42442584597 (job128614759457), each on the first attempt: 698 real-PostgreSQL tests/zero skipped, 62 Chromium, 278 mock plans, three API-client regressions, 14 configuration/11 transport/10 client-certificate/17 peer-probe/13 relay checks and four Production-mode flows. Existing Windows full-suite backend-exit limitations are not resolved by changing a port.

The user's requested efficiency/security agents reviewed source read-only. Initial findings informed these refactors, including the concrete CSRF cancellation gap. They perform no edits, test execution or AWS operations. Both final source/documentation rechecks, including the initially missing report link, are closed with no remaining actionable finding. Exact-source links, retained four medium JavaScript findings and final-image seven medium/one low/one negligible findings per image are in [progress](../progress.md). No high/critical/unknown image finding or new source-security finding appeared; lower findings remain open servicing work. No credentials, AWS API/apply, real secret or public deployment is involved.
