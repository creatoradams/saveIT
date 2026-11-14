# Configuration and compute completion

Scope confirmed by the user: complete the account-free configuration-and-compute section, then stop at live AWS verification. This register tracks engineering acceptance separately from managed evidence. The full enterprise roadmap stays in backlog/feature-parity; this is not a production release approval.

## Engineering acceptance

- [ ] Protected launch/change: one durable expected-version controller, trusted metadata acquisition, all four task definitions checked, fixed network/role/image/configuration targets, no arbitrary overrides, explicit handoff from zero-count Terraform ownership.
- [x] Authenticated intake implementation/synthetic verification: server-only issuance client, existing verified web/relay TLS and relay/API mTLS, strict standard JWT verification, browser header replacement and no forwarding of the workload credential to API/browser/logs. Actual AWS issuance/claims/IAM verification remains below.
- [ ] Health/drain: separate liveness/readiness/startup; atomic refusal of new admissions while existing work finishes; durable response completion remains required.
- [ ] Fencing: exact owned process/task termination checks; uncertain acknowledgements/orphan responses block; no protocol TTL, force-close or pruning bypass.
- [ ] Restricted paths: scoped private ingress, authenticated web/relay, relay/API, designated DB/EFS/endpoints and bounded Okta egress; direct bypass and unexpected destinations refused.
- [ ] Executable evidence: real packaged processes and synthetic control-plane tests for concurrent operations, stalled work, crash/partition/uncertain outcomes; required repository checks and independent source review.

## Design boundaries

ADR052 supplies an independently pinned short-lived signed release and exact API/web/worker/relay metadata checks. ADR053 now persists verified approval identity with expected-version/single-use admission and body-free uncertain-operation inspection in the existing journal. Trusted provider acquisition, side-effect intent/ownership and actual launch/change/fencing are still unfinished. The completion boxes above remain open until the complete controller and failure evidence exist.

ADR054 implements read-only STS/four-task acquisition with a private temporary-session carrier, original raw metadata validation and fixed SDK/TLS transports. ADR055 adds exact initial parked-cluster/four-service observations, and ADR056 adds original manifest/configuration acquisition. Exact verified source/tests are in [progress](progress.md). Network acquisition and provider write/reconciliation authority remain unfinished. These commands do not launch or fence any task.

ADR051 now supplies cooperative API/worker generation and core-quiescence primitives, a durable operator journal and separate health modes. Whole-cell drain, exclusive privileged-writer ownership and exact process/provider fencing are still required; CoreQuiescent is not a snapshot/fence acknowledgement. All services remain at zero in Terraform.

ADR057 reads three exact approved private versions and validates database-target/PKI/key consistency without retaining private values; full verification is in progress. ADR058 adds signed network metadata and initial13-SG acquisition; other raw network/resource reads and full controller operation remain unfinished. These prerequisites close no acceptance box. Managed secret selection/injection remains an account-side gate.

ADR059 checks VPC/DNS flags, exact private subnets, explicit associations and all three route tables before any future controller effect.

ADR060 verifies the six signed endpoints and declared policy digests plus the exact regional AWS-owned S3 prefix. Effective policy enforcement, Resolver and independent attachment checks, exclusive ownership and launch/drain/fencing remain open. See [ADR060](adr/060-bound-private-endpoint-acquisition.md) and [progress](progress.md).

ADR061 verifies the VPC's DHCP options and independently cross-checks the ten interface endpoint attachments. Resolver rules, DNS Firewall, actual answers, attachments for other resource types, exclusive ownership and launch/drain/fencing remain open. [ADR061](adr/061-bound-dhcp-and-endpoint-interfaces.md)

### Block 2 — Resolver and DNS Firewall

- [x] Inspect Terraform and the signed release contract for declared DNS policy.
- [x] Record the missing DNS Firewall policy. The current roots declare zero custom direct forwarding and firewall associations.
- [x] Add fixed regional read-only checks bound to the approved VPC.
- [x] Verify the direct baseline and reject drift, foreign references and incomplete results.
- [x] Confirm the synthetic SDK and packaged Linux checks. All 361 local acquisition regressions and 88 packaged acquisition cases passed, including seven Resolver cases. [Packaged CI](https://github.com/creatoradams/saveIT/actions/runs/43539335185)

Approving and declaring a filtering policy, verifying Route 53 Profile associations and testing live DNS answers remain open. [ADR062](adr/062-direct-resolver-baseline.md)

### Block 3 — Private DNS and resource attachments

- [x] Bind the private hosted zone ID and namespace ARN to the signed release. Names follow the existing cell and environment convention.
- [x] Read back the Cloud Map namespace, both registries and their empty parked instance inventories.
- [ ] Read back Route 53 private zone associations and records. Cloud Map metadata alone does not verify the Route 53 zone.
- [ ] Verify RDS identity, private subnet placement and the dedicated database group.
- [ ] Verify EFS mount targets, their subnet and group placement, and the approved access point.
- [x] Implement the parked task inventory. The signed cluster must return a complete, empty desired-RUNNING task list. All 76 local parked-state tests passed.
- [ ] Match post-launch tasks to their exact task revisions and independent ENI observations.
- [ ] Complete synthetic and packaged evidence for all Block 3 reads. Verify live DNS, database TLS and EFS mounts during managed staging.

Keep existing ten roots and zero-count/null bootstrap contracts. Add no secret values to Terraform. Workload admission uses a dedicated Cognito developer identity pool solely for web, not customer identity; Okta and live SQL tenant authority are unchanged. Web never reads a PFX or configuration secret. The client requests60s tokens; IAM limits issuance to the isolated pool/endpoint, and relay independently limits accepted lifetime. This is not an IAM-enforced duration or developer-identifier restriction. Relay validates standard signed JWTs using reviewed public keys, exact issuer/audience/provider and at most60s lifetime; tokens are bearer capabilities, not per-task attestation or proof of possession. No SaveIT role trusts the pool for STS exchange. Public Cognito operations cannot be controlled by an IAM Deny alone; strict receiver lifetime/amr checks remain mandatory.

Preserve verified web/relay TLS rather than replacing it with Lattice's unverified backend certificate handling. Preserve separate API mTLS, current session/organization authority and all28 forced-RLS tables. No controller may close response-protocol records; only server OnCompleted can do that. A provider stop acknowledgement alone is not fencing evidence. Protected release/signing/population prerequisites remain explicit dependencies, not caller-supplied approval booleans.

## Managed evidence required after engineering

Actual IAM/transitive isolation and endpoint enforcement; Cognito issuer/claims/public operations/rotation; ECR manifests/pulls and protected configuration versions; ECS registration, packet reachability and verified task/ENI stop; PKI, RDS roles/TLS, EFS multi-instance access; old-instance/recovery drills; direct regional Logs customer CallerAccount compatibility. No AWS account, credentials, API/apply or production data is used by the account-free tests. These gates cannot be marked verified from synthetic fixtures.

Exact source/check results belong in [progress](progress.md). Independent efficiency review remains outstanding because its agent reached the account usage limit; source self-review cannot replace it.
