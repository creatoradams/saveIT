locals {
  name       = "saveit-${var.cell}-${var.environment}"
  namespace  = "${var.cell}.${var.environment}.saveit.internal"
  tasks      = merge(var.task_definitions, { relay = var.relay_task.task_definition_arn })
  groups     = merge({ for name in ["api", "web", "worker"] : name => var.api_discovery.client_security_groups[name] }, { relay = var.relay_task.network.security_group_id })
  subnets    = { for i, id in var.api_discovery.application_subnet_ids : "application-${i}" => id }
  registries = { api = var.api_discovery.service_arn, relay = var.relay_discovery.service_arn }
}

# Only network/discovery metadata. Never read secret versions or container ENV.
data "aws_vpc" "cell" {
  id = var.api_discovery.vpc_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:vpc/${var.api_discovery.vpc_id}" && self.owner_id == var.account_id && !self.default && self.enable_dns_support && self.enable_dns_hostnames &&
        length(self.cidr_block_associations) == 1 && length(self.ipv6_cidr_block_associations) == 0 && can(regex("^10[.][0-9]{1,3}[.]0[.]0/16$", self.cidr_block)) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require designated private IPv4 DNS-enabled same-account/region/cell VPC metadata."
    }
  }
}
data "aws_subnet" "cell" {
  for_each = local.subnets
  id       = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:subnet/${each.value}" && self.vpc_id == var.api_discovery.vpc_id && !self.map_public_ip_on_launch && self.ipv6_cidr_block == "" && startswith(self.availability_zone, var.region) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Tier, "") == "application")
      error_message = "Require the same-cell private IPv4 application subnet metadata."
    }
  }
}
data "aws_security_group" "client" {
  for_each = local.groups
  id       = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:security-group/${each.value}" && self.vpc_id == var.api_discovery.vpc_id &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Process, "") == each.key)
      error_message = "Require each own designated process group. Metadata is not rule, route, exclusive attachment or workload authority proof."
    }
  }
}
data "aws_service_discovery_dns_namespace" "cell" {
  name = local.namespace
  type = "DNS_PRIVATE"
  lifecycle {
    postcondition {
      condition = (self.id == var.api_discovery.namespace_id && self.arn == var.api_discovery.namespace_arn &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require the exact own private DNS namespace, never a shared/foreign namespace."
    }
  }
}
data "aws_route53_zone" "cell" {
  # Name/private/VPC filtering observes a matching association; using zone_id
  # would bypass the pinned provider's VPC filter. Extra associations are not exposed.
  name         = local.namespace
  private_zone = true
  vpc_id       = var.api_discovery.vpc_id
  lifecycle {
    postcondition {
      condition     = can(regex("^Z[A-Z0-9]{1,31}$", self.zone_id)) && self.zone_id == data.aws_service_discovery_dns_namespace.cell.hosted_zone
      error_message = "The private zone selected through the designated VPC must be this exact namespace's hosted zone; not exclusive-association evidence."
    }
  }
}
data "aws_service_discovery_service" "registry" {
  for_each     = local.registries
  name         = each.key
  namespace_id = var.api_discovery.namespace_id
  # Pinned source reads remote tags back into this Optional-only field. Mocks
  # cannot override it; configured tags are not returned-tag drift evidence.
  tags = { Project = "SaveIT", Cell = var.cell, Environment = var.environment, Process = each.key }
  lifecycle {
    postcondition {
      condition = try(self.arn == each.value && self.namespace_id == var.api_discovery.namespace_id &&
        one(self.dns_config).namespace_id == var.api_discovery.namespace_id && one(self.dns_config).routing_policy == "MULTIVALUE" &&
        one(one(self.dns_config).dns_records).type == "A" && one(one(self.dns_config).dns_records).ttl == 10 &&
        length(self.health_check_config) == 0 && one(self.health_check_custom_config).failure_threshold == 1 &&
      self.tags.Project == "SaveIT" && self.tags.Cell == var.cell && self.tags.Environment == var.environment && self.tags.Process == each.key, false)
      error_message = "Require exact API/relay private A/MULTIVALUE10s/custom-health1 registry metadata. Health is not fail-closed access."
    }
  }
}

resource "aws_ecs_cluster" "cell" {
  name = local.name
  setting {
    name  = "containerInsights"
    value = "disabled"
  }
  depends_on = [data.aws_vpc.cell, data.aws_subnet.cell, data.aws_security_group.client, data.aws_route53_zone.cell, data.aws_service_discovery_service.registry]
  lifecycle {
    prevent_destroy = true
    precondition {
      condition     = length(distinct([for subnet in data.aws_subnet.cell : subnet.availability_zone])) == 2
      error_message = "Use two distinct private application availability zones."
    }
  }
}
resource "aws_ecs_service" "application" {
  for_each        = local.tasks
  name            = "${local.name}-${each.key}"
  cluster         = aws_ecs_cluster.cell.arn
  task_definition = each.value
  # Deliberately no activation/count/autoscaling input. This is not account-side
  # enforcement against administrators, other schedulers or out-of-band changes.
  desired_count                      = 0
  launch_type                        = "FARGATE"
  platform_version                   = "1.4.0"
  scheduling_strategy                = "REPLICA"
  enable_execute_command             = false
  enable_ecs_managed_tags            = true
  propagate_tags                     = "SERVICE"
  force_delete                       = false
  force_new_deployment               = false
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  wait_for_steady_state              = true
  deployment_controller { type = "ECS" }
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }
  network_configuration {
    subnets          = var.api_discovery.application_subnet_ids
    security_groups  = [local.groups[each.key]]
    assign_public_ip = false
  }
  dynamic "service_registries" {
    for_each = contains(keys(local.registries), each.key) ? [local.registries[each.key]] : []
    content { registry_arn = service_registries.value }
  }
  tags = { Process = each.key }
  lifecycle { prevent_destroy = true }
  timeouts {
    create = "10m"
    update = "10m"
    delete = "10m"
  }
}

output "parked_services" {
  description = "Unapplied zero-count service/network/registry intent only. No running tasks, registration, health, drain, fencing, exclusive isolation or launch approval."
  value = {
    cluster_arn   = aws_ecs_cluster.cell.arn
    services      = { for name, service in aws_ecs_service.application : name => service.id }
    desired_count = 0
  }
}
