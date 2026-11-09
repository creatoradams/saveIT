variable "workload_admission" {
  default = null
  type = object({
    account_id = string, pool_id = string, pool_arn = string, provider = string, issuer = string, binding_ready = bool, jwks_sha256 = string
    endpoint   = object({ pool_id = string, id = string, vpc_id = string, application_subnet_ids = list(string), security_group_id = string })
  })
  validation {
    condition = var.workload_admission == null ? true : try(var.workload_admission.binding_ready && var.relay != null &&
      var.workload_admission.account_id == var.account_id && can(regex("^${var.region}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.workload_admission.pool_id)) &&
      var.workload_admission.pool_arn == "arn:aws:cognito-identity:${var.region}:${var.account_id}:identitypool/${var.workload_admission.pool_id}" &&
      var.workload_admission.provider == "saveit.${var.cell}.${var.environment}.web" && contains(["https://cognito-identity.amazonaws.com", "https://cognito-identity.${var.region}.amazonaws.com"], var.workload_admission.issuer) &&
      can(regex("^[0-9a-f]{64}$", var.workload_admission.jwks_sha256)) && var.workload_admission.endpoint.pool_id == var.workload_admission.pool_id && can(regex("^vpce-[0-9a-f]{17}$", var.workload_admission.endpoint.id)) &&
      var.workload_admission.endpoint.vpc_id == var.relay.network.vpc_id && toset(var.workload_admission.endpoint.application_subnet_ids) == toset(var.relay.network.application_subnet_ids) &&
      length(var.workload_admission.endpoint.application_subnet_ids) == 2 && length(distinct(var.workload_admission.endpoint.application_subnet_ids)) == 2 &&
    can(regex("^sg-[0-9a-f]{17}$", var.workload_admission.endpoint.security_group_id)) && var.workload_admission.endpoint.security_group_id != var.relay.network.security_group_id, false)
    error_message = "Require the bound own pool/endpoint producer contract plus reviewed public JWKS hash and an isolated relay."
  }
}
locals {
  web_workload_environment = var.workload_admission == null ? {} : {
    SAVEIT_WORKLOAD_ACCOUNT_ID = var.account_id, SAVEIT_WORKLOAD_POOL_ID = var.workload_admission.pool_id, SAVEIT_WORKLOAD_PROVIDER = var.workload_admission.provider
  }
  relay_workload_environment = var.workload_admission == null ? {} : {
    SAVEIT_WORKLOAD_POOL_ID = var.workload_admission.pool_id, SAVEIT_WORKLOAD_PROVIDER = var.workload_admission.provider,
    SAVEIT_WORKLOAD_ISSUER  = var.workload_admission.issuer, SAVEIT_WORKLOAD_JWKS_SHA256 = var.workload_admission.jwks_sha256
  }
}
output "workload_admission" {
  value       = var.workload_admission
  description = "Public workload-admission contract, null for legacy bootstrap. No token, private key, actual issuer or managed IAM evidence."
}
