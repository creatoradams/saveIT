# Private session-key storage definition

This separate **unapplied** root defines only persistent encrypted session-key-ring storage for a future cell/environment. It is not an ECS deployment, a mounted key ring, a secret-manager client or an operated backup/recovery system. See [ADR 030](../../../docs/adr/030-private-session-key-storage.md).

Use the pinned Terraform 1.13.5/AWS 6.71.0 lock with:

```powershell
terraform -chdir=infra/terraform/key-storage fmt -check -recursive
terraform -chdir=infra/terraform/key-storage init -backend=false -lockfile=readonly
terraform -chdir=infra/terraform/key-storage validate
terraform -chdir=infra/terraform/key-storage test
```

Tests use provider mocks and synthetic metadata; they require no AWS account/credentials and perform no AWS calls. Initialization downloads the public signed provider. Mocks check declarations and input/lookup failures, not IAM enforcement, KMS grants, mounts, throughput or managed recovery. Distinct subnet, security-group and resource identities keep wiring assertions meaningful. A regional EFS mock explicitly supplies the provider's empty computed AZ instead of an invented random string.

Before any real plan/apply, configure a separately reviewed protected remote backend/state key using the state-bootstrap guidance, and verify actual account/region/budget, foundation outputs and an existing exact API **task role**, distinct from execution/key-administrator roles. The role must exist before policy creation. This root adds one standalone API TCP 2049 egress rule to the foundation-owned SG; keep both roots on standalone rules, without inline/all-destination rules or a second manager for that rule. Provider lookups reject wrong VPC/public-address subnets and same-AZ targets. They do not inspect route tables, SG drift or establish that operator-supplied IDs came from the right cell: bind reviewed foundation outputs, maintain local-only/private routing and monitor account drift.

The dedicated regional filesystem uses rotating customer-KMS file-data encryption, protected key/filesystem lifecycles, two private application-subnet targets and API-group-only NFS. EFS metadata uses the service-managed key. The access point enforces UID/GID 1654 and a dedicated 0700 directory, matching current API images. Client policy explicitly denies other/anonymous roles, absent/wrong access points, insecure/non-mount-target use and client root access. Only the exact API task role gets mount/write through TLS and that access point. KMS use/grant/control boundaries reserve privileged administration to exact administrator/account principals; administrators can change policy. Distinct supplied role ARNs do not prove transitive IAM isolation: actual task/execution/web/worker role creation must deny unintended role assumption and policy-management escalation. Actual EFS service grant/backup/maintenance behavior still requires managed verification. No PFX or other secret value enters Terraform.

Future task wiring must use `session_key_storage.task_volume` with IAM authorization/transit encryption enabled and mount it at `/var/saveit/keys` for `Identity__KeyDirectory`; compute code is still missing. The API must also receive the same protected current/retired PFX configuration across replicas. Read/write key-ring access is session-key authority: never attach this group/role/mount to web/worker/deployment tasks or mix environments. The runtime must not receive direct EFS/KMS administration. The filesystem policy is not tenant isolation; existing PostgreSQL/session/live membership controls remain authoritative.

Automatic backup **enrollment** is enabled, but retention, vault protection, cross-region/offsite copies, backup roles, recovery permissions and actual restore/key/session/fencing drills are unfinished. There is deliberately no general recovery-client exception. A reviewed recovery procedure must coordinate isolated restoration, protector availability and old-instance fencing before policy changes. Disabling/deleting the key can make retained files inaccessible. Terraform `prevent_destroy` is a configuration guard, not protection from account administrators or removal of the resource block. Bursting throughput is an unmeasured small-ring starting configuration, not a capacity or availability promise. No account, purchase, public endpoint or paid resource has been created.
