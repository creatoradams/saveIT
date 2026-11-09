variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply the explicit twelve-digit account."
  }
}
variable "region" {
  type = string
  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Supply a reviewed commercial region."
  }
}
variable "cell" {
  type = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.cell))
    error_message = "Use a two-to-twelve character lowercase cell name."
  }
}
variable "environment" {
  type = string
  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "Choose staging or production."
  }
}
variable "task_definitions" {
  type        = map(string)
  nullable    = false
  description = "ecs-tasks.task_definitions only. Exact revisions, never family/latest aliases or promotion/launch approval. No container configuration is queried here."
  validation {
    condition = toset(keys(var.task_definitions)) == toset(["api", "web", "worker"]) && alltrue([for name, arn in var.task_definitions :
      can(regex("^arn:aws:ecs:${var.region}:${var.account_id}:task-definition/saveit-${var.cell}-${var.environment}-${name}:[1-9][0-9]{0,9}$", arn)) &&
    try(tonumber(element(split(":", arn), length(split(":", arn)) - 1)) <= 2147483647, false)])
    error_message = "Bind exactly the three own canonical API/web/worker task ARNs with positive bounded explicit revisions."
  }
}
variable "api_discovery" {
  nullable = false
  type = object({
    namespace_id = string, namespace_arn = string, service_arn = string, hostname = string, origin = string, port = number
    vpc_id       = string, application_subnet_ids = list(string), client_security_groups = map(string)
  })
  description = "private-discovery.api_discovery output, not DNS/health/authority evidence."
  validation {
    condition = (can(regex("^ns-[a-z0-9]{16}$", var.api_discovery.namespace_id)) &&
      var.api_discovery.namespace_arn == "arn:aws:servicediscovery:${var.region}:${var.account_id}:namespace/${var.api_discovery.namespace_id}" &&
      can(regex("^arn:aws:servicediscovery:${var.region}:${var.account_id}:service/srv-[a-z0-9]{16}$", var.api_discovery.service_arn)) &&
      var.api_discovery.hostname == "api.${var.cell}.${var.environment}.saveit.internal" && var.api_discovery.origin == "https://api.${var.cell}.${var.environment}.saveit.internal:8443" && var.api_discovery.port == 8443 &&
      can(regex("^vpc-[0-9a-f]{17}$", var.api_discovery.vpc_id)) &&
      length(var.api_discovery.application_subnet_ids) == 2 && length(distinct(var.api_discovery.application_subnet_ids)) == 2 &&
      alltrue([for id in var.api_discovery.application_subnet_ids : can(regex("^subnet-[0-9a-f]{17}$", id))]) &&
      toset(keys(var.api_discovery.client_security_groups)) == toset(["api", "web", "worker", "deployment"]) &&
      length(distinct(values(var.api_discovery.client_security_groups))) == 4 &&
    alltrue([for id in values(var.api_discovery.client_security_groups) : can(regex("^sg-[0-9a-f]{17}$", id))]))
    error_message = "Require exact same-cell API8443/private namespace/registry and explicit VPC/two application subnets/four distinct designated groups."
  }
}
variable "relay_task" {
  nullable = false
  type = object({
    task_definition_arn = string, origin = string, api_origin = string, port = number
    network             = object({ vpc_id = string, application_subnet_ids = list(string), security_group_id = string })
  })
  description = "Nonnull ecs-tasks.relay_task. Bound metadata only; parked services never authorize launch."
  validation {
    condition = (can(regex("^arn:aws:ecs:${var.region}:${var.account_id}:task-definition/saveit-${var.cell}-${var.environment}-relay:[1-9][0-9]{0,9}$", var.relay_task.task_definition_arn)) &&
      try(tonumber(element(split(":", var.relay_task.task_definition_arn), length(split(":", var.relay_task.task_definition_arn)) - 1)) <= 2147483647, false) &&
      var.relay_task.origin == "https://relay.${var.cell}.${var.environment}.saveit.internal:8443" && var.relay_task.api_origin == var.api_discovery.origin && var.relay_task.port == 8443 &&
      var.relay_task.network.vpc_id == var.api_discovery.vpc_id && length(var.relay_task.network.application_subnet_ids) == 2 &&
      toset(var.relay_task.network.application_subnet_ids) == toset(var.api_discovery.application_subnet_ids) &&
      can(regex("^sg-[0-9a-f]{17}$", var.relay_task.network.security_group_id)) &&
    !contains(values(var.api_discovery.client_security_groups), var.relay_task.network.security_group_id))
    error_message = "Bind the exact revisioned isolated relay task with separate canonical targets and the same VPC/subnet set plus its own distinct group."
  }
}
variable "relay_discovery" {
  nullable = false
  type = object({
    namespace_id = string, namespace_arn = string, service_arn = string, hostname = string, origin = string, api_origin = string, port = number
    vpc_id       = string, application_subnet_ids = list(string), security_group_id = string
  })
  description = "Nonnull private-discovery.relay_discovery, in the same protected namespace and exact task network."
  validation {
    condition = (var.relay_discovery.namespace_id == var.api_discovery.namespace_id && var.relay_discovery.namespace_arn == var.api_discovery.namespace_arn &&
      can(regex("^arn:aws:servicediscovery:${var.region}:${var.account_id}:service/srv-[a-z0-9]{16}$", var.relay_discovery.service_arn)) && var.relay_discovery.service_arn != var.api_discovery.service_arn &&
      var.relay_discovery.hostname == "relay.${var.cell}.${var.environment}.saveit.internal" && var.relay_discovery.origin == var.relay_task.origin && var.relay_discovery.api_origin == var.api_discovery.origin && var.relay_discovery.port == 8443 &&
      var.relay_discovery.vpc_id == var.api_discovery.vpc_id && length(var.relay_discovery.application_subnet_ids) == 2 &&
    toset(var.relay_discovery.application_subnet_ids) == toset(var.api_discovery.application_subnet_ids) && var.relay_discovery.security_group_id == var.relay_task.network.security_group_id)
    error_message = "Require the distinct same-namespace relay registry with exact relay/API targets and the same declared relay network."
  }
}
