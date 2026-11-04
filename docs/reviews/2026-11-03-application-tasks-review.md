# Guarded application task review — 2026-11-03

The two agents requested by the user performed separate read-only efficiency/operational and security reviews of ADR 033, the API probe/CLI/tests, Docker scratch/health definitions, Terraform task/input/lookup/contract tests and related documentation. They did not execute tests, containers, AWS requests, IAM simulation or apply. This bounded source review is not a penetration test or independent assurance of the entire repository.

Findings addressed in source:

- Readiness flags alone could accept different concrete EFS access-point IDs or log keys from the identities' bound references. Workload-identity now exports its declared nullable bindings; tasks require exact filesystem/access-point equality and the five distinct log-key map, and compare looked-up log keys. Real alternate-module contract plans include successful consumption and mismatched AP/key denials. Null bootstrap is retained upstream and rejected for compute.
- Blocking operational-log delivery could stall application output during a log/KMS/network outage. Tasks use nonblocking delivery with an explicit 4 MiB buffer. Documentation states that operational events can be lost and that durable PostgreSQL application audit is separate.
- The worker's 1,000-tenant application bound would exceed ECS's 64 KiB definition limit when transported as environment entries. Task definitions accept at most 100 unique tenants, guard ASCII container JSON at 24 KiB and test the largest accepted list and the oversized rejection. The worker application's own bound is unchanged.

Security review also required explicit pre-launch treatment of the private HTTP web/API hop. ADR, root README, deployment and readiness documents retain authenticated encrypted inter-service transport, trusted forwarding, RDS CA delivery and private launch networking as unfinished engineering. Private addressing is not transport protection. No cluster/services/launch or public deployment is introduced.

Actual Linux execution then caught mode 0755 rather than 0700 on the directly copied empty scratch directory, while UID/GID 1654 passed. Commit 4549980 copies the prepared parent contents, retaining runtime/keys child-directory metadata; it adds no runtime root/chmod workaround and does not weaken the strict canary. Both agents rechecked the small correction without actionable findings. The corrected packaged Linux run passes; exact immutable run links and scan snapshot are in progress.

Both final read-only rechecks found no remaining actionable issue in the bounded delta. Exact-source CI evidence is recorded separately in progress when complete. The implementing agent separately runs real PostgreSQL/HTTP/process tests, provider mocks, frontend checks and Linux packaged CI. Managed IAM drift/transitive authority, payload/injection validity, Fargate directory/mount/log behavior, actual identity, operated fencing/recovery and measured capacity remain unverified release gates.
