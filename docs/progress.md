# SaveIT implementation progress

SaveIT is a working, persistent local documentation/inventory preview. It is not ready for customer production data or verified enterprise parity. The [original brief](project-brief.md), all 146 [capability inventory entries](feature-parity.md) and the [full backlog](backlog.md) remain in scope.

## Current build

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Core | Organizations, locations/contacts, typed assets, relationships, plain-text documents, folders/independent placement, immutable history/comparison/restoration and reviewed templates/provenance | Rich editing/files, broader administration, bulk work, template reapply/restore and deletion recovery |
| Tenant authority | Server-selected membership, live organization roles, transaction-local PostgreSQL context, 28 forced-RLS tables, scoped foreign keys and non-owner runtime guards | Managed database verification, advanced permissions and broader SaaS onboarding |
| Writes and search | Atomic business/revision/audit/outbox/queue commits; restricted PostgreSQL search worker with current-source/grant checks, retry/dead-letter/replay/rebuild | OpenSearch, generic external delivery, real connectors and measured scale |
| Identity/access | Opt-in development identity, separate Okta adapter, restricted operator access/session epochs and durable response drain | Live Okta/MFA/assignment, human operator lifecycle and operated fencing/recovery |
| Packaged runtime | Nonroot images, nonce/no-store web behavior, TLS/mTLS relay path, optional authenticated workload intake, pinned-version loader, bounded health/configuration tools and synthetic logical database/key recovery | Protected service launch/change, whole-cell drain/fencing, managed PKI/rotation, multi-instance keys and managed/offsite/PITR recovery |
| Infrastructure | Ten separate provider-mocked roots for state, private network/database, keys, identities, images/logs, tasks, endpoints, discovery and relay metadata | All roots remain unapplied; no running AWS service or production deployment exists |

The ten roots include separate state, application and service lifecycle boundaries, not ten deployed services. The current runtime uses PostgreSQL; Redis, OpenSearch, OpenFGA, Temporal, vault/AI and connectors are not active subsystems.

## Prior verified build

Implementation source **050ef1b11bb2a663ba622460189795030b920d09** passed all three workflows:

