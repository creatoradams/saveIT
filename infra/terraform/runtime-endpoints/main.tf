locals {
  name = "saveit-${var.cell}-${var.environment}"
  subnets = merge(
    { for i, id in var.network.application_subnet_ids : "application-${i}" => { id = id, tier = "application" } },
    { for i, id in var.network.database_subnet_ids : "database-${i}" => { id = id, tier = "database" } }
  )
  tables = { application = var.network.application_route_table_id, database = var.network.database_route_table_id }
  interfaces = {
    ecr-api       = { service = "ecr.api", policy = local.ecr_policy }
    ecr-dkr       = { service = "ecr.dkr", policy = local.ecr_policy }
    logs          = { service = "logs", policy = local.logs_policy }
    configuration = { service = "secretsmanager", policy = local.secrets_policy }
  }
  clients          = merge(var.network.client_security_groups, { web = aws_security_group.web.id })
  endpoint_clients = merge(local.clients, var.relay_network_enabled ? { relay = aws_security_group.relay[0].id } : {})
  interface_paths  = { for pair in setproduct(keys(local.interfaces), keys(local.endpoint_clients)) : "${pair[0]}-${pair[1]}" => { endpoint = pair[0], client = pair[1] } if pair[0] != "configuration" || pair[1] != "web" }
}
data "aws_vpc" "cell" {
  id = var.network.vpc_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:vpc/${var.network.vpc_id}" && self.owner_id == var.account_id && !self.default &&
        self.enable_dns_support && self.enable_dns_hostnames && length(self.cidr_block_associations) == 1 && length(self.ipv6_cidr_block_associations) == 0 &&
        can(regex("^10[.][0-9]{1,3}[.]0[.]0/16$", self.cidr_block)) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require the designated same-account/region/cell private IPv4 DNS-enabled VPC."
    }
  }
}
data "aws_subnet" "cell" {
  for_each = local.subnets
  id       = each.value.id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:subnet/${each.value.id}" && self.vpc_id == var.network.vpc_id && !self.map_public_ip_on_launch && self.ipv6_cidr_block == "" &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Tier, "") == each.value.tier)
      error_message = "Bind only the designated private same-cell application/database subnets."
    }
  }
}
data "aws_security_group" "client" {
  for_each = var.network.client_security_groups
  id       = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:security-group/${each.value}" && self.vpc_id == var.network.vpc_id &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Process, "") == each.key)
      error_message = "Require the exact designated foundation process group in this account/region/VPC/cell."
    }
  }
}
data "aws_prefix_list" "s3" {
  name = "com.amazonaws.${var.region}.s3"
}
data "aws_route_table" "cell" {
  for_each       = local.tables
  route_table_id = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:route-table/${each.value}" && self.owner_id == var.account_id && self.vpc_id == var.network.vpc_id &&
        try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Tier, "") == each.key &&
        length(self.associations) == 2 && alltrue([for a in self.associations : !a.main && (a.gateway_id == null || a.gateway_id == "")]) &&
        toset([for a in self.associations : a.subnet_id]) == toset(each.key == "application" ? var.network.application_subnet_ids : var.network.database_subnet_ids) &&
        # AWS provider 6.71 filters local, gateway-endpoint, propagated and some
        # service routes. Empty here rejects reported routes, not hidden drift.
      length(self.routes) == 0)
      error_message = "Require exact separated subnet associations and no provider-reported routes. Hidden service/propagated routes still require direct AWS verification."
    }
  }
}
resource "aws_security_group" "web" {
  name_prefix = "${local.name}-web-"
  description = "Future web process; no ingress, database or configuration endpoint path"
  vpc_id      = var.network.vpc_id
  tags        = { Process = "web" }
}
resource "aws_security_group" "endpoint" {
  for_each    = local.interfaces
  name_prefix = "${local.name}-${each.key}-endpoint-"
  description = "Private ${each.value.service} endpoint; declared client443 paths only"
  vpc_id      = var.network.vpc_id
}
resource "aws_security_group" "relay" {
  count       = var.relay_network_enabled ? 1 : 0
  name_prefix = "${local.name}-relay-"
  description = "Separate relay process; endpoint paths here, web/API paths owned by discovery"
  vpc_id      = var.network.vpc_id
  tags        = { Process = "relay" }
}
resource "aws_vpc_security_group_ingress_rule" "interface" {
  for_each                     = local.interface_paths
  security_group_id            = aws_security_group.endpoint[each.value.endpoint].id
  referenced_security_group_id = local.endpoint_clients[each.value.client]
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_vpc_security_group_egress_rule" "interface" {
  for_each                     = local.interface_paths
  security_group_id            = local.endpoint_clients[each.value.client]
  referenced_security_group_id = aws_security_group.endpoint[each.value.endpoint].id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_vpc_security_group_egress_rule" "layers" {
  for_each          = local.endpoint_clients
  security_group_id = each.value
  prefix_list_id    = data.aws_prefix_list.s3.id
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
resource "aws_vpc_endpoint" "interface" {
  for_each            = local.interfaces
  vpc_id              = var.network.vpc_id
  service_name        = "com.amazonaws.${var.region}.${each.value.service}"
  vpc_endpoint_type   = "Interface"
  private_dns_enabled = true
  ip_address_type     = "ipv4"
  subnet_ids          = var.network.application_subnet_ids
  security_group_ids  = [aws_security_group.endpoint[each.key].id]
  policy              = each.value.policy
  dns_options { dns_record_ip_type = "ipv4" }
  depends_on = [data.aws_vpc.cell, data.aws_subnet.cell, data.aws_security_group.client, data.aws_route_table.cell]
  lifecycle {
    precondition {
      condition = (length(distinct([for name, s in data.aws_subnet.cell : s.availability_zone if startswith(name, "application-")])) == 2 &&
      toset([for name, s in data.aws_subnet.cell : s.availability_zone if startswith(name, "application-")]) == toset([for name, s in data.aws_subnet.cell : s.availability_zone if startswith(name, "database-")]))
      error_message = "Require distinct application AZs matching the two database AZs."
    }
    precondition {
      condition     = length(each.value.policy) <= 18000
      error_message = "Keep each ASCII endpoint policy below the 20,480-byte service limit with headroom."
    }
  }
}
resource "aws_vpc_endpoint" "layers" {
  vpc_id              = var.network.vpc_id
  service_name        = "com.amazonaws.${var.region}.s3"
  vpc_endpoint_type   = "Gateway"
  ip_address_type     = "ipv4"
  private_dns_enabled = false
  subnet_ids          = []
  security_group_ids  = []
  route_table_ids     = [var.network.application_route_table_id]
  policy              = local.s3_policy
  depends_on          = [data.aws_vpc.cell, data.aws_subnet.cell, data.aws_security_group.client, data.aws_route_table.cell]
}
