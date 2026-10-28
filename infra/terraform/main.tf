terraform {
  required_version = ">= 1.9.0, < 2.0.0"
}

# Foundation scaffold only. No provider or cloud resource is provisioned.
# Select region, tenancy cells, account boundaries and remote state in ADR 004
# before adding AWS modules and committing the generated .terraform.lock.hcl.
locals {
  project = "saveit"
  tags = {
    Project     = local.project
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

variable "environment" {
  type        = string
  description = "Deployment environment; this scaffold does not deploy resources."
  default     = "development"
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "Use development, staging, or production."
  }
}

output "planned_tags" {
  value       = local.tags
  description = "Naming convention preview, not deployed infrastructure."
}
