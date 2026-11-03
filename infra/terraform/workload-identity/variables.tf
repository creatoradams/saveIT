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
  description = "Existing exact same-account key/metadata administrators, including infrastructure execution. No workload role may administer keys."
  validation {
    condition = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators :
      length(arn) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn)) &&
      !contains(flatten([for name in ["api", "web", "worker", "migration", "access"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${name}-${kind}"]]), element(split("/", arn), length(split("/", arn)) - 1))
    ])
    error_message = "Supply one to eight exact same-account administrator roles distinct from all new workload role names."
  }
}
variable "secret_custodians" {
  type        = set(string)
  description = "Existing exact same-account protected secret-population/rotation roles. Their human/service trust and identity permissions are separately reviewed."
  validation {
    condition = length(var.secret_custodians) > 0 && length(var.secret_custodians) <= 8 && alltrue([for arn in var.secret_custodians :
      length(arn) <= 256 && can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn)) &&
      !contains(flatten([for name in ["api", "web", "worker", "migration", "access"] : [for kind in ["task", "execution"] : "saveit-${var.cell}-${var.environment}-${name}-${kind}"]]), element(split("/", arn), length(split("/", arn)) - 1))
    ])
    error_message = "Supply one to eight exact same-account custodian roles distinct from all new workload role names."
  }
}
variable "configuration_versions" {
  type        = map(string)
  nullable    = false
  description = "Nonsecret pinned UUID version per API/worker/migration/access configuration. Choose before protected population; no payload enters Terraform."
  validation {
    condition     = toset(keys(var.configuration_versions)) == toset(["api", "worker", "migration", "access"]) && alltrue([for id in values(var.configuration_versions) : can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id))])
    error_message = "Supply exactly api, worker, migration and access canonical UUID version IDs; no stages, wildcard or payload values."
  }
}
variable "api_retired_certificates" {
  type        = number
  default     = 0
  description = "Explicit contiguous retired PFX/password pairs in the pinned API JSON configuration, not automatic rotation."
  validation {
    condition     = floor(var.api_retired_certificates) == var.api_retired_certificates && var.api_retired_certificates >= 0 && var.api_retired_certificates <= 4
    error_message = "Choose zero to four integer retired certificate pairs."
  }
}
variable "session_key_storage" {
  type = object({
    file_system_id  = string
    access_point_id = string
  })
  default     = null
  description = "Null is a fail-closed role bootstrap. Bind reviewed key-storage IDs only after API role and EFS exist, before any compute."
  validation {
    condition     = var.session_key_storage == null || (can(regex("^fs-[0-9a-f]{17}$", var.session_key_storage.file_system_id)) && can(regex("^fsap-[0-9a-f]{17}$", var.session_key_storage.access_point_id)))
    error_message = "Supply exact filesystem/access-point IDs or retain the unbound fail-closed bootstrap."
  }
}
variable "operational_log_keys" {
  type        = map(string)
  default     = null
  description = "Optional exact artifact-stores log key ARNs for all five workloads. Null is unbound bootstrap; future encrypted compute must require log_key_binding_ready."
  validation {
    condition = var.operational_log_keys == null || try(
      toset(keys(var.operational_log_keys)) == toset(["api", "web", "worker", "migration", "access"]) &&
      length(distinct(values(var.operational_log_keys))) == 5 &&
    alltrue([for arn in values(var.operational_log_keys) : can(regex("^arn:aws:kms:${var.region}:${var.account_id}:key/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", arn))]), false)
    error_message = "Bind exactly api/web/worker/migration/access to distinct same-account/region symmetric key UUID ARNs, or retain null bootstrap."
  }
}
