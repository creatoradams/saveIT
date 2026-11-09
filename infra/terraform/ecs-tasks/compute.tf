variable "compute_generation" {
  type    = string
  default = null
  validation {
    condition     = var.compute_generation == null ? true : can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.compute_generation)) && var.compute_generation != "00000000-0000-0000-0000-000000000000"
    error_message = "Require a canonical nonzero protected release generation, never a request-derived value."
  }
}
locals {
  compute_environment = var.compute_generation == null ? {} : {
    Deployment__ComputeGuard      = "true"
    Deployment__ComputeGeneration = var.compute_generation
  }
}
output "compute_coordination" {
  value       = var.compute_generation == null ? null : { generation = var.compute_generation, required_processes = ["api", "worker"] }
  description = "Declared immutable process generation only. Null is bootstrap compatibility; no launch, database state, quiescence or fencing evidence."
}
