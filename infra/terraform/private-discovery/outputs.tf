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
