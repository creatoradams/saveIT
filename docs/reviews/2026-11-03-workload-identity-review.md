# Workload identity and configuration-reference review

Two user-requested agents independently inspected the ADR 031 delta read-only: workload-identity Terraform, mock-plan tests, ADR/README and integration guidance. They made no edits, ran no tests and used no AWS credentials/APIs/apply. The security agent checked current primary AWS documentation for ECS selectors, Secrets Manager VersionId conditions and KMS context. Primary-agent execution is separately recorded in [progress](../progress.md).

The efficiency/correctness agent found no actionable issue in dependency ordering, deterministic role-ARN conditions, separate identity/selector wiring, bounded policy sizes or fail-closed EFS bootstrap/binding. It inspected the initial 29 plan cases. The primary agent subsequently added a thirtieth security predicate case and strengthened assertions; no infrastructure behavior changed through those test changes.

The security review identified two claim/test improvements, now addressed:

- Trusted key/metadata administrators can use UpdateSecret for metadata and payload replacement, as well as change policies. Code, ADR, README and deployment guidance now state this clearly. PutSecretValue/version-stage custodian restrictions are not an exclusive value-writer guarantee. Workload identities cannot perform those actions.
- Complete deny assertions now include the whole nine-statement KMS policy, repository/log boundary statements and bound-task access-point/TLS/mount-target denies. Independent missing/wrong CallerAccount and ViaService cases read the generated separate predicates, guarding against accidentally combining them into an AND condition. Thirty mock plans pass locally; they are declared-policy/narrow predicate checks, not an IAM simulator.

The security agent completed a final recheck of these remedies and found no remaining actionable issue in this bounded delta. No whole-repository independent audit, safety certification or deployed enforcement claim follows. Both privileged account administrators and secret custodians remain trusted external identities. Actual IAM/Secrets Manager/KMS enforcement, propagation/drift, scheduler/PassRole controls, payload validation/injection, EFS mounts/replica sessions and operated recovery/fencing remain unverified. Compute, protected promotion and production operations are still unfinished.
