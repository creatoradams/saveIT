# SaveIT implementation progress

SaveIT is a working, persistent local documentation/inventory preview. It is not ready for customer production data or verified enterprise parity. The [original brief](project-brief.md), all 146 [capability inventory entries](feature-parity.md) and the [full backlog](backlog.md) remain in scope.

## Current build

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Core | Organizations, locations/contacts, typed assets, relationships, plain-text documents, folders/independent placement, immutable history/comparison/restoration and reviewed templates/provenance | Rich editing/files, broader administration, bulk work, template reapply/restore and deletion recovery |
| Tenant authority | Server-selected membership, live organization roles, transaction-local PostgreSQL context, 28 forced-RLS tables, scoped foreign keys and non-owner runtime guards | Managed database verification, advanced permissions and broader SaaS onboarding |
| Writes and search | Atomic business/revision/audit/outbox/queue commits; restricted PostgreSQL search worker with current-source/grant checks, retry/dead-letter/replay/rebuild | OpenSearch, generic external delivery, real connectors and measured scale |
| Identity/access | Opt-in development identity, separate Okta adapter, restricted operator access/session epochs and durable response drain | Live Okta/MFA/assignment, human operator lifecycle and operated fencing/recovery |
| Packaged runtime | Nonroot images, nonce/no-store web behavior, TLS/mTLS relay path, pinned-version relay startup loader, bounded health/configuration tools and synthetic logical database/key recovery | Protected service launch/change/health/drain, authenticated web workload intake, managed PKI/rotation, multi-instance keys and managed/offsite/PITR recovery |
| Infrastructure | Ten separate provider-mocked roots for state, private network/database, keys, identities, images/logs, tasks, endpoints, discovery and relay metadata | All roots remain unapplied; no running AWS service or production deployment exists |

The ten roots include separate state, application and service lifecycle boundaries, not ten deployed services. The current runtime uses PostgreSQL; Redis, OpenSearch, OpenFGA, Temporal, vault/AI and connectors are not active subsystems.

## Latest verified build

Implementation source **68a2d78a701221b03255023c7e7248ad53c08683** passed all three workflows on their first attempt:

