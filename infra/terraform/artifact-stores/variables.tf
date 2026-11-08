variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply the explicit twelve-digit authorized AWS account ID."
  }
}
variable "region" {
  type = string
  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Supply a reviewed commercial AWS region."
  }
}
variable "cell" {
  type = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.cell))
    error_message = "Use two to twelve lowercase alphanumeric characters, beginning with a letter."
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
  type        = set(string)
  description = "One to eight existing same-account infrastructure/key administrators; trusted policy controllers, never workload roles."
  validation {
    condition = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators :
      length(arn) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/([A-Za-z0-9+=,.@_-]+/)*[A-Za-z0-9+=,.@_-]{1,64}$", arn)) &&
      !contains(flatten([for name in ["api", "web", "worker", "migration", "access", "relay"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${name}-${kind}"]]), lower(element(split("/", arn), length(split("/", arn)) - 1)))
    ])
    error_message = "Supply exact bounded same-account administrator roles distinct from all workload role names."
  }
}
variable "publisher_role" {
  type        = string
  description = "One existing protected publisher role, distinct from administrators and all workload roles; its trust/promotion/identity permissions remain separate."
  validation {
    condition = (length(var.publisher_role) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/([A-Za-z0-9+=,.@_-]+/)*[A-Za-z0-9+=,.@_-]{1,64}$", var.publisher_role)) &&
      !contains(var.key_administrators, var.publisher_role) &&
    !contains(flatten([for name in ["api", "web", "worker", "migration", "access", "relay"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${name}-${kind}"]]), lower(element(split("/", var.publisher_role), length(split("/", var.publisher_role)) - 1))))
    error_message = "Supply an exact bounded same-account publisher role distinct from administrators and all workload role names."
  }
}
variable "log_retention_days" {
  type        = number
  default     = 90
  description = "Finite operational-log retention, not an immutable audit/archive or approved legal retention policy."
  validation {
    condition     = contains([30, 60, 90, 120, 150, 180, 365], var.log_retention_days)
    error_message = "Choose a supported finite operational retention between 30 and 365 days."
  }
}

variable "relay_execution_role_arn" {
  type        = string
  default     = null
  description = "Optional exact separate relay execution role; null preserves every existing reader. Changes only deployment pull admission, never publisher/control/delete privileges."
  validation {
    condition     = var.relay_execution_role_arn == null || var.relay_execution_role_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-relay-execution"
    error_message = "Bind only the exact same-account/cell/environment relay execution role, or retain null bootstrap."
  }
}
