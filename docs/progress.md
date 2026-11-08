# SaveIT implementation progress

SaveIT is a working, persistent local documentation/inventory preview. It is not ready for customer production data or verified enterprise parity. The [original brief](project-brief.md), all 146 [capability inventory entries](feature-parity.md) and the [full backlog](backlog.md) remain in scope.

## Current build

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Core | Organizations, locations/contacts, typed assets, relationships, plain-text documents, folders/independent placement, immutable history/comparison/restoration and reviewed templates/provenance | Rich editing/files, broader administration, bulk work, template reapply/restore and deletion recovery |
| Tenant authority | Server-selected membership, live organization roles, transaction-local PostgreSQL context, 28 forced-RLS tables, scoped foreign keys and non-owner runtime guards | Managed database verification, advanced permissions and broader SaaS onboarding |
| Writes and search | Atomic business/revision/audit/outbox/queue commits; restricted PostgreSQL search worker with current-source/grant checks, retry/dead-letter/replay/rebuild | OpenSearch, generic external delivery, real connectors and measured scale |
| Identity/access | Opt-in development identity, separate Okta adapter, restricted operator access/session epochs and durable response drain | Live Okta/MFA/assignment, human operator lifecycle and operated fencing/recovery |
| Packaged runtime | Nonroot images, nonce/no-store web behavior, TLS/mTLS relay path, bounded health/configuration tools and synthetic logical database/key recovery | Authenticated web workload intake, managed PKI/rotation, multi-instance keys and managed/offsite/PITR recovery |
| Infrastructure | Nine separate provider-mocked roots for state, private network/database, keys, identities, images/logs, tasks, endpoints, discovery and relay metadata | All roots remain unapplied; no running AWS service or production deployment exists |

The nine roots include separate state and application security boundaries, not nine deployed services. The current runtime uses PostgreSQL; Redis, OpenSearch, OpenFGA, Temporal, vault/AI and connectors are not active subsystems.

## Latest verified baseline

Implementation source **e910910578c62984c7db3ff41c1a0a12ddf3fcc1** passed all three workflows on their first attempt:

- [General checks 42430807160](https://github.com/creatoradams/saveIT/actions/runs/42430807160): **698 real-PostgreSQL tests, zero skipped; 62 Chromium tests; 278 mocked plans** across nine roots (9/16/13/40/39/53/41/31/36). Locked restores, zero-warning/error backend build, generated-contract drift, TypeScript/optimized build, ignore policy, seven security groups and eight load-harness cases passed.
- [Source security 42430807162](https://github.com/creatoradams/saveIT/actions/runs/42430807162): C#/Actions zero findings, JavaScript four medium/zero high/errors, reachable-history secrets zero. npm audit reported zero at this snapshot.
- [Packaged artifacts 42430807169](https://github.com/creatoradams/saveIT/actions/runs/42430807169): 14 configuration, 11 transport, 10 client-certificate, 17 peer-probe and 13 relay checks, plus four Production-mode identity/access/session/recovery flows.

Source-bound final API/web/worker/deployment reports each retain **zero critical/high/unknown; seven medium, one low and one negligible**. Database: **2026-11-07T06:18:48Z**, schema v6.1.10. Retained matches are seven libc6 2.39-0ubuntu8.9 medium not-fixed, libicu74 74.2-1ubuntu3.1 low not-fixed and libc6 negligible wont-fix. SBOM/report hashes and image/source bindings were verified. These findings remain servicing work; scans are not signatures, registry promotion, license approval or production assurance. Inherited Terraform/action dependency deprecation warnings remain visible.

Both requested agents closed bounded ADR043 source/documentation reviews. Their reviews are not independent penetration tests. [ADR043 review](reviews/2026-11-07-relay-identity-review.md) records the full-policy test remedy and unresolved direct Logs customer CallerAccount compatibility gate.

## Build reassessment and refactor

The current cleanup removes duplicate browser request ownership and navigation metadata, forwards cancellation through CSRF token retrieval, reuses the existing audit/outbox helper and avoids loading current document content for historical comparisons. It adds no runtime dependency, cloud resource, service, schema or API contract. Local typecheck/optimized build, ignore/seven security checks, three real-loopback API-client regressions and42 affected real-PostgreSQL tests pass with zero skipped. Both requested bounded source/documentation reviews are closed. Relevant Chromium checks and exact-source Linux CI remain pending; the baseline above must not be attributed to these edits. The [build review](reviews/2026-11-07-build-simplification-review.md) records scope and findings.

Current status now lives here; [historical delivery evidence](progress-history.md) preserves the previous progress file verbatim. Keep future release evidence concise and record a verified source once. Architecture, readiness and completion documents link here rather than copying checkpoints.

## Next steps

1. Implement the relay's in-process pinned-version configuration loader and distinct task/execution private endpoint admissions.
2. Complete referenced network/task/service bindings, ingress/restricted egress, protected population, signing/promotion, scheduling/PassRole and health/drain/fencing operations.
3. Add redacted telemetry/alerts, API/tenant quotas, operational audit archive/retention and recovery runbooks.
4. Use authorized staging accounts to verify actual IAM/KMS/PKI/RDS/Okta, multi-instance keys, recovery and capacity; remediate findings and obtain independent assessment before an approved pilot.

No AWS account or real credentials are needed for the next engineering step. The user selected **Okta** and has no nonproduction AWS account yet. No AWS credential/API/apply, real secret population, registry publication, live Okta or public deployment has been performed.

Before any relay apply, resolve whether direct regional Logs supplies the customer kms:CallerAccount required by the restrictive key policy; missing context fails closed and may block DescribeKey/ingestion. Mocks are not service-context or IAM evidence. Null bootstrap denies all AWS actions to both relay identities; binding_ready checks declared references only.

API completion cannot recall relay/Next/browser buffers. Lost response-protocol state during recovery still requires old-instance fencing. Existing Windows full-suite PostgreSQL backend-exit limitations remain unresolved and separate from the full Linux pass. Product and managed recovery gaps stay in [readiness](production-readiness.md) and [backlog](backlog.md).
