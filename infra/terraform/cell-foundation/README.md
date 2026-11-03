# SaveIT private network/database foundation

Unapplied partial application-cell infrastructure under ADR 028. No AWS credentials/API calls, paid resources or application deployment are used by the tests. The parent root remains a scaffold; this root does not run the app or provide operator connectivity.

Credential-free checks (Terraform 1.13.5, pinned AWS 6.71.0):

```sh
terraform -chdir=infra/terraform/cell-foundation init -backend=false -lockfile=readonly
terraform -chdir=infra/terraform/cell-foundation fmt -check -recursive
terraform -chdir=infra/terraform/cell-foundation validate
terraform -chdir=infra/terraform/cell-foundation test
```

Tests use mocked **plan** operations with distinct plan-time subnet/security-group IDs. No apply or AWS authentication is needed. Lock hashes are copied from the already verified identical provider/platform selection, not newly invented; `init -lockfile=readonly` verifies the public provider. Mocks assert configuration, not actual IAM/KMS enforcement, service compatibility, credentials isolation or recovery.

The definition creates only isolated VPC/subnets/local routes, an empty default SG, designated future process SGs/private database paths, a rotating protected storage key and RDS subnet/parameter/instance intent. Public routes/IPs, NAT, internet/VPC endpoints, application services, HTTPS ingress and operator networks are absent. Terraform standalone rules manage declared rules, not all privileged out-of-band additions; actual drift monitoring/CloudTrail and reachability checks are release work. No public or web-to-database path is defined.

The database bootstrap password is generated and managed by RDS/Secrets Manager. No secret value or version is generated/read by Terraform. The service's AWS-managed Secrets Manager key protects that separate bootstrap secret; the custom key protects database storage/backups. Key administrators/account root/IAM administrators are privileged boundaries. Do not use the bootstrap master login for application/worker/migration runtime. Actual restricted-owner/function/forced-RLS behavior, SSL/CA, role/secret lifecycle and recovery need managed staging verification with the existing guards intact.

TLS and limited statement/error logging are configured, but generic database errors and service metadata still need redaction review. Backup retention/final snapshots/deletion protection/prevent-destroy are settings, not tested managed restore or measured RPO/RTO. Staging is single-instance; production intent forces multi-AZ and at least 100 GiB. The PostgreSQL major prefix is fixed to 18 so automatic minor servicing cannot make Terraform request a downgrade. The actual minor is exposed as metadata and must pass compatibility/orderability review; instance class inputs must be checked against actual region/account orderability; examples do not establish availability or capacity. Auto-minor servicing and maintenance windows need operational ownership. A reused final-snapshot name must never overwrite evidence; any authorized retirement needs a reviewed unique target and deliberate protection changes.

Before **any real plan/apply**, settle account/region/residency/budget/network overlap, exact infrastructure/key roles, service orderability, protected state and operating ownership. Use a separate reviewed cell state key such as `cells/pilot-staging/terraform.tfstate` in the existing state-bootstrap bucket with native locks and account restrictions. Prepare only ignored backend configuration/overrides, run a reviewed init/migrate-state process and verify lock/version/key recovery. Never commit state/plans/secrets or run an unreviewed apply. None has been performed.

This is not a ready-to-deploy cell. Compute/registry/promotion/ingress/restricted egress, workload secrets/persisted certificate/key ring, private migration/operator execution/fencing, actual data/key recovery, redacted observability/quotas and independent assurance remain separate gates. All enterprise inventory entries remain in scope.

AZ inputs are sorted before allocating CIDR slots, so reordering the same two zones does not replace subnets. The pinned provider supports major-version prefixes with automatic minor servicing; exact-minor inputs are rejected. Capture actual engine minor/maintenance outcomes and rerun managed compatibility tests before promotion. AWS warns that blanket KMS denies can affect internal managed-resource access despite IAM-principal exceptions: actual resource-owning-account/service-linked grant creation, backup, maintenance, failover and recovery must be verified before any deployment. The mocked tests are not permission to apply this root; no broad same-account deny bypass is introduced.
