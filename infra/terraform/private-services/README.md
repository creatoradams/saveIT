# Parked private services (unapplied)

[ADR048](../../../docs/adr/048-parked-private-service-composition.md) composes task and discovery outputs downstream in a separate service/state/release lifecycle. It defines one cluster and four REPLICA Fargate services, all with **hardcoded desired_count=0**. No launch/count/activation input, autoscaling, ignore_changes, IAM grant, public ingress, secret/task-content lookup or new SG rule is added. Nothing has been applied, started or registered.

Bind ecs-tasks.task_definitions and its nonnull relay_task, plus private-discovery.api_discovery and nonnull relay_discovery. Exact revisioned task families, separate origins, same namespace/VPC/subnet set and distinct groups must agree. Each service declares only its own SG and the same private application subnets, no public IP or Exec. Only API and relay attach their exact A-record registries; worker/web have no registry. No migration/access service exists. The pinned Linux platform version is1.4.0; AWS manages revisions within that version, so it is not a runtime-image or kernel-revision pin.

Lookups check scoped VPC/private two-AZ subnets/process groups, the private namespace and exact A/MULTIVALUE/TTL10/custom-health1 registries. Route53 selection uses name/private_zone/vpc_id filters, then compares the returned zone ID with namespace.hosted_zone. Supplying zone_id instead would bypass the pinned provider's VPC filter. This observes a matching association, not absence of additional/shared associations. SG metadata does not prove actual rule/routes/attachments or workload identity.

The pinned Cloud Map service source reads remote tags into an Optional-only tags field. Expected tags are configured and its postcondition checks returned scope, but Terraform mocks cannot override that noncomputed field. Mock tag inputs are not returned-tag drift rejection evidence. Test other computed metadata mismatches and retain actual tag/association/DNS/IAM verification as a launch gate.

Run only account-free checks with Terraform1.13.5 and the tracked AWS6.71.0 lock:

```sh
terraform init -backend=false -lockfile=readonly
terraform fmt -check -recursive
terraform validate
terraform test
```

Tests use provider mocks and synthetic public metadata. Ordinary plan/apply is not part of these checks. Keep lock/example tracked and all state/plans/local tfvars/evidence ignored. Task ARNs are producer contracts; protected live inspection of task contents, digest/image/public-CA/configuration/receiver-pin binding remains unfinished. No private payload belongs here or in Terraform state.

Zero is declared intent, not an account-enforced launch prohibition or proof that old tasks/instances are absent. Trusted external actors can run/scale workloads. Importing/applying against active services can scale them down without the required operated drain/fencing. Do not use this root as rollback, cleanup, revocation or recovery automation. Protected lifecycle/force_delete=false are source guards which trusted administrators can change. Circuit-breaker settings are not measured rollback or zero-downtime evidence.

Before any authorized apply, review protected state/change ownership, collisions/imports/drift, deployment-role PassRole and ECS service-linked-role scope: AWS can automatically create AWSServiceRoleForECS when a cluster/service is created. This source adds no IAM resource/grant; that does not imply zero IAM side effects. Real registration/container-health publication, API readiness versus relay liveness, DNS all-unhealthy behavior, routing/egress/ingress, protected population/promotion, launch controls, drain/old-instance fencing and managed recovery remain unfinished. Desired0 registers no instances or health updates here; no services are running.
