locals {
  name          = "saveit-${var.cell}-${var.environment}-relay"
  root          = "arn:aws:iam::${var.account_id}:root"
  task_arn      = "arn:aws:iam::${var.account_id}:role/${local.name}-task"
  execution_arn = "arn:aws:iam::${var.account_id}:role/${local.name}-execution"
  admins        = concat(sort(tolist(var.key_administrators)), [local.root])
  custodians    = concat(sort(tolist(var.secret_custodians)), [local.root])
  secret_name   = "saveit/${var.cell}/${var.environment}/relay/config"
  secret_slot   = "arn:aws:secretsmanager:${var.region}:${var.account_id}:secret:${local.secret_name}-??????"
  log_name      = "/saveit/${var.cell}/${var.environment}/relay"
  log_arn       = "arn:aws:logs:${var.region}:${var.account_id}:log-group:${local.log_name}"
  repository    = "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/deployment"
  bound         = var.image_repository != null
  crypto        = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"]
  pull          = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
  log_actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
  task_actions  = ["secretsmanager:GetSecretValue", "kms:Decrypt"]
  exec_actions  = concat(["ecr:GetAuthorizationToken"], local.pull, local.log_actions, local.crypto, ["kms:DescribeKey"])
  trust = jsonencode({ Version = "2012-10-17", Statement = [{
    Sid       = "OnlyAccountRegionalECSTasks", Effect = "Allow", Action = "sts:AssumeRole", Principal = { Service = "ecs-tasks.amazonaws.com" },
    Condition = { StringEquals = { "aws:SourceAccount" = var.account_id }, ArnLike = { "aws:SourceArn" = "arn:aws:ecs:${var.region}:${var.account_id}:*" } }
  }] })
  control = [
    { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root }, Action = "kms:*", Resource = "*" },
    { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) }, Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
    { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = concat(local.crypto, ["kms:Describe*", "kms:Get*", "kms:List*"]), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } }
  ]
}

# Metadata only: no secret version/value resource, data lookup or payload input.
resource "aws_kms_key" "configuration" {
  description             = "${local.name} protected relay configuration"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({ Version = "2012-10-17", Statement = concat(local.control, [
    { Sid = "ConfigurationPrincipalsOnly", Effect = "Deny", Principal = "*", Action = local.crypto, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = sort(distinct(concat(local.admins, local.custodians, [local.task_arn]))) } } },
    { Sid = "SecretsManagerOnly", Effect = "Deny", Principal = "*", Action = local.crypto, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com" } } },
    { Sid = "CallerAccountOnly", Effect = "Deny", Principal = "*", Action = local.crypto, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
    { Sid = "ConfigurationSlotOnly", Effect = "Deny", Principal = "*", Action = local.crypto, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotLikeIfExists = { "kms:EncryptionContext:SecretARN" = local.secret_slot } } },
    { Sid = "TaskVersionOnly", Effect = "Deny", Principal = "*", Action = "kms:Decrypt", Resource = "*", Condition = { ArnEquals = { "aws:PrincipalArn" = local.task_arn }, StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = var.configuration_version_id } } },
    { Sid = "IAMDelegationThroughSecretsManager", Effect = "Allow", Principal = { AWS = local.root }, Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } } }
  ]) })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "configuration" {
  name          = "alias/${local.name}-configuration"
  target_key_id = aws_kms_key.configuration.key_id
}
resource "aws_secretsmanager_secret" "configuration" {
  name                    = local.secret_name
  description             = "Whole pinned relay JSON; protected population and SDK loader remain separate, never Terraform"
  kms_key_id              = aws_kms_key.configuration.arn
  recovery_window_in_days = 30
  lifecycle { prevent_destroy = true }
}
resource "aws_secretsmanager_secret_policy" "configuration" {
  secret_arn          = aws_secretsmanager_secret.configuration.arn
  block_public_policy = true
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "secretsmanager:*", Resource = aws_secretsmanager_secret.configuration.arn, Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
    { Sid = "ExactReadersOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.configuration.arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(local.custodians, [local.task_arn]) } } },
    { Sid = "MetadataAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret", "secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = aws_secretsmanager_secret.configuration.arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } },
    { Sid = "VersionCustodiansOnly", Effect = "Deny", Principal = "*", Action = ["secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = aws_secretsmanager_secret.configuration.arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.custodians } } },
    { Sid = "TaskVersionOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.configuration.arn, Condition = { ArnEquals = { "aws:PrincipalArn" = local.task_arn }, StringNotEqualsIfExists = { "secretsmanager:VersionId" = var.configuration_version_id } } }
  ] })
}

resource "aws_kms_key" "logs" {
  # Pre-apply gate: direct regional Logs customer CallerAccount availability is
  # unverified. Missing context deliberately fails closed; mocks are not evidence.
  description             = "${local.name} finite-retention operational logs"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({ Version = "2012-10-17", Statement = concat(local.control, [
    { Sid = "RegionalLogsOrForwardingOnly", Effect = "Deny", Principal = "*", Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "aws:PrincipalServiceName" = "logs.${var.region}.amazonaws.com", "kms:ViaService" = "logs.${var.region}.amazonaws.com" } } },
    { Sid = "CallerAccountOnly", Effect = "Deny", Principal = "*", Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
    { Sid = "LogPrincipalsOnly", Effect = "Deny", Principal = "*", Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(local.admins, [local.execution_arn]) }, StringNotEqualsIfExists = { "aws:PrincipalServiceName" = "logs.${var.region}.amazonaws.com" } } },
    { Sid = "ExactLogContext", Effect = "Deny", Principal = "*", Action = local.crypto, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = local.log_arn } } },
    { Sid = "RegionalLogData", Effect = "Allow", Principal = { Service = "logs.${var.region}.amazonaws.com" }, Action = local.crypto, Resource = "*", Condition = { ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = local.log_arn }, StringEquals = { "kms:CallerAccount" = var.account_id } } },
    { Sid = "RegionalLogDescription", Effect = "Allow", Principal = { Service = "logs.${var.region}.amazonaws.com" }, Action = "kms:DescribeKey", Resource = "*", Condition = { StringEquals = { "kms:CallerAccount" = var.account_id } } }
  ]) })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "logs" {
  name          = "alias/${local.name}-logs"
  target_key_id = aws_kms_key.logs.key_id
}
resource "aws_cloudwatch_log_group" "relay" {
  name                        = local.log_name
  log_group_class             = "STANDARD"
  retention_in_days           = var.log_retention_days
  deletion_protection_enabled = true
  kms_key_id                  = aws_kms_key.logs.arn
  lifecycle { prevent_destroy = true }
}

resource "aws_iam_role" "task" {
  name                 = "${local.name}-task"
  assume_role_policy   = local.trust
  permissions_boundary = aws_iam_policy.task_boundary.arn
  max_session_duration = 3600
  depends_on           = [aws_secretsmanager_secret_policy.configuration]
}
resource "aws_iam_role" "execution" {
  name                 = "${local.name}-execution"
  assume_role_policy   = local.trust
  permissions_boundary = aws_iam_policy.execution_boundary.arn
  max_session_duration = 3600
}
