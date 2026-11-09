# Private API discovery (unapplied)

[ADR038](../../../docs/adr/038-private-api-discovery-and-network-path.md) defines a private Cloud Map namespace/API registry and initial standalone web-to-API TCP8443 directions. [ADR046](../../../docs/adr/046-relay-network-and-private-discovery.md) optionally replaces this pair with web-to-relay and relay-to-API TCP8443 directions and a separate private relay registry. It creates no ECS service, registered instance, health publisher, IAM authority, client workload authentication or publicly reachable endpoint. Do not launch workloads from these outputs; service/client/ingress authentication, secret population, protected promotion and managed verification remain gates.

Bind VPC/application subnets/four process groups from `runtime-endpoints.runtime_network`, and `api_transport` unchanged from `ecs-tasks.api_transport`. Origin, fixed8443 and server-TLS declaration must agree with this cell. The example is synthetic metadata, not real IDs or approval. Web still receives no secret or new AWS action. Namespace/registry prevent_destroy and false force_destroy protect against incidental source-driven deletion while these guards remain; trusted administrators can change them.

```sh
terraform init -backend=false -lockfile=readonly
terraform fmt -check -recursive
terraform validate
terraform test
```

Tests use only provider mocks and distinct plan-time identities; never pass credentials or run apply for tests. Keep lockfiles/examples tracked and plans/state/local variables ignored. Adjacent root suites test actual module outputs as well as mismatches. Source metadata checks do not prove DNS resolution, IAM, TLS, packet reach, SG/routing/attachment drift or managed health.

The namespace is private `<cell>.<environment>.saveit.internal`, with an `api` A/MULTIVALUE registry and ten-second record TTL. Its private custom-health block is for future ECS health management. Pin the retired threshold to1 despite the provider warning: the pinned provider reads AWS's constant1 into an Optional/ForceNew field, so leaving it unset risks replacement churn. Do not hide other drift. No registration/health updater exists. AWS DNS can return unhealthy records when all are unhealthy; TTL is not a client-cache/socket/failover bound or access gate. Provider custom-health deprecation needs integration review before an upgrade; public health probing is not an equivalent private-health replacement.

Bind optional relay_network from the separate runtime-endpoints.relay_network output. Require the exact VPC/subnet set and distinct scoped relay group; malformed bindings fail closed. Bound mode replaces both API rule endpoints with the relay group and adds only web/relay directions, four standalone referenced-group TCP8443 rules total. relay_discovery keeps relay origin and API target distinct; task environment/service bindings remain unfinished. The relay registry uses the same protected lifecycle/private DNS/custom-health settings as API. Null retains initial declarations, not safe live rollback: it can restore direct web/API, and protected registry removal may block a transition. Withdrawal/drain/fencing needs separate review.

Keep foundation database, key-storage NFS and runtime-endpoint rule ownership unchanged. SG metadata/tags do not establish exclusive task attachment; SGs do not contain Amazon resolver traffic. Inspect real SG/NACL/routes/DHCP/resolver/namespace associations and registrar IAM before authorized staging. Server TLS still needs approved certificates/CA inputs and managed hostname/chain checks. Client workload authentication, service registry attachment/health/drain, HTTPS ingress/restricted egress, protected jobs/promotion, telemetry/quotas and recovery remain unfinished.
