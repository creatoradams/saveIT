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
