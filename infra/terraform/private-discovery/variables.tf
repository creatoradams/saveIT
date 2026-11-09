variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply an explicit twelve-digit account."
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
    error_message = "Supply a two-to-twelve character lowercase cell name."
  }
}
variable "environment" {
  type = string
  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "Choose staging or production."
  }
}
variable "network" {
  description = "VPC/subnets/process groups selected from runtime-endpoints.runtime_network, not routing/SG enforcement evidence."
  nullable    = false
  type        = object({ vpc_id = string, application_subnet_ids = list(string), client_security_groups = map(string) })
  validation {
    condition = (can(regex("^vpc-[0-9a-f]{17}$", var.network.vpc_id)) &&
      length(var.network.application_subnet_ids) == 2 && length(distinct(var.network.application_subnet_ids)) == 2 &&
      alltrue([for id in var.network.application_subnet_ids : can(regex("^subnet-[0-9a-f]{17}$", id))]) &&
      toset(keys(var.network.client_security_groups)) == toset(["api", "web", "worker", "deployment"]) &&
      length(distinct(values(var.network.client_security_groups))) == 4 &&
    alltrue([for id in values(var.network.client_security_groups) : can(regex("^sg-[0-9a-f]{17}$", id))]))
    error_message = "Require an explicit VPC, two distinct application subnets and four distinct designated process groups."
  }
}
variable "api_transport" {
  description = "ecs-tasks.api_transport output: declared server TLS only, never client identity, live PKI or launch approval."
  nullable    = false
  type        = object({ origin = string, port = number, server_tls_declared = bool })
  validation {
    condition = (var.api_transport.origin == "https://api.${var.cell}.${var.environment}.saveit.internal:8443" &&
    var.api_transport.port == 8443 && var.api_transport.server_tls_declared)
    error_message = "Bind the same cell's exact HTTPS API DNS name and fixed8443 server-TLS task contract."
  }
}

variable "relay_network" {
  type        = object({ vpc_id = string, application_subnet_ids = list(string), security_group_id = string })
  default     = null
  description = "Optional runtime-endpoints.relay_network output. Null retains initial direct web/API declarations; nonnull requires a distinct same-cell relay group and selects web/relay/API paths."
  validation {
    condition = var.relay_network == null || try(
      var.relay_network.vpc_id == var.network.vpc_id &&
      length(var.relay_network.application_subnet_ids) == 2 &&
      toset(var.relay_network.application_subnet_ids) == toset(var.network.application_subnet_ids) &&
      can(regex("^sg-[0-9a-f]{17}$", var.relay_network.security_group_id)) &&
    !contains(values(var.network.client_security_groups), var.relay_network.security_group_id), false)
    error_message = "Bind the same VPC/exact private application subnet set and a separate relay group, never a web/API/worker/deployment group."
  }
}