- [General checks 42955234819](https://github.com/creatoradams/saveIT/actions/runs/42955234819): **1232 backend tests including real PostgreSQL integration, zero skipped; 62 Chromium tests; 462 mocked plans** across ten roots (9/16/13/46/39/105/83/51/36/64). Build, locked restores, contract drift, TypeScript/optimized build, ignore/security/load/API-client/SDK gates pass. npm/NuGet audits reported no vulnerable dependencies at this snapshot.
- [Source security 42955234715](https://github.com/creatoradams/saveIT/actions/runs/42955234715): C#/Actions zero findings; JavaScript four medium, zero high/errors; reachable-history secrets zero with the same two reviewed exact historical fingerprints. Original thresholds/exclusions remain unchanged.
- [Packaged artifacts 42955234777](https://github.com/creatoradams/saveIT/actions/runs/42955234777): all12 native signed-release and48 task/service/image/private-configuration acquisition checks, authenticated native TLS/JWT/mTLS intake, existing loader/configuration/transport/identity/access/session/recovery flows and four Production browser cases pass. Recovery explicitly checks 28 tenant/protocol tables, two empty operational control tables and migration history; this is an uninitialized-control synthetic drill. Database outage refuses readiness while API/worker/web liveness stays available. Native execution was verified in CI.

All four source-bound final image reports retain **zero critical/high/unknown; three medium, one low and one negligible**. Database2026-11-10T06:21:20Z, schema v6.1.10; libc6/libicu findings remain servicing work. Downloaded artifact13827491086 matches archive SHA256 **f7784c84794372e4901651ca96c153338884be5ac19ca383025eead74cd5f728**; native/CycloneDX/report hashes, source/image identities and nonroot bindings verify. All five .NET AWS packages remain deployment-only. The web Cognito/Smithy SDK is bundled by Next and is **not individually cataloged by these image SBOMs**; locked npm audit covers its declared dependency tree, not a complete final-bundle inventory. This release-assurance gap remains open. No signature, promotion, registry digest, license approval or production assurance follows from scans.

The independent security agent closed the [workload review](reviews/2026-11-09-web-workload-review.md) and [coordination review](reviews/2026-11-09-compute-coordination-review.md), including the subsequent clock/recovery fixture remedies, without executing tests. The requested independent efficiency review remains outstanding after its agent's account usage limit. These reviews are not penetration tests. Earlier exact workload evidence remains at the [previous checkpoint](https://github.com/creatoradams/saveIT/blob/b813a78/docs/progress.md); historical evidence is not rewritten. Direct regional Logs customer CallerAccount compatibility remains a pre-apply gate.

## Current engineering: parked workload inventory

`check-compute-parked-tasks` adds one signed-cluster ECS inventory read after the existing parked-service preflight. It rejects any desired-RUNNING task and any incomplete inventory. All 76 local parked-state tests passed. Three packaged Linux cases cover the empty inventory, an unexpected task and pagination. Private zone and registry readback, RDS and EFS attachment checks and post-launch task-to-ENI verification remain open.

## Prior delivery: direct Resolver baseline

[ADR062](adr/062-direct-resolver-baseline.md) adds read-only verification of direct Resolver associations, DNS Firewall associations and the VPC's fail-open setting. The current infrastructure declares no custom associations or approved filtering policy. Route 53 Profiles, effective filtering and live DNS answers remain open.

Source **559eda2** passed 361 local acquisition regressions, locked restore, TypeScript, optimized web build and the ignore check. The [packaged run](https://github.com/creatoradams/saveIT/actions/runs/43539335185) passed all 88 acquisition cases, including seven Resolver cases. The fresh scan found zero critical, high or unknown findings across the four images. Each image had 18 medium, one low and one negligible finding. [Source security](https://github.com/creatoradams/saveIT/actions/runs/43539335014) passed.

[General CI](https://github.com/creatoradams/saveIT/actions/runs/43539335056) passed 1,406 backend tests with zero skipped, 62 Chromium cases and all ten Terraform mock suites.

## Prior delivery: DHCP and endpoint-interface acquisition

[ADR061](adr/061-bound-dhcp-and-endpoint-interfaces.md) checks the associated DHCP options and independently verifies endpoint interface membership, placement, security groups and private IPv4 addresses. It adds two fixed EC2 reads and retains the existing signed contract and Terraform resources. Resolver policy, live DNS answers and other interface types remain open.

Local verification passed 352 acquisition and signed release tests, all 84 runtime-endpoint Terraform mocks, TypeScript, the optimized web build, the ignore check and both synthetic signer tests. Five packaged cases cover valid readback, a custom resolver, a foreign interface and unexpected pagination.

## Prior delivery: bound private endpoint acquisition

[ADR060](adr/060-bound-private-endpoint-acquisition.md) adds an exact six-endpoint inventory and the regional AWS-owned S3 prefix check after route verification. It compares canonical policy readback hashes against signed bindings. The optional Terraform digest output is derived from the existing policies, with all 84 runtime-endpoint mock tests passing. Live AWS response compatibility, IAM evaluation, Resolver and independent attachment checks remain open.

The 324 focused .NET regressions, TypeScript, optimized web build, ignore check, Compose validation and two signer tests pass locally. Seven new native cases cover the valid inventory, policy/group drift, pagination, prefix ownership and a stalled prefix response. Their packaged execution is tracked in CI.

The first [packaged run](https://github.com/creatoradams/saveIT/actions/runs/43255787764) passed the 69 earlier acquisition cases and stopped at the first endpoint case because the synthetic provider selected the legacy task fixture. With that corrected, source **332ca2d** passed [general checks](https://github.com/creatoradams/saveIT/actions/runs/43257202166) with 1,369 backend tests, 62 Chromium cases and 465 Terraform mocks. [Source security](https://github.com/creatoradams/saveIT/actions/runs/43257202567) passed. The [packaged run](https://github.com/creatoradams/saveIT/actions/runs/43257202420) passed all 76 acquisition cases, 12 signed-release checks and four browser cases. Fresh image scans had zero critical/high/unknown findings, with three medium, one low and one negligible per image. The downloaded inventory and report hashes verified.

## Prior delivery: bound private route acquisition

Source **29b0cf4** pins SDK10.0.404 and ASP.NET/Core10.0.15. [General checks](https://github.com/creatoradams/saveIT/actions/runs/43249803057) passed 1,336 backend tests with zero skipped, 62 Chromium tests and all Terraform mocks. [Source security](https://github.com/creatoradams/saveIT/actions/runs/43249803037) passed. The [packaged run](https://github.com/creatoradams/saveIT/actions/runs/43249803401) verified the runtime inside all three .NET images and passed 69 native acquisition checks, 12 signed-release checks and four browser cases. All four images had zero critical/high/unknown, with three medium, one low and one negligible finding per image. The database was built at 2026-11-12T06:20:58Z. The downloaded inventory archive, source, image and report bindings verified.

[ADR059](adr/059-bound-private-route-acquisition.md) adds five fixed EC2 reads for the exact VPC, its DNS flags, all signed private subnets and the complete route inventory. It checks four explicit associations, the local-only main table, original local routes and the signed application-only S3 route. No SDK/dependency, Terraform resource, schema/API change, provider write or tenant authority is added.

Local411 affected tests and101 producer mock plans across foundation/endpoints pass. Both prior reviewers checked the route source and fixture. The efficiency fixes bound child allocations, preserve original fixture bytes and make final-response expiry deterministic. Ownership transfer, durable provider effects, launch/drain/fencing and restricted ingress/Okta egress remain open.

## Prior delivery: bound security-group acquisition

[ADR058](adr/058-bound-security-group-acquisition.md) adds optional signed network bindings and requires them before one fixed VPC-scoped raw EC2 inventory. Exact13-group ownership/default/reciprocal-rule checks reject unknown/extra/foreign/CIDR/IPv6/peering/pagination authority. It reuses the existing acquisition/TLS/deadline stack with deployment-only EC24.0.132.2; Core remains4.0.102.20. Fixed acquisition CLI handling and native command expectations are consolidated. This stage does not verify routes, endpoint policies, S3 ownership, DNS, attachments or fencing.

Local553 affected tests pass after the efficiency fixes, including the56 security-group cases. All43 real PostgreSQL compute/admission tests pass, including exact bound/legacy receipt retention and body-free inspection. Four producer roots pass166 mocked plans comparing31 frozen edge roles/directions to declared resources. Synthetic IDs/policy-hash placeholders are not IAM/resource evidence. No new root/resource/schema, API contract, provider write or launch switch is added.

Source **92a9037** passed [general checks](https://github.com/creatoradams/saveIT/actions/runs/43138166086) with 1290 backend tests, 62 Chromium tests and 465 mocked plans. [Source security](https://github.com/creatoradams/saveIT/actions/runs/43138166273) passed. The [packaged run](https://github.com/creatoradams/saveIT/actions/runs/43138166189) passed all 59 native acquisition checks, 12 signed-release checks and four Production browser cases before Grype blocked the three .NET images on CVE-2026-114149 in ASP.NET Core 10.0.14. The downloaded blocked artifact matched its archive, image and report hashes. Microsoft lists 10.0.15 as the fix. [Microsoft advisory](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-x33c-w48q-qrgc)

Independent efficiency and security agents reviewed the acquisition changes and rechecked the remedies. The cohort reuses validated keys/CA bundles and database targets, transport retains one owned raw JSON view with tighter buffers, and native stall timing is measured at the target response. See the [review](reviews/2026-11-10-security-group-acquisition-review.md); its scope is the acquisition stack and fixtures, not the whole repository.

## Prior delivery: trusted private configuration cohort

[ADR057](adr/057-trusted-private-configuration-cohort.md) adds exactly three approved ARN/UUID reads after public acquisition and checks API/worker/relay database-target, PKI/pin and transport/protector/anchor-key consistency. It reuses the existing pinned Secrets Manager SDK, fixed SDK/TLS transport and carrier validators; no dependency, schema, tenant/API contract or infrastructure resource is added. No private values are returned/written; managed copies remain sensitive. The approved relay version supplies API-root custody, not another independently signed approval.

Local60 focused tests, prior427 affected SDK/configuration/TLS regressions,8 workload/signer tests, clean zero-warning/error Release build, TypeScript/build and ignore policy pass. The security agent closed two coverage findings after correct-name/purpose foreign-issuer refusals and actual response-after-approval/session-expiry tests were added. Its [review](reviews/2026-11-10-configuration-cohort-review.md) records no remaining actionable issue and no test execution. All48 native cases/full CI now pass above; Docker is unavailable locally. Efficiency review remains outstanding after its agent's usage limit.

The whole section remains open: next are network acquisition, exclusive ownership/Terraform handoff, privileged-writer exclusion, durable effect intent/reconciliation, launch/drain and exact task/process/ENI fencing. No AWS account, real credentials, apply or customer data has been used.

## Prior delivery: trusted image configuration acquisition

[ADR056](adr/056-trusted-image-configuration-acquisition.md) extends the existing read-only deployment executable with original signed manifest→configuration hash/size checks and strict inherited Linux/nonroot/startup/environment/volume/source policy. It uses the pinned ECR4.0.102.3 SDK, explicit temporary session and fixed native regional TLS; one bounded registry credential is confined to exact own-registry configuration GETs. Fixed immutable read factories reuse the existing transport rather than adding a parallel SDK/HTTP stack. Core4.0.102.20/ECS4.0.111/SecurityToken4.0.101.10/SecretsManager4.0.100.25 remain pinned; all five packages are deployment-only.

Local372 combined tests,8 workload/signer tests and locked restore pass. The34-case native fixture derives startup configuration from packaged images and re-signs synthetic root/command/environment/index drift, with exact counters, distinct SigV4/Basic checks, a reachable verified-TLS forbidden destination, observed deadlines and owned cleanup. Full verification is recorded above. The independent [security review](reviews/2026-11-10-image-acquisition-review.md) records findings/remedies and executed no tests/AWS calls. Efficiency review remains outstanding after its agent's usage limit.

Source52b3cc8 passed packaged workflow42951430931, but source security correctly rejected a Host-selected unauthenticated test route inside the authenticated fake provider. The remedy isolates that receiver in its own internal-only fixture with independent counters/cleanup. The agent rechecked it without actionable findings; full source/native verification passed above. No scanner suppression/threshold changes.

[ADR054](adr/054-trusted-compute-task-acquisition.md) and [ADR055](adr/055-trusted-parked-service-acquisition.md) supply private session/STS/four-task/initial parked-cluster/service acquisition. Full source-specific verification is recorded once above. Their [reviews](reviews/2026-11-09-parked-service-review.md) preserve the earlier cancellation-fixture race and its observed-entry remedy; earlier partial evidence remains in the [previous checkpoint](https://github.com/creatoradams/saveIT/blob/fd75fcb/docs/progress.md). No source history is rewritten.

No launch/provider write, trusted network acquisition, ownership handoff or process/ENI fence is implemented by these prerequisites. Labels/hashes/configuration correspondence does not verify layer contents, binary provenance, signatures, scanning or promotion; tags/counts and sequential reads establish no ownership/absence proof. The [whole-section register](configuration-compute.md) remains open. No AWS credentials, provisioning, apply or customer data is used.

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

## Prior delivery: offline relay task preflight

[ADR049](adr/049-offline-relay-task-preflight.md) adds `check-relay-task` to the existing deployment executable. It independently checks supplied relay revision/image/configuration/role/runtime metadata, rejects semantic additions/duplicates and emits fixed content-free output under bounded input/EOF limits. It reuses protected input mechanics while retaining the original flat configuration schema; no dependency, infrastructure root/resource, tenant authority or launch switch is added. All services remain fixed at zero. Supplied data does not prove provenance/freshness, actual image/private configuration, RunTask override restrictions or launch approval.

Local focused validation passed **215 .NET tests with zero skipped**, including relay loader/liveness and flat configuration regressions. The ECS producer's95 mocked plans, TypeScript/build, ignore policy and native-fixture syntax/format checks pass. Full source/Linux/browser/package evidence for this source is above. Downloaded artifact **13648796929** matches archive SHA256 **6001b6ea76c24cb431a66526c9a06cd51b3dac0d06d930942a47344afb311138**; all four source/image/SBOM/report bindings verify. The security agent reviewed design/source/tests/docs/native fixture without actionable findings. This is neither cloud evidence nor release approval; the fresh independent efficiency review remains outstanding after the agent's usage limit.

## Next steps

[ADR053](adr/053-durable-compute-release-admission.md) now adds single-use signed intent persistence and body-free exact-operation inspection to the existing journal. Local **42 real PostgreSQL tests, zero skipped**, pass, including actual stdin CLI, non-superuser ownership with revoked PUBLIC schema usage, concurrent/replayed approvals, post-wait expiry, statement/deferred rollback, lost COMMIT acknowledgement and downgrade/no invented approval. A further **213 focused release/configuration regressions** pass. The [security review](reviews/2026-11-09-durable-compute-admission-review.md) found no actionable issue without executing tests. Build has zero warnings/errors; full new-source CI is pending. No new table, dependency, AWS action or tenant authority is added. Provider effects must still require independently reverified retained approvals and trustworthy acquired metadata.

[ADR051](adr/051-compute-coordination.md) is verified at the source above: seed-free core-control journal, immutable API/worker generations, restricted operator decisions, cooperative core quiescence and separate liveness/startup/readiness. This is not whole-cell drain/fencing.

[ADR052](adr/052-protected-compute-release-admission.md) adds strict independently pinned PS256 approval and derived exact four-task/public network contracts in the existing offline executable. Local **213 affected .NET tests, zero skipped**, **105 task mock plans**, zero-warning/error build, TypeScript/build, ignore and native-fixture syntax checks pass. The [security agent review](reviews/2026-11-09-compute-release-review.md) found no actionable issue; it did not execute tests. Twelve no-network packaged canaries are wired but full source/Linux/package evidence for this new implementation is pending. The [usage guide](compute-release-admission.md) explains trust custody, input bounds and the absence of replay/provider authority. No dependency, schema, runtime tenant authority, API contract or running service is added. Durable single-use admission, trusted acquisition, provider control/fencing, exclusive privileged-writer ownership, explicit Terraform handoff, restricted ingress/Okta egress and whole-section failure drills remain unfinished in the [section register](configuration-compute.md).

1. Add trusted acquisition/protected release authority and override prohibition around offline task checks, then bounded service change and operated health/drain/fencing for the parked contracts; retain keyless web and exact startup/version/role guards.
2. Complete restricted ingress/egress, protected population, signing/promotion, scheduling/PassRole and operated health/drain/fencing.
3. Add redacted telemetry/alerts, API/tenant quotas, operational audit archive/retention and recovery runbooks.
4. Use authorized staging accounts to verify actual IAM/KMS/PKI/RDS/Okta, multi-instance keys, recovery and capacity; remediate findings and obtain independent assessment before an approved pilot.

No AWS account or real credentials are needed for the next engineering step. The user selected **Okta** and has no nonproduction AWS account yet. No AWS credential/API/apply, real secret population, registry publication, live Okta or public deployment has been performed.

Before any relay apply, resolve whether direct regional Logs supplies the customer kms:CallerAccount required by the restrictive key policy; missing context fails closed and may block DescribeKey/ingestion. Mocks are not service-context or IAM evidence. Null bootstrap denies all AWS actions to both relay identities; binding_ready checks declared references only.

API completion cannot recall relay/Next/browser buffers. Lost response-protocol state during recovery still requires old-instance fencing. Existing Windows full-suite PostgreSQL backend-exit limitations remain unresolved and separate from the full Linux pass. Product and managed recovery gaps stay in [readiness](production-readiness.md) and [backlog](backlog.md).
