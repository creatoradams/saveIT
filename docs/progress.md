# SaveIT implementation progress

SaveIT is a working, persistent local documentation/inventory preview. It is not ready for customer production data or verified enterprise parity. The [original brief](project-brief.md), all 146 [capability inventory entries](feature-parity.md) and the [full backlog](backlog.md) remain in scope.

## Current build

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Core | Organizations, locations/contacts, typed assets, relationships, plain-text documents, folders/independent placement, immutable history/comparison/restoration and reviewed templates/provenance | Rich editing/files, broader administration, bulk work, template reapply/restore and deletion recovery |
| Tenant authority | Server-selected membership, live organization roles, transaction-local PostgreSQL context, 28 forced-RLS tables, scoped foreign keys and non-owner runtime guards | Managed database verification, advanced permissions and broader SaaS onboarding |
| Writes and search | Atomic business/revision/audit/outbox/queue commits; restricted PostgreSQL search worker with current-source/grant checks, retry/dead-letter/replay/rebuild | OpenSearch, generic external delivery, real connectors and measured scale |
| Identity/access | Opt-in development identity, separate Okta adapter, restricted operator access/session epochs and durable response drain | Live Okta/MFA/assignment, human operator lifecycle and operated fencing/recovery |
| Packaged runtime | Nonroot images, nonce/no-store web behavior, TLS/mTLS relay path, pinned-version relay startup loader, bounded health/configuration tools and synthetic logical database/key recovery | Relay task/execution endpoint admissions, authenticated web workload intake, managed PKI/rotation, multi-instance keys and managed/offsite/PITR recovery |
| Infrastructure | Nine separate provider-mocked roots for state, private network/database, keys, identities, images/logs, tasks, endpoints, discovery and relay metadata | All roots remain unapplied; no running AWS service or production deployment exists |

The nine roots include separate state and application security boundaries, not nine deployed services. The current runtime uses PostgreSQL; Redis, OpenSearch, OpenFGA, Temporal, vault/AI and connectors are not active subsystems.

## Latest verified build

Implementation source **78b509a9c9f92029adcb191e84d449c8fb9cda89** passed all three workflows on their first attempt for that source. The earlier packaged fixture failure and correction are recorded in the review below.

