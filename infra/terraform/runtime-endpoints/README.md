# Private runtime service endpoints (unapplied)

[ADR 034](../../../docs/adr/034-private-runtime-service-endpoints.md) defines four private interface endpoints (ECR API/DKR, Logs, Secrets Manager), one application-only regional S3 image-layer gateway, a web client group and scoped standalone TCP443 rules. This root launches no task and creates no public/default route, NAT, gateway to the internet, ingress service or secret value. No AWS resources/API requests, account credentials or paid services are used by mocked tests. The cell remains incomplete.

Use the updated cell-foundation `network` output unchanged, including separate application/database route-table IDs and the tagged private subnets/designated process groups. Bind workload-identity `workloads` and `configuration` outputs into `identity`; only metadata is accepted, never payloads. Unlike compute, endpoint preparation can consume unbound bootstrap references and grants no task authority. ECS still requires both concrete EFS/log bindings and all existing runtime guards.

Foundation owns subnets/table associations and existing database paths. This root owns the S3 gateway association, its five groups and its new standalone rules; key-storage retains its NFS rules. Application table routes are not managed inline. Database routing stays declared local-only and receives no new gateway association. The new web group has no declared ingress, database or configuration-endpoint path; only API/worker/deployment groups can reach the configuration endpoint. Migration/access use the deployment network group but retain distinct execution identities and secret/log policies.

Endpoint policies supplement IAM/resource/KMS policies. Explicit deny boundaries retain five exact execution identities, pull-only own repositories (shared deployment image for jobs), create/put-only own log streams and four own exact configuration ARN/UUID reads. There is no log query, secret write/control, publisher, task role, scheduler or PassRole grant. S3 permits TLS GetObject only on the documented AWS-owned regional starport layer bucket with Principal '*', because presigned service requests cannot be assumed to carry our execution-role principal. This is not anonymous authorization, a repository-level layer filter or tenant-file/backup storage.

Metadata guards check owner/account/region/cell, VPC/subnet/group scope, private IPv4/DNS settings, distinct AZs and exact separated route-table associations. The **pinned provider filters local, gateway-endpoint, propagated, Lattice and some service-ENI routes** from route-table data: `routes = []` rejects only reported routes. It does not prove complete route isolation or detect hidden service/propagation drift. Security-group metadata lookups also do not prove absence of additional rules or task group attachments. Security groups cannot block Amazon DNS resolver traffic; resolver firewall/query monitoring remains unfinished. Before launch inspect unfiltered AWS routes/propagations/endpoints/ENIs/DHCP/resolver rules, SG/NACL drift and actual allowed/denied TLS/service paths. Mocks are declared contracts, not IAM/reachability/managed-ingestion evidence.

Account-free checks (Terraform 1.13.5, tracked AWS 6.71.0 signed lock):

```sh
terraform fmt -check -recursive
terraform init -backend=false -lockfile=readonly
terraform validate
terraform test
```

Tests include negative namespace/role/version/network/metadata cases, full declared policy boundaries and plans of the actual foundation/identity modules consumed by this root. Keep locks/sanitized examples tracked and state/plans/local values/evidence ignored. The example is synthetic and is not permission to apply. Before an authorized cloud plan/apply, configure protected separate state, exact infrastructure roles, budget/residency/overlap/orderability and ownership; no apply has occurred.

Still unfinished: authenticated encrypted web/API transport and trusted forwarding, database CA delivery, restricted Okta egress, DNS/HTTPS ingress, cluster/services/private task launch, protected migration/access jobs/scheduler/PassRole/publishing/signing/promotion, redacted telemetry/alerts/quotas and operated recovery/fencing. Then verify actual AWS/Okta/IAM/DNS/TLS/secret injection/EFS/log/recovery/capacity behavior. These endpoints do not complete enterprise parity or make the application production-ready.
