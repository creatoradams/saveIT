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
  description = "The explicit cell-foundation network output after application/database routing separation. No ambient VPC selection."
  nullable    = false
  type = object({
    vpc_id                     = string, application_subnet_ids = list(string), database_subnet_ids = list(string)
    application_route_table_id = string, database_route_table_id = string, database_security_group = string
    client_security_groups     = map(string)
  })
  validation {
    condition = (can(regex("^vpc-[0-9a-f]{17}$", var.network.vpc_id)) &&
      length(var.network.application_subnet_ids) == 2 && length(var.network.database_subnet_ids) == 2 &&
      length(distinct(concat(var.network.application_subnet_ids, var.network.database_subnet_ids))) == 4 &&
      alltrue([for id in concat(var.network.application_subnet_ids, var.network.database_subnet_ids) : can(regex("^subnet-[0-9a-f]{17}$", id))]) &&
      var.network.application_route_table_id != var.network.database_route_table_id &&
      alltrue([for id in [var.network.application_route_table_id, var.network.database_route_table_id] : can(regex("^rtb-[0-9a-f]{17}$", id))]) &&
      toset(keys(var.network.client_security_groups)) == toset(["api", "worker", "deployment"]) &&
      length(distinct(concat(values(var.network.client_security_groups), [var.network.database_security_group]))) == 4 &&
    alltrue([for id in concat(values(var.network.client_security_groups), [var.network.database_security_group]) : can(regex("^sg-[0-9a-f]{17}$", id))]))
    error_message = "Require four distinct private subnets, separate application/database route tables and the three distinct designated client groups, excluding the database group."
  }
}
variable "identity" {
  description = "Nonsecret workload-identity workloads/configuration outputs. Endpoint filters add no IAM grant or secret value."
  nullable    = false
  type = object({
    workloads     = map(object({ execution_role_arn = string, repository_arn = string, log_group_arn = string }))
    configuration = map(object({ arn = string, version_id = string }))
  })
  validation {
    condition = (toset(keys(var.identity.workloads)) == toset(["api", "web", "worker", "migration", "access"]) &&
      alltrue([for name, w in var.identity.workloads :
        w.execution_role_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-${name}-execution" &&
        w.repository_arn == "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/${contains(["migration", "access"], name) ? "deployment" : name}" &&
        w.log_group_arn == "arn:aws:logs:${var.region}:${var.account_id}:log-group:/saveit/${var.cell}/${var.environment}/${name}"
      ]) && toset(keys(var.identity.configuration)) == toset(["api", "worker", "migration", "access"]) &&
      alltrue([for name, c in var.identity.configuration :
        can(regex("^arn:aws:secretsmanager:${var.region}:${var.account_id}:secret:saveit/${var.cell}/${var.environment}/${name}/config-[A-Za-z0-9]{6}$", c.arn)) &&
    can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", c.version_id))]))
    error_message = "Require the exact five own execution-role/repository/log references and four own UUID-pinned configuration metadata containers; web has none."
  }
}
