variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply the exact authorized twelve-digit AWS account ID."
  }
}
variable "region" {
  type = string
  validation {
    condition     = length(var.region) <= 32 && can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Choose a reviewed commercial AWS region."
  }
}
variable "cell" {
  type = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.cell))
    error_message = "Use two to twelve lowercase alphanumeric characters beginning with a letter."
  }
}
variable "environment" {
  type = string
  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "Choose staging or production."
  }
}
variable "key_administrators" {
  type = set(string)
  validation {
    condition = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators :
      length(arn) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/([A-Za-z0-9+=,.@_-]+/)*[A-Za-z0-9+=,.@_-]{1,64}$", arn)) &&
      !contains(flatten([for workload in ["api", "web", "worker", "migration", "access", "relay"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${workload}-${kind}"]]), lower(element(split("/", arn), length(split("/", arn)) - 1)))
    ])
    error_message = "Supply one to eight exact same-account trusted administrator roles, excluding all twelve workload role names."
  }
}
variable "secret_custodians" {
  type = set(string)
  validation {
    condition = length(var.secret_custodians) > 0 && length(var.secret_custodians) <= 8 && alltrue([for arn in var.secret_custodians :
      length(arn) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/([A-Za-z0-9+=,.@_-]+/)*[A-Za-z0-9+=,.@_-]{1,64}$", arn)) &&
      !contains(flatten([for workload in ["api", "web", "worker", "migration", "access", "relay"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${workload}-${kind}"]]), lower(element(split("/", arn), length(split("/", arn)) - 1)))
    ])
    error_message = "Supply one to eight exact same-account trusted custodian roles, excluding all twelve workload role names."
  }
}
variable "configuration_version_id" {
  type        = string
  description = "Choose the exact future whole-JSON version UUID before protected population. No stage, selector or payload."
  validation {
    condition     = can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.configuration_version_id))
    error_message = "Supply one canonical lowercase UUID version, never AWSCURRENT, a selector or a secret value."
  }
}
variable "image_repository" {
  type        = object({ arn = string, url = string, pull_execution_roles = list(string) })
  default     = null
  description = "Null explicitly denies all AWS actions to BOTH identities. Bind only actual reviewed artifact-stores deployment output after its optional exact relay reader is set."
  validation {
    condition = var.image_repository == null || try(
      var.image_repository.arn == "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/deployment" &&
      var.image_repository.url == "${var.account_id}.dkr.ecr.${var.region}.amazonaws.com/saveit/${var.cell}/${var.environment}/deployment" &&
      length(var.image_repository.pull_execution_roles) == 3 &&
    toset(var.image_repository.pull_execution_roles) == toset([for workload in ["migration", "access", "relay"] : "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-${workload}-execution"]), false)
    error_message = "Bind exact same-cell deployment ARN/URL and exactly the migration/access/relay execution reader references, or retain null bootstrap."
  }
}
variable "log_retention_days" {
  type    = number
  default = 90
  validation {
    condition     = contains([30, 60, 90, 120, 150, 180, 365], var.log_retention_days)
    error_message = "Choose supported finite operational retention between thirty and365 days."
  }
}
