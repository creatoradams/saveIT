# SaveIT state bootstrap

This is an unapplied state-storage definition, not a cloud deployment. See ADR 024. The application cell at the parent root remains a scaffold. Region/account/principals are explicit inputs; the example uses synthetic identifiers. No AWS access key, secret value or password belongs in Terraform input, backend files, outputs or state.

Credential-free checks (Terraform 1.13.5):

```sh
terraform -chdir=infra/terraform/bootstrap init -backend=false -lockfile=readonly
terraform -chdir=infra/terraform/bootstrap fmt -check -recursive
terraform -chdir=infra/terraform/bootstrap validate
terraform -chdir=infra/terraform/bootstrap test
```

Tests use only a mocked AWS provider and plan operations. Provider download uses the public registry and its signed checksums. Windows/Linux hashes are tracked in `.terraform.lock.hcl`. These tests cannot prove AWS IAM/KMS enforcement, actual service compatibility or state recovery.

The bucket blocks public access, enforces ownership, retains versions, defaults to its rotating KMS key and refuses accidental Terraform destruction. Policies require TLS/approved principals, reject conflicting encryption and customer-supplied keys, protect state versions from ordinary operator deletion and allow lock-file deletion separately. Ordinary key use must pass through regional S3 for this bucket. Key administrators and account-root delegation are privileged recovery/control-plane boundaries. The bootstrap execution role needs actual IAM permissions to configure these resources and must be included in `key_administrators`; principal existence is not checked by mocks.

Before an authorized real apply, settle account/region/residency/budget, use short-lived SSO/assumed-role credentials, review the plan and exact principals, configure CloudTrail access auditing and own the key/state recovery procedure. Do not apply merely to satisfy a test. No such apply has been performed.

The initial bootstrap root deliberately uses ignored local state, because its remote bucket does not exist yet. After authorized creation, use the output identifiers to prepare an ignored `bootstrap.backend.hcl` (`bucket`, `region`, `allowed_account_ids`, `kms_key_id`, `encrypt=true`, `use_lockfile=true`, `key="bootstrap/terraform.tfstate"`). The tracked staging example is an identifier-only shape, not actual deployment configuration. Add an ignored `backend_override.tf`:

```hcl
terraform {
  backend "s3" {}
}
```

Then run a reviewed `terraform init -migrate-state -backend-config=bootstrap.backend.hcl`. Confirm the remote state/version/lock behavior and recovery before retiring the protected local copy. Never print or commit state.

Application cells use explicit keys such as `cells/staging/terraform.tfstate`; the policy does not authorize Terraform workspace paths. Native S3 lock files are used; no DynamoDB table or locking migration is implied. Do not use `-lock=false` for normal operations. Bucket/versioning is not immutable or offsite backup; test version/key recovery and audit access in the actual account.

Primary guidance: [S3 backend and lock files](https://developer.hashicorp.com/terraform/language/backend/s3), [mocked provider tests](https://developer.hashicorp.com/terraform/language/tests/mocking). Actual infrastructure/network/database/runtime/promotion implementation and managed verification remain separate work.
