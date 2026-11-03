variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply a twelve-digit authorized AWS account ID."
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
variable "network" {
  description = "Explicit same-cell foundation outputs. Provider lookups verify VPC, private subnet and distinct-AZ placement before creation."
  type = object({
    vpc_id                 = string
    api_security_group_id  = string
    application_subnet_ids = set(string)
  })
  validation {
    condition = can(regex("^vpc-[0-9a-f]{17}$", var.network.vpc_id)) && can(regex("^sg-[0-9a-f]{17}$", var.network.api_security_group_id)) && length(var.network.application_subnet_ids) == 2 && alltrue([
      for id in var.network.application_subnet_ids : can(regex("^subnet-[0-9a-f]{17}$", id))
    ])
    error_message = "Supply one exact VPC/API security group and two distinct application subnet IDs."
  }
}
variable "api_task_role" {
  type        = string
  description = "Exact same-account API task role, not its execution role; IAM authorization must be enabled in the future task definition."
  validation {
    condition     = can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", var.api_task_role))
    error_message = "Supply the exact same-account API task IAM role ARN."
  }
}
variable "key_administrators" {
  type        = set(string)
  description = "Exact same-account key administrators including the infrastructure execution role. These are privileged key-policy authorities."
  validation {
    condition     = length(var.key_administrators) > 0 && length(var.key_administrators) <= 8 && alltrue([for arn in var.key_administrators : can(regex("^arn:aws:iam::${var.account_id}:role/[A-Za-z0-9+=,.@_/-]+$", arn))]) && !contains(var.key_administrators, var.api_task_role)
    error_message = "Supply one to eight exact same-account administrator roles, distinct from the API task role."
  }
}
