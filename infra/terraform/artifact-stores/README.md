# SaveIT private image and operational log stores

Unapplied infrastructure under [ADR 032](../../../docs/adr/032-private-image-and-log-stores.md). This root defines resources/policies only. No images were published, logs collected, AWS APIs called or paid resources created. Full compute/deployment remains unfinished.

Four private ECR repositories match the workload-identity contract: API, web, worker and the shared migration/access deployment image. Tags are immutable without exclusions, KMS encryption and basic scan-on-push are declared, force-delete is disabled and Terraform prevents destruction. No expiry/lifecycle rule is created, including for untagged layers, rollback candidates or future attached signatures/inventories. Repository policies deny missing/false TLS, foreign readers, unauthorized publishing, image/tag deletion and nonadministrator control changes. They grant no identity permissions. Runtime execution roles already receive exact pull permissions from the separate identity root; the single protected publisher still needs a reviewed identity/trust/promotion path. Administrators/root can amend policies and remain trusted.

Five distinct STANDARD CloudWatch groups match API/web/worker/migration/access execution log destinations. Each has its own rotating protected KMS key, exact log-group encryption context and deletion protection. The configurable operational retention defaults to 90 days; supported choices are 30, 60, 90, 120, 150, 180 and 365. Retention expiry still removes events. These are operational stores, not a legally approved retention policy, tenant audit archive, redaction/alert pipeline or immutable retention system. This root creates no log query-reader permissions. Future readers need separately reviewed IAM/KMS authority. Never send secret values, request bodies, document/history content or restore reasons into these logs.

One separate rotating KMS key protects this cell's four image repositories through regional ECR/account/exact-repository context and resource-service grants. Five log keys limit nonadministrator data use to each exact group and either the regional Logs service principal or regional Logs forwarding. Forwarding admission alone grants no caller identity authority. Privileged key administrators/root may change policies, grant permissions, disable keys or change retention; authenticated operator controls, monitoring and managed denial/recovery tests remain gates. With eventual apply, six customer-managed keys plus storage/scanning/ingestion have service costs; no budget or operating capacity is approved by these definitions.

## Account-free verification

Use Terraform 1.13.5 and the tracked AWS 6.71.0 provider lock:

```sh
terraform -chdir=infra/terraform/artifact-stores fmt -check -recursive
terraform -chdir=infra/terraform/artifact-stores init -backend=false -lockfile=readonly
terraform -chdir=infra/terraform/artifact-stores validate
terraform -chdir=infra/terraform/artifact-stores test
```

Thirty-one plan cases use distinct synthetic provider overrides, complete policy-shape assertions and narrow wrong/missing-context cases. The contract test actually plans the existing `../workload-identity` module and compares its role names/image/log grants to the new resource references. It does not copy a second identity implementation. Mocks are not an IAM simulator, real KMS/ECR grant/Logs enforcement, scan result or deployment evidence. Public provider installation requires registry access; tests use no AWS credentials/APIs.

## Future controlled application

Bind the returned five `log_groups.*.key_arn` references into workload-identity's `operational_log_keys`. Its unbound bootstrap intentionally lacks log-key crypto. The binding adds exact own-key via-Logs crypto/context and separate exact-key description, with matching explicit-deny boundaries and configuration-key separation. Actual encrypted PutLogEvents requires this caller authority as well as the Logs service key policy. Future compute must gate on both log-key-binding-ready and EFS-binding-ready; ARN naming alone is insufficient. No log-query actions or task-role permissions are added by the binding, and no managed ingestion has been tested.

The sanitized example contains metadata only. Real local variables, backend configuration, state and plans remain ignored. Establish authorized account/region/budget, protected remote state, existing infrastructure/key-administrator and separate publisher roles, reviewed plans and authenticated operator controls before any apply. No payload or secret belongs in Terraform. Keep the full [enterprise scope](../../../docs/backlog.md) visible.

The infrastructure identity must be among key administrators. In addition to reviewed service provisioning rights it needs the ECR-documented KMS create/describe/retire-grant authority; `kms:RetireGrant` must be in its identity policy. Do not revoke ECR service grants or disable/delete keys as a way of revoking image-pull access: that can make stored data unavailable. ECR encryption choice is fixed at repository creation. Log key disassociation/retention changes do not recover expired events; old keys must remain available for retained encrypted data.

Review exact repository policies and actual allowed/denied publisher/runtime requests, KMS service-grant creation, ingestion/query/context/retention and account-side drift in isolated staging. Existing registry-wide scanning settings can override the intended basic scan behavior; this root does not own account-wide configuration or implement an enhanced-scanning contract. Existing same-image CI vulnerability gates still apply. No image signature, OCI referrer preservation, immutable registry promotion or rollback has been exercised here.

Next engineering: protected publishing/signing/promotion, ECS task/service definitions, private endpoints/network/HTTPS ingress/restricted egress, operator scheduling/PassRole/connectivity, log collection/redaction/alerts/quotas, then actual AWS/Okta/managed recovery/capacity verification. Workload IAM never grants tenant authority or changes current membership/organization/RLS/response-completion checks.
