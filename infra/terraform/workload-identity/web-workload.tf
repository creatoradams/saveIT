# Workload capability only; not customer identity or a credential-exchange pool.
variable "web_workload_pool_enabled" {
  type    = bool
  default = false
}
variable "web_workload_token_endpoint" {
  type    = object({ pool_id = string, id = string, vpc_id = string, application_subnet_ids = list(string), security_group_id = string })
  default = null
  validation {
    condition = var.web_workload_token_endpoint == null ? true : try(var.web_workload_pool_enabled &&
      can(regex("^${var.region}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.web_workload_token_endpoint.pool_id)) &&
      can(regex("^vpce-[0-9a-f]{17}$", var.web_workload_token_endpoint.id)) && can(regex("^vpc-[0-9a-f]{17}$", var.web_workload_token_endpoint.vpc_id)) &&
      length(var.web_workload_token_endpoint.application_subnet_ids) == 2 && length(distinct(var.web_workload_token_endpoint.application_subnet_ids)) == 2 &&
      alltrue([for id in var.web_workload_token_endpoint.application_subnet_ids : can(regex("^subnet-[0-9a-f]{17}$", id))]) &&
    can(regex("^sg-[0-9a-f]{17}$", var.web_workload_token_endpoint.security_group_id)), false)
    error_message = "Enable the isolated pool before binding its exact same-cell private endpoint contract."
  }
}
locals {
  web_token_bound         = var.web_workload_pool_enabled && var.web_workload_token_endpoint != null
  web_bound_pool_arn      = var.web_workload_token_endpoint == null ? "" : "arn:aws:cognito-identity:${var.region}:${var.account_id}:identitypool/${var.web_workload_token_endpoint.pool_id}"
  web_pool_arn            = var.web_workload_pool_enabled ? aws_cognito_identity_pool.web[0].arn : ""
  web_provider            = "saveit.${var.cell}.${var.environment}.web"
  global_identity_regions = toset(["us-east-1", "us-east-2", "us-west-1", "us-west-2", "ap-northeast-1", "ap-northeast-2", "ap-northeast-3", "ap-south-1", "ap-southeast-1", "ap-southeast-2", "ca-central-1", "eu-central-1", "eu-north-1", "eu-west-1", "eu-west-2", "eu-west-3", "sa-east-1"])
  web_issuer              = contains(local.global_identity_regions, var.region) ? "https://cognito-identity.amazonaws.com" : "https://cognito-identity.${var.region}.amazonaws.com"
  web_token_action        = "cognito-identity:GetOpenIdTokenForDeveloperIdentity"
  web_token_statements = [
    { Sid = "OwnPoolThroughBoundEndpoint", Effect = "Allow", Action = local.web_token_action, Resource = local.web_pool_arn,
    Condition = { StringEquals = { "aws:SourceVpce" = try(var.web_workload_token_endpoint.id, ""), "aws:RequestedRegion" = var.region }, Bool = { "aws:SecureTransport" = "true" } } },
    { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.web_token_action, Resource = "*" },
    { Sid = "RejectOtherPools", Effect = "Deny", Action = local.web_token_action, NotResource = local.web_pool_arn },
    { Sid = "RejectOtherEndpoints", Effect = "Deny", Action = local.web_token_action, Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:SourceVpce" = try(var.web_workload_token_endpoint.id, "") } } },
    { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = var.region } } },
    { Sid = "RejectInsecureTransport", Effect = "Deny", Action = "*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } }
  ]
  web_token_endpoint_policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Sid = "OnlyWebTokenIssuance", Effect = "Allow", Principal = { AWS = local.task_arns.web }, Action = local.web_token_action, Resource = local.web_bound_pool_arn },
    { Sid = "RejectOtherPrincipals", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.task_arns.web } } },
    { Sid = "RejectOtherActions", Effect = "Deny", Principal = "*", NotAction = local.web_token_action, Resource = "*" },
    { Sid = "RejectOtherPools", Effect = "Deny", Principal = "*", Action = "*", NotResource = local.web_bound_pool_arn },
    { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } }
  ] })
}
resource "aws_cognito_identity_pool" "web" {
  count                            = var.web_workload_pool_enabled ? 1 : 0
  identity_pool_name               = "${local.name}-web-workload"
  developer_provider_name          = local.web_provider
  allow_unauthenticated_identities = false
  allow_classic_flow               = false
  openid_connect_provider_arns     = []
  saml_provider_arns               = []
  supported_login_providers        = {}
  tags                             = { Process = "web" }
  lifecycle { prevent_destroy = true }
}
# Bootstrap pool/roles -> endpoint -> bound role. No secret or token lookup.
data "aws_vpc_endpoint" "web_token" {
  count = local.web_token_bound ? 1 : 0
  id    = var.web_workload_token_endpoint.id
  lifecycle {
    postcondition {
      condition = try(self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:vpc-endpoint/${var.web_workload_token_endpoint.id}" && self.owner_id == var.account_id &&
        self.vpc_id == var.web_workload_token_endpoint.vpc_id && self.service_name == "com.amazonaws.${var.region}.cognito-identity" && self.vpc_endpoint_type == "Interface" && self.state == "available" &&
        self.private_dns_enabled && self.ip_address_type == "ipv4" && toset(self.subnet_ids) == toset(var.web_workload_token_endpoint.application_subnet_ids) && toset(self.security_group_ids) == toset([var.web_workload_token_endpoint.security_group_id]) &&
        self.tags.Project == "SaveIT" && self.tags.Cell == var.cell && self.tags.Environment == var.environment && self.tags.Process == "web-token" &&
      jsondecode(self.policy) == jsondecode(local.web_token_endpoint_policy), false)
      error_message = "Require exact own private regional Cognito endpoint/network/policy metadata before token authority. Mocks are not IAM evidence."
    }
  }
}
resource "aws_iam_role_policy" "web_token" {
  count      = local.web_token_bound ? 1 : 0
  name       = "OwnWorkloadTokenOnly"
  role       = aws_iam_role.task["web"].id
  policy     = jsonencode({ Version = "2012-10-17", Statement = local.web_token_statements })
  depends_on = [data.aws_vpc_endpoint.web_token]
  lifecycle {
    precondition {
      condition     = local.web_pool_arn == local.web_bound_pool_arn
      error_message = "The bound endpoint must serve this exact owned workload pool before granting token authority."
    }
  }
}
output "web_workload_pool" {
  value       = var.web_workload_pool_enabled ? { account_id = var.account_id, pool_id = aws_cognito_identity_pool.web[0].id, pool_arn = local.web_pool_arn, provider = local.web_provider, issuer = local.web_issuer } : null
  description = "Dedicated workload-only pool references. No role mapping, user identity, token lookup or managed issuance proof."
}
output "web_workload_admission" {
  value      = local.web_token_bound ? { account_id = var.account_id, pool_id = aws_cognito_identity_pool.web[0].id, pool_arn = local.web_pool_arn, provider = local.web_provider, issuer = local.web_issuer, endpoint = var.web_workload_token_endpoint, binding_ready = true } : null
  depends_on = [data.aws_vpc_endpoint.web_token, aws_iam_role_policy.web_token]
}
