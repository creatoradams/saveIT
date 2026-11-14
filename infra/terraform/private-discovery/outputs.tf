output "api_discovery" {
  description = "Unapplied registry/network references only; no instances, health, client identity or launch approval."
  value = {
    namespace_id           = aws_service_discovery_private_dns_namespace.cell.id
    namespace_arn          = aws_service_discovery_private_dns_namespace.cell.arn
    service_arn            = aws_service_discovery_service.api.arn
    hostname               = "api.${local.namespace}"
    origin                 = var.api_transport.origin
    port                   = var.api_transport.port
    vpc_id                 = var.network.vpc_id
    application_subnet_ids = var.network.application_subnet_ids
    client_security_groups = var.network.client_security_groups
  }
}

output "relay_discovery" {
  description = "Optional declared relay registry/transport targets only. No instances, active health, actual TLS/caller identity or launch approval; API and relay targets stay distinct."
  value = var.relay_network == null ? null : {
    namespace_id = aws_service_discovery_private_dns_namespace.cell.id, namespace_arn = aws_service_discovery_private_dns_namespace.cell.arn
    service_arn  = aws_service_discovery_service.relay[0].arn, hostname = "relay.${local.namespace}"
    origin       = "https://relay.${local.namespace}:${local.relay_port}", port = local.relay_port, api_origin = var.api_transport.origin
    vpc_id       = var.network.vpc_id, application_subnet_ids = var.network.application_subnet_ids, security_group_id = var.relay_network.security_group_id
  }
}

output "compute_private_dns" {
  description = "Namespace ARN and hosted zone ID for an independently reviewed signed release. This output does not verify DNS or hosted zone associations."
  value = var.relay_network == null ? null : {
    namespaceArn = aws_service_discovery_private_dns_namespace.cell.arn
    hostedZoneId = aws_service_discovery_private_dns_namespace.cell.hosted_zone
  }
}
