locals {
  task_grants = [
    { Sid = "PinnedWholeConfiguration", Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.configuration.arn, Condition = { StringEquals = { "secretsmanager:VersionId" = var.configuration_version_id, "aws:RequestedRegion" = var.region }, Bool = { "aws:SecureTransport" = "true" } } },
    { Sid = "ConfigurationDecryptOnly", Effect = "Allow", Action = "kms:Decrypt", Resource = aws_kms_key.configuration.arn, Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id, "kms:EncryptionContext:SecretARN" = aws_secretsmanager_secret.configuration.arn, "kms:EncryptionContext:SecretVersionId" = var.configuration_version_id } } }
  ]
  execution_grants = [
    { Sid = "RegionalRegistryAuthorization", Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*", Condition = { StringEquals = { "aws:RequestedRegion" = var.region } } },
    { Sid = "ExactDeploymentImagePull", Effect = "Allow", Action = local.pull, Resource = local.repository },
    { Sid = "OwnLogStreamsOnly", Effect = "Allow", Action = local.log_actions, Resource = "${local.log_arn}:log-stream:*" },
    { Sid = "OwnEncryptedLogData", Effect = "Allow", Action = local.crypto, Resource = aws_kms_key.logs.arn, Condition = { StringEquals = { "kms:ViaService" = "logs.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id }, ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = local.log_arn } } },
    { Sid = "OwnLogKeyDescription", Effect = "Allow", Action = "kms:DescribeKey", Resource = aws_kms_key.logs.arn, Condition = { StringEquals = { "kms:ViaService" = "logs.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } } }
  ]
  bootstrap = jsonencode({ Version = "2012-10-17", Statement = [{ Sid = "UnboundNoAWSAuthority", Effect = "Deny", Action = "*", Resource = "*" }] })
  task_boundary = local.bound ? jsonencode({ Version = "2012-10-17", Statement = concat(local.task_grants, [
    { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.task_actions, Resource = "*" },
    { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = var.region } } },
    { Sid = "RejectOtherSecrets", Effect = "Deny", Action = "secretsmanager:GetSecretValue", NotResource = aws_secretsmanager_secret.configuration.arn },
    { Sid = "RejectInsecureConfigurationReads", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
    { Sid = "RejectOtherSecretVersions", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { StringNotEqualsIfExists = { "secretsmanager:VersionId" = var.configuration_version_id } } },
    { Sid = "RejectOtherDecryptKeys", Effect = "Deny", Action = "kms:Decrypt", NotResource = aws_kms_key.configuration.arn },
    { Sid = "RejectDirectConfigurationDecrypt", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com" } } },
    { Sid = "RejectOtherCallerAccounts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
    { Sid = "RejectOtherSecretContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretARN" = aws_secretsmanager_secret.configuration.arn } } },
    { Sid = "RejectOtherVersionContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = var.configuration_version_id } } }
  ]) }) : local.bootstrap
  execution_boundary = local.bound ? jsonencode({ Version = "2012-10-17", Statement = concat(local.execution_grants, [
    { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.exec_actions, Resource = "*" },
    { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = var.region } } },
    { Sid = "RejectOtherRepositories", Effect = "Deny", Action = local.pull, NotResource = local.repository },
    { Sid = "RejectOtherLogStreams", Effect = "Deny", Action = local.log_actions, NotResource = "${local.log_arn}:log-stream:*" },
    { Sid = "RejectOtherCryptoKeys", Effect = "Deny", Action = concat(local.crypto, ["kms:DescribeKey"]), NotResource = aws_kms_key.logs.arn },
    { Sid = "RejectDirectLogKeyUse", Effect = "Deny", Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "logs.${var.region}.amazonaws.com" } } },
    { Sid = "RejectOtherCallerAccounts", Effect = "Deny", Action = concat(local.crypto, ["kms:DescribeKey"]), Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
    { Sid = "RejectOtherLogContexts", Effect = "Deny", Action = local.crypto, Resource = "*", Condition = { ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = local.log_arn } } }
  ]) }) : local.bootstrap
}

resource "aws_iam_policy" "task_boundary" {
  name   = "${local.name}-task-boundary"
  policy = local.task_boundary
  lifecycle {
    precondition {
      condition     = length(local.task_boundary) <= 6144
      error_message = "Relay task boundary exceeds IAM's limit; never truncate or widen it."
    }
    precondition {
      condition     = aws_kms_key.configuration.arn != aws_kms_key.logs.arn
      error_message = "Relay configuration/log keys must stay distinct."
    }
  }
}
resource "aws_iam_policy" "execution_boundary" {
  name   = "${local.name}-execution-boundary"
  policy = local.execution_boundary
  lifecycle {
    precondition {
      condition     = length(local.execution_boundary) <= 6144
      error_message = "Relay execution boundary exceeds IAM's limit; never truncate or widen it."
    }
    precondition {
      condition     = aws_kms_key.configuration.arn != aws_kms_key.logs.arn
      error_message = "Relay execution must never use the configuration key."
    }
  }
}
resource "aws_iam_role_policy" "task" {
  count  = local.bound ? 1 : 0
  name   = "PinnedRelayConfigurationOnly"
  role   = aws_iam_role.task.id
  policy = jsonencode({ Version = "2012-10-17", Statement = local.task_grants })
}
resource "aws_iam_role_policy" "execution" {
  count  = local.bound ? 1 : 0
  name   = "ReviewedRelayExecutionOnly"
  role   = aws_iam_role.execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = local.execution_grants })
}
