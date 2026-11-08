# SaveIT isolated relay identity

Unapplied metadata/policies under [ADR043](../../../docs/adr/043-isolated-relay-identity.md). This root creates no running relay, secret value/version, image, endpoint, task/service or loader. Mock plans require no AWS account credentials or apply.

Declare separate ECS task/execution roles, two protected rotating KMS keys, one protected finite-retention operational log group and one configuration-secret metadata container. The relay reuses the deployment image repository, with its own identities; migration/access operator roles must never run the relay. The existing five workload identities and four repositories/five operational groups/six keys in their separate roots retain their contracts.

The task role reserves exact whole-secret ARN and UUID GetSecretValue, plus its configuration key's Decrypt through regional Secrets Manager with exact account/secret/version context. The isolated [ADR044 loader](../../../docs/adr/044-pinned-relay-startup-loader.md) now implements this startup protocol; its exact test status is in [progress](../../../docs/progress.md), and this root does not launch or bind it. Execution can only authorize regional ECR access, pull the exact deployment repository, write its own log streams and use its own log key through regional Logs. Separate DescribeKey has no encryption context. Execution has no configuration read/decrypt authority; task has no log/image/database/EFS/operator authority. Explicit-deny boundaries constrain additional identity/session grants. Workload IAM never supplies user/tenant authority.

## Staged binding

1. Keep image_repository null. Both identities explicitly deny all AWS actions; protected stores/roles are still declared.
2. Feed the exact relay execution-role output into artifact-stores.relay_execution_role_arn. That existing root remains the only repository-policy owner and admits this canonical role only to deployment pulls.
3. Feed the actual artifact-stores deployment repository output into image_repository. The exact ARN, URL and three distinct migration/access/relay execution-role references must match.

binding_ready means only this declared reference contract is checked. Mocks cannot inspect policy drift or prove IAM, ECR pull, KMS context availability, populated versions, SDK execution or launch. A null withdrawal plan is not applied revocation or fencing evidence. A real staged apply requires separate protected state/approvals; none has occurred.

Trusted administrators/custodians are bounded same-account external roles. They can change controls; UpdateSecret may also replace a value, so custodians are not exclusive payload writers. No secret value belongs in Terraform, tfvars, output, CLI arguments, logs or committed test fixtures. The example and policy contract contain only synthetic metadata. Seven configuration field names describe the existing stdin carrier, not selectors or working private delivery.

## Account-free checks

Use Terraform1.13.5 and the tracked signed AWS6.71.0 lock:

```sh
terraform -chdir=infra/terraform/relay-identity fmt -check -recursive
terraform -chdir=infra/terraform/relay-identity init -backend=false -lockfile=readonly
terraform -chdir=infra/terraform/relay-identity validate
terraform -chdir=infra/terraform/relay-identity test
```

Thirty-six mock plans cover null/bound shapes, complete independent policy JSON, missing/wrong-context predicates, exact configuration fields, invalid inputs, maximum-size trusted roles, key separation and an actual staged artifact-stores module contract. The checked-in synthetic policies.json is a reviewed normative contract; do not regenerate it from implementation output. Predicate tests are narrow condition checks, not an IAM simulator. Ignore state/plans/.terraform/local values; track the provider lock and sanitized example.

## Remaining deployment gates

Verify the ADR044 loader, then implement private endpoint admissions for the relay execution role's ECR/Logs calls and the task role's Secrets Manager calls, referenced network/discovery, isolated task/service definitions, public trust/digest binding, protected population/promotion/PassRole, health/drain and operated fencing/recovery. Existing runtime-endpoints admits legacy execution roles only; it cannot deliver this configuration unchanged. Same-task containers share task-role authority and cannot provide independent credential isolation.

Before any apply, verify the retained log-key assumption that direct regional Logs calls supply the customer kms:CallerAccount required by both deny and allow statements. Forwarded-role CallerAccount is a separate path. AWS's published direct-service example uses the regional principal and exact log-group encryption context without this account predicate. Mocks deliberately cannot settle context availability: missing customer CallerAccount fails closed and may prevent creation/ingestion/description. Review against managed service evidence before applying; do not remove account/service/context boundaries opportunistically to make deployment pass. Actual DescribeKey/PutLogEvents, wrong-account/key/group/principal denials and account-side drift all require authorized staging verification.

Operational retention is finite (default90 days); this is not immutable tenant audit storage, log collection/redaction/queries/alerts or approved legal retention. Managed PKI/Okta/RDS/IAM, multi-instance keys, rotation, recovery and capacity/SLOs remain unfinished. Exact execution/review evidence is in [progress](../../../docs/progress.md); the full enterprise roadmap remains in [backlog](../../../docs/backlog.md).