- [General checks 42559316721](https://github.com/creatoradams/saveIT/actions/runs/42559316721): **852 backend tests including real PostgreSQL integration, zero skipped; 62 Chromium tests; 438 mocked plans** across ten roots (9/16/13/40/39/95/76/51/36/63). Locked restores, zero-warning/error backend build, generated-contract drift, TypeScript/optimized build, ignore policy, seven security-gate tests, eight load-harness cases and three API-client cancellation/fresh-token regressions passed. npm/NuGet audits reported no vulnerable dependencies at this snapshot.
- [Source security 42559316688](https://github.com/creatoradams/saveIT/actions/runs/42559316688): C#/Actions zero findings, JavaScript four medium/zero high/errors, reachable-history secrets zero.
- [Packaged artifacts 42559316708](https://github.com/creatoradams/saveIT/actions/runs/42559316708): **eight new native Linux offline relay-task checks** with networking disabled, covering the independent exact fixture, private environment drift, manifest/configuration mismatch, host-volume escalation, escaped duplicates, mixed arguments and held-input EOF deadline. Existing **13 relay-loader scenarios**, 14 configuration, 11 transport, 10 client-certificate, 17 peer-probe and 13 relay checks, plus four Production-mode identity/access/session/recovery flows pass. Loader scenarios retain exact version/role, refused bodies/redirects, bounded waits, native TLS/root/credential and separate environment/default-profile CSM poison controls; liveness remains quiet under proxy poison without extra metadata/secret reads.

Source-bound final API/web/worker/deployment reports each retain **zero critical/high/unknown; seven medium, one low and one negligible**. Database: **2026-11-08T06:19:57Z**, schema v6.1.10. Retained matches are seven libc6 2.39-0ubuntu8.9 medium not-fixed, libicu74 74.2-1ubuntu3.1 low not-fixed and libc6 negligible wont-fix. Downloaded archive/SBOM/report hashes and image/source bindings were verified. The two pinned AWS packages occur only in the deployment image. These findings remain servicing work; scans are not signatures, registry promotion, license approval or production assurance. Inherited Terraform/action dependency deprecation warnings remain visible.

The requested security agent closed the bounded offline-preflight design/source/tests/docs/native-fixture review without actionable findings. The [preflight review](reviews/2026-11-09-relay-task-preflight-review.md) records input, override and managed-evidence limits; the agent did not execute or independently attest tests. The efficiency agent remains unavailable after its account usage limit; a fresh independent efficiency review remains outstanding. Reviews are not penetration tests. Prior [service](reviews/2026-11-09-private-services-review.md), [task](reviews/2026-11-09-relay-task-review.md), [network](reviews/2026-11-08-relay-network-review.md) and [endpoint](reviews/2026-11-08-relay-endpoint-review.md) reviews retain their scopes; [ADR043 review](reviews/2026-11-07-relay-identity-review.md) retains the direct Logs customer CallerAccount compatibility gate.

## Prior build reassessment and refactor

The prior cleanup consolidated browser request ownership/navigation, forwarded CSRF cancellation, reused the audit/outbox helper and avoided reading current document content for historical comparisons. That refactor added no runtime dependency, service, schema or API contract; the subsequent loader intentionally adds the deployment-only SDK. The [build review](reviews/2026-11-07-build-simplification-review.md) and [previous verified checkpoint](https://github.com/creatoradams/saveIT/blob/9b65eb9/docs/progress.md) retain its exact scope and evidence.

Current status now lives here; [historical delivery evidence](progress-history.md) preserves the previous progress file verbatim. Keep future release evidence concise and record a verified source once. Architecture, readiness and completion documents link here rather than copying checkpoints.

## Latest runtime delivery: relay startup loader

[ADR044](adr/044-pinned-relay-startup-loader.md) implements one in-process exact-ARN/UUID Secrets Manager read using literal ECS task metadata and the pinned SDK signer. It validates role/expiry, raw envelopes and the original64KiB payload, reuses existing certificate checks, disables alternate sources/proxies/retries/ambient telemetry and emits fixed failures.77 focused/230 affected .NET tests pass locally; native Linux/full CI/source/final-image evidence is recorded above. Docker is unavailable locally, so native execution was verified in CI. The two requested agent reviews found legacy CSM telemetry and test-runner lifecycle issues; remedies and limitations are in the [review](reviews/2026-11-08-relay-startup-review.md). No schema, tenant authority, API contract, Terraform resource or separate service was added.

## Prior delivery: relay endpoint policy admission

[ADR045](adr/045-relay-endpoint-policy-admission.md) adds optional validated endpoint filters: relay execution may pull its exact deployment image/write its own log streams; relay task may read only its whole-secret ARN/UUID. Null retains the original policy bytes and existing resources/network output. Local validation includes all infrastructure mocks, 77 loader regressions, TypeScript/build and ignore policy; the complete Linux/source/package/final-image verification is above. Both requested source reviews closed without actionable findings; scope and limits are in the [review](reviews/2026-11-08-relay-endpoint-review.md). No relay packet path, IAM grant, apply or cloud request was added. The [previous verified checkpoint](https://github.com/creatoradams/saveIT/blob/8b92135/docs/progress.md) retains the loader delivery's earlier exact evidence.

## Prior delivery: relay network and private discovery

[ADR046](adr/046-relay-network-and-private-discovery.md) adds separate opt-in relay networking and a private registry using existing roots. Enabled mode requires the bound policy contract, creates its own group with scoped443 endpoint/layer paths, and replaces declared direct web/API8443 with web/relay/API8443. API-owned transport and relay target stay distinct. Null retains initial compatibility; it is not safe operated withdrawal/failover, and protected registry removal may block a transition. No task definition/environment, application authority, dependency, root or running service is added.

The [verified network checkpoint](https://github.com/creatoradams/saveIT/blob/613b006/docs/progress.md) retains its exact test/workflow/artifact evidence and both requested source reviews. Those mocks and source reviews establish no managed enforcement or safe withdrawal behavior.

## Prior delivery: isolated relay task and web target

[ADR047](adr/047-isolated-relay-task-and-web-target.md) adds an optional separate nonroot scratch-only relay task using its own roles, deployment digest and exact startup ARN/UUID arguments. Only web target/public CA hash changes among existing applications; web stays keyless and API-owned transport remains distinct. Fixed shell-free loopback liveness checks headers and empty-body EOF under one deadline, without SDK/configuration or upstream contact. No new root/dependency, tenant/schema authority, service or ENI attachment is added.

The [verified task checkpoint](https://github.com/creatoradams/saveIT/blob/8ace209/docs/progress.md) retains exact local/full-CI/artifact evidence and the security review's two probe/test remedies. Its [review](reviews/2026-11-09-relay-task-review.md) retains scope and the outstanding efficiency review; none of this is managed trust/network evidence.

## Prior delivery: parked private service composition

[ADR048](adr/048-parked-private-service-composition.md) composes task/discovery outputs downstream into one cluster and four REPLICA services fixed at desired0. Each declares private subnet placement/one own SG; only API/relay bind registries. It adds no launch switch, autoscaling, IAM grant, task/secret-content lookup, runtime dependency or application authority. Zero is not account-enforced no-launch, drain, fencing or proof that prior instances are absent. Future apply can create the ECS service-linked role; no apply or cloud request has occurred.

The [verified service checkpoint](https://github.com/creatoradams/saveIT/blob/404aca1/docs/progress.md) retains its exact local/full-CI/artifact evidence. The [review](reviews/2026-11-09-private-services-review.md) records provider/mock limits and review status. Registry tag mock inputs are not returned-tag rejection evidence; actual association/DNS/IAM/registration/health/drain remains unverified. The efficiency review remains outstanding after the agent's usage limit.

## Current delivery: offline relay task preflight

[ADR049](adr/049-offline-relay-task-preflight.md) adds `check-relay-task` to the existing deployment executable. It independently checks supplied relay revision/image/configuration/role/runtime metadata, rejects semantic additions/duplicates and emits fixed content-free output under bounded input/EOF limits. It reuses protected input mechanics while retaining the original flat configuration schema; no dependency, infrastructure root/resource, tenant authority or launch switch is added. All services remain fixed at zero. Supplied data does not prove provenance/freshness, actual image/private configuration, RunTask override restrictions or launch approval.

Local focused validation passed **215 .NET tests with zero skipped**, including relay loader/liveness and flat configuration regressions. The ECS producer's95 mocked plans, TypeScript/build, ignore policy and native-fixture syntax/format checks pass. Full source/Linux/browser/package evidence for this source is above. Downloaded artifact **13648796929** matches archive SHA256 **6001b6ea76c24cb431a66526c9a06cd51b3dac0d06d930942a47344afb311138**; all four source/image/SBOM/report bindings verify. The security agent reviewed design/source/tests/docs/native fixture without actionable findings. This is neither cloud evidence nor release approval; the fresh independent efficiency review remains outstanding after the agent's usage limit.

## Next steps

1. Add trusted acquisition/protected release authority and override prohibition around offline task checks, then bounded service change and operated health/drain/fencing for the parked contracts; retain keyless web and exact startup/version/role guards.
2. Complete restricted ingress/egress, protected population, signing/promotion, scheduling/PassRole and operated health/drain/fencing.
3. Add redacted telemetry/alerts, API/tenant quotas, operational audit archive/retention and recovery runbooks.
4. Use authorized staging accounts to verify actual IAM/KMS/PKI/RDS/Okta, multi-instance keys, recovery and capacity; remediate findings and obtain independent assessment before an approved pilot.

No AWS account or real credentials are needed for the next engineering step. The user selected **Okta** and has no nonproduction AWS account yet. No AWS credential/API/apply, real secret population, registry publication, live Okta or public deployment has been performed.

Before any relay apply, resolve whether direct regional Logs supplies the customer kms:CallerAccount required by the restrictive key policy; missing context fails closed and may block DescribeKey/ingestion. Mocks are not service-context or IAM evidence. Null bootstrap denies all AWS actions to both relay identities; binding_ready checks declared references only.

API completion cannot recall relay/Next/browser buffers. Lost response-protocol state during recovery still requires old-instance fencing. Existing Windows full-suite PostgreSQL backend-exit limitations remain unresolved and separate from the full Linux pass. Product and managed recovery gaps stay in [readiness](production-readiness.md) and [backlog](backlog.md).