- [General checks 42512906656](https://github.com/creatoradams/saveIT/actions/runs/42512906656): **775 backend tests including real PostgreSQL integration, zero skipped; 62 Chromium tests; 278 mocked plans** across nine roots (9/16/13/40/39/53/41/31/36). Locked restores, zero-warning/error backend build, generated-contract drift, TypeScript/optimized build, ignore policy, seven security groups, eight load-harness cases and three API-client cancellation/fresh-token regressions passed. npm/NuGet audits reported no vulnerable dependencies at this snapshot.
- [Source security 42512906689](https://github.com/creatoradams/saveIT/actions/runs/42512906689): C#/Actions zero findings, JavaScript four medium/zero high/errors, reachable-history secrets zero.
- [Packaged artifacts 42512906666](https://github.com/creatoradams/saveIT/actions/runs/42512906666): **13 new native Linux relay-loader scenarios**, 14 configuration, 11 transport, 10 client-certificate, 17 peer-probe and 13 existing relay checks, plus four Production-mode identity/access/session/recovery flows. Loader scenarios cover real TLS listening, exact version/role, rejected bodies/redirects, stalled headers/body, untrusted system root, absent/alternate credentials and separate environment/default-profile CSM poison with acknowledged UDP controls.

Source-bound final API/web/worker/deployment reports each retain **zero critical/high/unknown; seven medium, one low and one negligible**. Database: **2026-11-08T06:19:57Z**, schema v6.1.10. Retained matches are seven libc6 2.39-0ubuntu8.9 medium not-fixed, libicu74 74.2-1ubuntu3.1 low not-fixed and libc6 negligible wont-fix. Downloaded archive/SBOM/report hashes and image/source bindings were verified. The two new pinned AWS packages occur only in the deployment image. These findings remain servicing work; scans are not signatures, registry promotion, license approval or production assurance. Inherited Terraform/action dependency deprecation warnings remain visible.

Both requested agents closed the bounded loader and fixture source reviews after remedies. Their reviews are not independent penetration tests. The [loader review](reviews/2026-11-08-relay-startup-review.md) records findings and limits; [ADR043 review](reviews/2026-11-07-relay-identity-review.md) retains the unresolved direct Logs customer CallerAccount compatibility gate.

## Prior build reassessment and refactor

The prior cleanup consolidated browser request ownership/navigation, forwarded CSRF cancellation, reused the audit/outbox helper and avoided reading current document content for historical comparisons. That refactor added no runtime dependency, service, schema or API contract; the subsequent loader intentionally adds the deployment-only SDK. The [build review](reviews/2026-11-07-build-simplification-review.md) and [previous verified checkpoint](https://github.com/creatoradams/saveIT/blob/9b65eb9/docs/progress.md) retain its exact scope and evidence.

Current status now lives here; [historical delivery evidence](progress-history.md) preserves the previous progress file verbatim. Keep future release evidence concise and record a verified source once. Architecture, readiness and completion documents link here rather than copying checkpoints.

## Latest runtime delivery: relay startup loader

[ADR044](adr/044-pinned-relay-startup-loader.md) implements one in-process exact-ARN/UUID Secrets Manager read using literal ECS task metadata and the pinned SDK signer. It validates role/expiry, raw envelopes and the original64KiB payload, reuses existing certificate checks, disables alternate sources/proxies/retries/ambient telemetry and emits fixed failures.77 focused/230 affected .NET tests pass locally; native Linux/full CI/source/final-image evidence is recorded above. Docker is unavailable locally, so native execution was verified in CI. The two requested agent reviews found legacy CSM telemetry and test-runner lifecycle issues; remedies and limitations are in the [review](reviews/2026-11-08-relay-startup-review.md). No schema, tenant authority, API contract, Terraform resource or separate service was added.

## Current delivery: relay endpoint policy admission

[ADR045](adr/045-relay-endpoint-policy-admission.md) adds optional validated endpoint filters: relay execution may pull its exact deployment image/write its own log streams; relay task may read only its whole-secret ARN/UUID. Null retains the original policy bytes and existing resources/network output. All 307 local mocked plans pass, including 70 endpoint plans; loader regressions, TypeScript/build and ignore checks pass. Both requested source reviews closed without actionable findings; scope and limits are in the [review](reviews/2026-11-08-relay-endpoint-review.md). Full Linux/source/package/final-image checks are pending, so the exact verified source above remains authoritative. No relay packet path, IAM grant, apply or cloud request was added.

## Next steps

1. Verify the new optional relay endpoint filters in Linux CI, then implement the dedicated relay network group and private discovery/transport bindings.
2. Complete referenced network/task/service bindings, ingress/restricted egress, protected population, signing/promotion, scheduling/PassRole and health/drain/fencing operations.
3. Add redacted telemetry/alerts, API/tenant quotas, operational audit archive/retention and recovery runbooks.
4. Use authorized staging accounts to verify actual IAM/KMS/PKI/RDS/Okta, multi-instance keys, recovery and capacity; remediate findings and obtain independent assessment before an approved pilot.

No AWS account or real credentials are needed for the next engineering step. The user selected **Okta** and has no nonproduction AWS account yet. No AWS credential/API/apply, real secret population, registry publication, live Okta or public deployment has been performed.

Before any relay apply, resolve whether direct regional Logs supplies the customer kms:CallerAccount required by the restrictive key policy; missing context fails closed and may block DescribeKey/ingestion. Mocks are not service-context or IAM evidence. Null bootstrap denies all AWS actions to both relay identities; binding_ready checks declared references only.

API completion cannot recall relay/Next/browser buffers. Lost response-protocol state during recovery still requires old-instance fencing. Existing Windows full-suite PostgreSQL backend-exit limitations remain unresolved and separate from the full Linux pass. Product and managed recovery gaps stay in [readiness](production-readiness.md) and [backlog](backlog.md).
