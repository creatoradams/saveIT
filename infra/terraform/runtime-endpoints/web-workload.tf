variable "web_workload_pool" {
  type    = object({ account_id = string, pool_id = string, pool_arn = string, provider = string, issuer = string })
  default = null
  validation {
    condition = var.web_workload_pool == null ? true : try(var.web_workload_pool.account_id == var.account_id &&
      can(regex("^${var.region}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.web_workload_pool.pool_id)) &&
      var.web_workload_pool.pool_arn == "arn:aws:cognito-identity:${var.region}:${var.account_id}:identitypool/${var.web_workload_pool.pool_id}" &&
      var.web_workload_pool.provider == "saveit.${var.cell}.${var.environment}.web" &&
    contains(["https://cognito-identity.amazonaws.com", "https://cognito-identity.${var.region}.amazonaws.com"], var.web_workload_pool.issuer), false)
    error_message = "Require the exact own dedicated identity pool producer output; never user identity or a public provider."
  }
}
locals {
  web_token_action = "cognito-identity:GetOpenIdTokenForDeveloperIdentity"
  web_token_role   = "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-web-task"
  web_token_policy = var.web_workload_pool == null ? "" : jsonencode({ Version = "2012-10-17", Statement = [
    { Sid = "OnlyWebTokenIssuance", Effect = "Allow", Principal = { AWS = local.web_token_role }, Action = local.web_token_action, Resource = var.web_workload_pool.pool_arn },
    { Sid = "RejectOtherPrincipals", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.web_token_role } } },
    { Sid = "RejectOtherActions", Effect = "Deny", Principal = "*", NotAction = local.web_token_action, Resource = "*" },
    { Sid = "RejectOtherPools", Effect = "Deny", Principal = "*", Action = "*", NotResource = var.web_workload_pool.pool_arn },
    { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } }
  ] })
}
data "aws_cognito_identity_pool" "web" {
  count              = var.web_workload_pool == null ? 0 : 1
  identity_pool_name = "${local.name}-web-workload"
  lifecycle {
    postcondition {
      condition = try(self.id == var.web_workload_pool.pool_id && self.arn == var.web_workload_pool.pool_arn && self.identity_pool_name == "${local.name}-web-workload" && self.developer_provider_name == var.web_workload_pool.provider &&
        !self.allow_unauthenticated_identities && !self.allow_classic_flow && length(self.cognito_identity_providers) == 0 && length(self.openid_connect_provider_arns) == 0 && length(self.saml_provider_arns) == 0 && length(self.supported_login_providers) == 0 &&
      self.tags.Project == "SaveIT" && self.tags.Cell == var.cell && self.tags.Environment == var.environment && self.tags.Process == "web", false)
      error_message = "Require the isolated own developer-only pool metadata. Role mappings and external trust still need managed review."
    }
  }
}
resource "aws_security_group" "web_token" {
  count       = var.web_workload_pool == null ? 0 : 1
  name_prefix = "${local.name}-web-token-endpoint-"
  vpc_id      = var.network.vpc_id
  tags        = { Process = "web-token" }
}
resource "aws_vpc_security_group_ingress_rule" "web_token" {
  count                        = var.web_workload_pool == null ? 0 : 1
  security_group_id            = aws_security_group.web_token[0].id
  referenced_security_group_id = aws_security_group.web.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_vpc_security_group_egress_rule" "web_token" {
  count                        = var.web_workload_pool == null ? 0 : 1
  security_group_id            = aws_security_group.web.id
  referenced_security_group_id = aws_security_group.web_token[0].id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_vpc_endpoint" "web_token" {
  count               = var.web_workload_pool == null ? 0 : 1
  vpc_id              = var.network.vpc_id
  service_name        = "com.amazonaws.${var.region}.cognito-identity"
  vpc_endpoint_type   = "Interface"
  private_dns_enabled = true
  ip_address_type     = "ipv4"
  subnet_ids          = var.network.application_subnet_ids
  security_group_ids  = [aws_security_group.web_token[0].id]
  policy              = local.web_token_policy
  tags                = { Process = "web-token" }
  dns_options { dns_record_ip_type = "ipv4" }
  depends_on = [data.aws_cognito_identity_pool.web, aws_vpc_endpoint.interface]
  lifecycle { prevent_destroy = true }
}
output "web_workload_token_endpoint" {
  value       = var.web_workload_pool == null ? null : { pool_id = var.web_workload_pool.pool_id, id = aws_vpc_endpoint.web_token[0].id, vpc_id = var.network.vpc_id, application_subnet_ids = var.network.application_subnet_ids, security_group_id = aws_security_group.web_token[0].id }
  description = "Bootstrap output to rebind the web task role; web alone has a declared443 path. No token/secret value or IAM evidence."
}
