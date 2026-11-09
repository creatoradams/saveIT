locals {
  namespace            = "${var.cell}.${var.environment}.saveit.internal"
  subnets              = { for i, id in var.network.application_subnet_ids : "application-${i}" => id }
  relay_port           = 8443
  api_caller           = var.relay_network == null ? var.network.client_security_groups.web : var.relay_network.security_group_id
  api_path_description = var.relay_network == null ? "Declared web to API server-TLS path only; not client authentication" : "Declared relay to API TLS path only; not client or tenant authority"
}
data "aws_vpc" "cell" {
  id = var.network.vpc_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:vpc/${var.network.vpc_id}" &&
        self.owner_id == var.account_id && !self.default && self.enable_dns_support && self.enable_dns_hostnames &&
        length(self.cidr_block_associations) == 1 && length(self.ipv6_cidr_block_associations) == 0 &&
        can(regex("^10[.][0-9]{1,3}[.]0[.]0/16$", self.cidr_block)) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require the designated same-account/region/cell private IPv4 DNS-enabled VPC."
    }
  }
}
data "aws_subnet" "cell" {
  for_each = local.subnets
  id       = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:subnet/${each.value}" && self.vpc_id == var.network.vpc_id &&
        !self.map_public_ip_on_launch && self.ipv6_cidr_block == "" && startswith(self.availability_zone, var.region) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Tier, "") == "application")
      error_message = "Bind only the same-cell private IPv4 application subnets."
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
      error_message = "Require each exact designated process group in this VPC/account/region/cell. Metadata is not rule/attachment isolation evidence."
    }
  }
}
data "aws_security_group" "relay" {
  count = var.relay_network == null ? 0 : 1
  id    = var.relay_network.security_group_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:security-group/${var.relay_network.security_group_id}" && self.vpc_id == var.network.vpc_id &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Process, "") == "relay")
      error_message = "Require the designated same-account/region/VPC/cell relay group. Metadata is not rule or exclusive-attachment evidence."
    }
  }
}
resource "aws_service_discovery_private_dns_namespace" "cell" {
  name        = local.namespace
  description = "SaveIT private API discovery; no instances or workload authority"
  vpc         = var.network.vpc_id
  depends_on  = [data.aws_vpc.cell, data.aws_subnet.cell, data.aws_security_group.client, data.aws_security_group.relay]
  lifecycle {
    prevent_destroy = true
    precondition {
      condition     = length(distinct([for subnet in data.aws_subnet.cell : subnet.availability_zone])) == 2
      error_message = "Require two distinct application availability zones."
    }
  }
}
resource "aws_service_discovery_service" "api" {
  name          = "api"
  description   = "Private API registry only; DNS health does not gate access"
  force_destroy = false
  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.cell.id
    routing_policy = "MULTIVALUE"
    dns_records {
      type = "A"
      ttl  = 10
    }
  }
  # Provider6.71 reads AWS's constant1 into this Optional/ForceNew field.
  # Pin1 despite its deprecation to avoid unset->1 refresh replacement churn.
  # Public Route53 probing is not a private-DNS custom-health substitute.
  health_check_custom_config { failure_threshold = 1 }
  tags = { Process = "api" }
  lifecycle { prevent_destroy = true }
}
resource "aws_vpc_security_group_ingress_rule" "api_https" {
  security_group_id            = var.network.client_security_groups.api
  referenced_security_group_id = local.api_caller
  ip_protocol                  = "tcp"
  from_port                    = var.api_transport.port
  to_port                      = var.api_transport.port
  description                  = local.api_path_description
  depends_on                   = [aws_service_discovery_private_dns_namespace.cell]
}
resource "aws_vpc_security_group_egress_rule" "api_https" {
  security_group_id            = local.api_caller
  referenced_security_group_id = var.network.client_security_groups.api
  ip_protocol                  = "tcp"
  from_port                    = var.api_transport.port
  to_port                      = var.api_transport.port
  description                  = local.api_path_description
  depends_on                   = [aws_service_discovery_private_dns_namespace.cell]
}

resource "aws_service_discovery_service" "relay" {
  count         = var.relay_network == null ? 0 : 1
  name          = "relay"
  description   = "Private relay registry only; no instances, health publisher or caller authority"
  force_destroy = false
  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.cell.id
    routing_policy = "MULTIVALUE"
    dns_records {
      type = "A"
      ttl  = 10
    }
  }
  # Same pinned-provider Optional/ForceNew readback constraint as the API registry.
  health_check_custom_config { failure_threshold = 1 }
  tags = { Process = "relay" }
  lifecycle { prevent_destroy = true }
}
resource "aws_vpc_security_group_ingress_rule" "relay_https" {
  count                        = var.relay_network == null ? 0 : 1
  security_group_id            = var.relay_network.security_group_id
  referenced_security_group_id = var.network.client_security_groups.web
  ip_protocol                  = "tcp"
  from_port                    = local.relay_port
  to_port                      = local.relay_port
  description                  = "Declared web to relay TLS path only; not workload or tenant authority"
  depends_on                   = [aws_service_discovery_private_dns_namespace.cell]
}
resource "aws_vpc_security_group_egress_rule" "relay_https" {
  count                        = var.relay_network == null ? 0 : 1
  security_group_id            = var.network.client_security_groups.web
  referenced_security_group_id = var.relay_network.security_group_id
  ip_protocol                  = "tcp"
  from_port                    = local.relay_port
  to_port                      = local.relay_port
  description                  = "Declared web to relay TLS path only; not workload or tenant authority"
  depends_on                   = [aws_service_discovery_private_dns_namespace.cell]
}
