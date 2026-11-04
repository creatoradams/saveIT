# Guarded application task definitions (unapplied)

Partial compute definitions under [ADR 033](../../../docs/adr/033-guarded-application-task-definitions.md): three Linux x86-64 Fargate/awsvpc task definitions for API, web and search worker. No cluster, service, task launch, networking, scheduler/PassRole or cloud resource has been created. This root is not a runnable application cell.

Inputs are metadata only. Bind the workload-identity `workloads`, `session_key_binding_ready`, `log_key_binding_ready`, `session_key_binding` and `operational_log_keys` outputs into `identity`, and key-storage's `session_key_storage` output unchanged. Concrete filesystem/access-point IDs and five distinct log keys must agree, not merely readiness flags. Lookups check exact role/trust/boundary references, own immutable encrypted repositories, own encrypted finite-retention logs and private nonroot encrypted EFS metadata. Policy contents, transitive grants, payload validity and managed enforcement still need actual verification. Null bootstrap remains available in workload-identity, but is rejected here.

Supply actual registry `@sha256:` manifest digest references after protected publication. Synthetic example values are not published images, inventory configuration IDs, signatures, vulnerability/license approval or promotion evidence. No secret payload/version resource or value lookup belongs here. API fields and contiguous retired PFX pairs stay pinned to one own configuration ARN/version; worker receives only its connection; web receives no secret. Tenant authority remains the live database membership/organization policy, not workload metadata.

Tasks use UID/GID 1654, read-only roots, dropped capabilities, private writable `/var/saveit/runtime` matching Docker VOLUME metadata and explicit shell-free readiness checks. Only API mounts `/var/saveit/keys` through exact EFS IAM/TLS access-point settings. No privileged mode, extra commands/environment inputs, host paths or execute-command/SSM permissions are introduced. Initial resource allocations are unmeasured. Worker task transport accepts at most 100 distinct tenants and guards ASCII container JSON below 24 KiB, leaving headroom under the 64 KiB ECS definition limit. Operational logs have a 4 MiB nonblocking buffer; outages can lose them. They are separate from durable PostgreSQL application audit.

Run account-free validation with Terraform 1.13.5 and the tracked AWS 6.71.0 lock:

```sh
terraform fmt -check -recursive
terraform init -backend=false -lockfile=readonly
terraform validate
terraform test
```

Mocks cover declared wiring and negative input/metadata cases, including a plan of the actual workload-identity module consumed by these tasks. They do not evaluate IAM, publish images, inject secrets, mount EFS or ingest logs. Keep state/plans/local values and evidence ignored. The sanitized example deliberately cannot be deployed as a working environment.

Before launch: implement private endpoints/restricted Okta egress, DNS/HTTPS ingress and authenticated encrypted web-to-API transport (the declared private HTTP origin is a placeholder), trusted forwarding, RDS CA trust delivery, private task networking, cluster/services, protected migration/access jobs/scheduler/PassRole/promotion, redacted telemetry/alerts/quotas and operated recovery/fencing. Then verify actual AWS/Okta/IAM/TLS/mount/log/recovery/capacity behavior with authorized accounts. Private addressing and mocked flags do not satisfy those gates.
