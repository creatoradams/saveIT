variable "account_id" {
  type        = string
  description = "Explicit authorized AWS account. Never inferred from ambient credentials."
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Provide an explicit twelve-digit AWS account ID."
  }
}

variable "region" {
  type        = string
  description = "Reviewed commercial AWS region; no residency decision is implied by examples."
  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Provide a commercial AWS region; other partitions require a separate design."
  }
}

variable "key_administrators" {
  type        = set(string)
  description = "Same-account IAM roles authorized to administer state keys/bucket; include the bootstrap execution role."
  validation {
    condition     = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators : can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn))])
    error_message = "Provide one to eight exact same-account IAM role ARNs, without wildcards."
  }
}

variable "state_operators" {
  type        = set(string)
  description = "Same-account IAM roles allowed to read/write named state keys and manage native lock files."
  validation {
    condition     = length(var.state_operators) > 0 && length(var.state_operators) <= 16 && alltrue([for arn in var.state_operators : can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn))])
    error_message = "Provide one to sixteen exact same-account IAM role ARNs, without wildcards."
  }
}
