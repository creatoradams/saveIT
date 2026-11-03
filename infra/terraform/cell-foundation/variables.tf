variable "account_id" {
  type        = string
  description = "Explicit authorized commercial AWS account; no ambient account inference."
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply a twelve-digit AWS account ID."
  }
}
variable "region" {
  type        = string
  description = "Explicit reviewed commercial region; examples are not residency decisions."
  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Supply a commercial AWS region; other partitions require a separate design."
  }
}
variable "cell" {
  type        = string
  description = "Bounded regional cell name, unrelated to request tenant authority."
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.cell))
    error_message = "Use two to twelve lowercase alphanumeric characters, beginning with a letter."
  }
}
variable "environment" {
  type        = string
  description = "Explicit staging or production intent; nothing is applied by tests."
  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "Choose staging or production."
  }
}
variable "availability_zones" {
  type        = list(string)
  description = "Two distinct actual account-specific AZ names in the selected region; verify orderability before apply."
  validation {
    condition     = length(var.availability_zones) == 2 && length(distinct(var.availability_zones)) == 2 && alltrue([for az in var.availability_zones : can(regex("^${var.region}[a-z]$", az))])
    error_message = "Supply two distinct AZs in the selected region."
  }
}
variable "vpc_cidr" {
  type        = string
  description = "Explicit nonoverlapping canonical 10.x.0.0/16 cell range; no IPv6 or public routing is defined."
  validation {
    condition     = can(regex("^10[.][0-9]{1,3}[.]0[.]0/16$", var.vpc_cidr)) && can(cidrnetmask(var.vpc_cidr))
    error_message = "Supply a valid canonical private 10.x.0.0/16 block; review overlap separately."
  }
}
variable "key_administrators" {
  type        = set(string)
  description = "Exact same-account key-administration IAM roles, including the infrastructure execution role. No wildcards."
  validation {
    condition     = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators : can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn))])
    error_message = "Supply one to eight exact same-account key-administrator role ARNs."
  }
}
variable "database_engine_version" {
  type        = string
  description = "Explicit PostgreSQL 18 major prefix, allowing automatic minor servicing without a downgrade request. Verify actual minor/orderability/compatibility before deployment."
  validation {
    condition     = var.database_engine_version == "18"
    error_message = "Use the PostgreSQL 18 major prefix; exact minor pins plus automatic servicing would request later downgrades."
  }
}
variable "database_instance_class" {
  type        = string
  description = "Explicit reviewed RDS class/budget; mocks do not prove availability or capacity."
  validation {
    condition     = can(regex("^db[.](m|r)[0-9]+g?[.](large|xlarge|[248]xlarge)$", var.database_instance_class))
    error_message = "Supply a reviewed bounded m/r instance class; burstable/shared classes need separate capacity review."
  }
}
variable "database_storage_gib" {
  type        = number
  description = "Initial gp3 allocation; bounded cost intent, not measured capacity."
  validation {
    condition     = floor(var.database_storage_gib) == var.database_storage_gib && var.database_storage_gib >= 20 && var.database_storage_gib <= 1000 && (var.environment != "production" || var.database_storage_gib >= 100)
    error_message = "Use 20-1000 integer GiB, with at least 100 GiB for production intent."
  }
}
