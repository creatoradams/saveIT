locals {
  crypto_actions  = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"]
  control_actions = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"]
  key_control = [
    { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root }, Action = "kms:*", Resource = "*" },
    { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) }, Action = local.control_actions, Resource = "*" },
    { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = concat(local.crypto_actions, ["kms:Describe*", "kms:Get*", "kms:List*"]), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } }
  ]
}
resource "aws_kms_key" "registry" {
  description             = "${local.name} private image stores"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(local.key_control, [
      { Sid = "RegionalECROnly", Effect = "Deny", Principal = "*", Action = local.crypto_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:ViaService" = "ecr.${var.region}.amazonaws.com" } } },
      { Sid = "CallerAccountOnly", Effect = "Deny", Principal = "*", Action = local.crypto_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
      { Sid = "ExactRepositoryContexts", Effect = "Deny", Principal = "*", Action = local.crypto_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:EncryptionContext:aws:ecr:arn" = values(local.repository_arns) } } },
      { Sid = "AWSResourceGrantsOnly", Effect = "Deny", Principal = "*", Action = "kms:CreateGrant", Resource = "*", Condition = { BoolIfExists = { "kms:GrantIsForAWSResource" = "false" } } },
      { Sid = "RegionalRepositoryData", Effect = "Allow", Principal = { AWS = "*" }, Action = concat(local.crypto_actions, ["kms:DescribeKey"]), Resource = "*", Condition = { StringEquals = { "kms:ViaService" = "ecr.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id, "kms:EncryptionContext:aws:ecr:arn" = values(local.repository_arns) } } }
    ])
  })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "registry" {
  name          = "alias/${local.name}-registry"
  target_key_id = aws_kms_key.registry.key_id
}
resource "aws_kms_key" "logs" {
  for_each                = local.workloads
  description             = "${local.name} ${each.key} operational logs"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(local.key_control, [
      # The two not-equals predicates deliberately share this condition: deny
      # only if NEITHER direct regional Logs NOR regional Logs forwarding holds.
      { Sid = "RegionalLogsOrForwardingOnly", Effect = "Deny", Principal = "*", Action = local.crypto_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "aws:PrincipalServiceName" = "logs.${var.region}.amazonaws.com", "kms:ViaService" = "logs.${var.region}.amazonaws.com" } } },
      { Sid = "ExactLogContext", Effect = "Deny", Principal = "*", Action = local.crypto_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = local.log_arns[each.key] } } },
      { Sid = "RegionalLogData", Effect = "Allow", Principal = { Service = "logs.${var.region}.amazonaws.com" }, Action = concat(local.crypto_actions, ["kms:Describe*"]), Resource = "*", Condition = { ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = local.log_arns[each.key] } } }
    ])
  })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "logs" {
  for_each      = local.workloads
  name          = "alias/${local.name}-${each.key}-logs"
  target_key_id = aws_kms_key.logs[each.key].key_id
}
