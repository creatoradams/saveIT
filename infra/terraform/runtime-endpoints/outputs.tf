output "runtime_network" {
  description = "Unapplied private endpoint/client references, not a launch configuration or managed reachability proof."
  value = {
    vpc_id                     = var.network.vpc_id, application_subnet_ids = var.network.application_subnet_ids
    client_security_groups     = local.clients
    interface_endpoint_ids     = { for name, endpoint in aws_vpc_endpoint.interface : name => endpoint.id }
    layer_endpoint_id          = aws_vpc_endpoint.layers.id
    application_route_table_id = var.network.application_route_table_id
  }
}

output "relay_admission" {
  description = "Validated endpoint-filter references only, or null. No relay network path, IAM grant/enforcement, workload attestation, secret population, immediate revocation or launch readiness."
  value = var.relay == null ? null : {
    roles          = var.relay.roles, configuration = var.relay.configuration
    repository_arn = var.relay.image_repository.arn, log_group_arn = var.relay.operational_log.arn
  }
}

output "compute_endpoint_policy_sha256" {
  description = "Declared policy digests for independent review before signing a complete compute release. These are not remote readback or effective policy evidence."
  value = var.relay_network_enabled && var.web_workload_pool != null ? merge(
    { for name, endpoint in local.interfaces : name => sha256(endpoint.policy) },
    { "web-token" = sha256(local.web_token_policy), layers = sha256(local.s3_policy) }
  ) : null
}

output "relay_network" {
  description = "Optional separate relay group with declared endpoint/layer paths. No API/web transport, SG attachment/rule enforcement, task or launch readiness."
  value = var.relay_network_enabled ? {
    vpc_id            = var.network.vpc_id, application_subnet_ids = var.network.application_subnet_ids
    security_group_id = aws_security_group.relay[0].id
  } : null
}
