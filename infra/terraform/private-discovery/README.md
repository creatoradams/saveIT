# Private API discovery (unapplied)

[ADR038](../../../docs/adr/038-private-api-discovery-and-network-path.md) defines only a private Cloud Map namespace/API registry and the standalone web-to-API TCP8443 directions. It creates no ECS service, registered instance, health publisher, IAM authority, client workload authentication or publicly reachable endpoint. Do not launch workloads from these outputs; service/client/ingress authentication, secret population, protected promotion and managed verification remain gates.

Bind VPC/application subnets/four process groups from `runtime-endpoints.runtime_network`, and `api_transport` unchanged from `ecs-tasks.api_transport`. Origin, fixed8443 and server-TLS declaration must agree with this cell. The example is synthetic metadata, not real IDs or approval. Web still receives no secret or new AWS action. Namespace/registry prevent_destroy and false force_destroy protect against incidental source-driven deletion while these guards remain; trusted administrators can change them.

```sh
terraform init -backend=false -lockfile=readonly
terraform fmt -check -recursive
terraform validate
terraform test
```

Tests use only provider mocks and distinct plan-time identities; never pass credentials or run apply for tests. Keep lockfiles/examples tracked and plans/state/local variables ignored. Adjacent root suites test actual module outputs as well as mismatches. Source metadata checks do not prove DNS resolution, IAM, TLS, packet reach, SG/routing/attachment drift or managed health.

The namespace is private `<cell>.<environment>.saveit.internal`, with an `api` A/MULTIVALUE registry and ten-second record TTL. Its private custom-health block is for future ECS health management. Pin the retired threshold to1 despite the provider warning: the pinned provider reads AWS's constant1 into an Optional/ForceNew field, so leaving it unset risks replacement churn. Do not hide other drift. No registration/health updater exists. AWS DNS can return unhealthy records when all are unhealthy; TTL is not a client-cache/socket/failover bound or access gate. Provider custom-health deprecation needs integration review before an upgrade; public health probing is not an equivalent private-health replacement.

The root owns exactly two new standalone SG rules, web egress/API ingress TCP8443 by referenced group only. Keep foundation database, key-storage NFS and runtime-endpoint rule ownership unchanged. SG metadata/tags do not establish exclusive task attachment; SGs do not contain Amazon resolver traffic. Inspect real SG/NACL/routes/DHCP/resolver/namespace associations and registrar IAM before authorized staging. Server TLS still needs approved certificates/CA inputs and managed hostname/chain checks. Client workload authentication, service registry attachment/health/drain, HTTPS ingress/restricted egress, protected jobs/promotion, telemetry/quotas and recovery remain unfinished.
