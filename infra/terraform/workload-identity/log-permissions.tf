locals {
  log_crypto     = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"]
  bound_log_keys = var.operational_log_keys == null ? {} : var.operational_log_keys
  log_bound_execution_boundaries = { for name in local.workloads : name => jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      { Sid = "MaximumExecutionActions", Effect = "Allow", Action = local.execution_actions[name], Resource = "*" },
      { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.execution_actions[name], Resource = "*" },
      { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = var.region } } },
      { Sid = "RejectOtherRepositories", Effect = "Deny", Action = local.pull_actions, NotResource = local.repositories[name] },
      { Sid = "RejectOtherLogStreams", Effect = "Deny", Action = local.log_actions, NotResource = "${local.log_groups[name]}:log-stream:*" },
      { Sid = "RejectOtherCryptoKeys", Effect = "Deny", Action = local.log_crypto, NotResource = concat([lookup(local.bound_log_keys, name, "")], contains(local.configured, name) ? [try(aws_kms_key.configuration[name].arn, "")] : []) },
      { Sid = "RejectOtherDescribedKeys", Effect = "Deny", Action = "kms:DescribeKey", NotResource = lookup(local.bound_log_keys, name, "") },
      { Sid = "RejectDirectLogKeyUse", Effect = "Deny", Action = concat(local.log_crypto, ["kms:DescribeKey"]), Resource = lookup(local.bound_log_keys, name, ""), Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "logs.${var.region}.amazonaws.com" } } },
      { Sid = "RejectOtherLogContexts", Effect = "Deny", Action = local.log_crypto, Resource = lookup(local.bound_log_keys, name, ""), Condition = { ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = local.log_groups[name] } } }
      ], [for statement in [
        { Sid = "RejectOtherSecrets", Effect = "Deny", Action = "secretsmanager:GetSecretValue", NotResource = try(aws_secretsmanager_secret.configuration[name].arn, "") },
        { Sid = "RejectOtherSecretVersions", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { StringNotEqualsIfExists = { "secretsmanager:VersionId" = lookup(var.configuration_versions, name, "") } } },
        { Sid = "ConfigurationDecryptOnly", Effect = "Deny", Action = ["kms:Encrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = try(aws_kms_key.configuration[name].arn, "") },
        { Sid = "RejectDirectConfigurationDecryption", Effect = "Deny", Action = "kms:Decrypt", Resource = try(aws_kms_key.configuration[name].arn, ""), Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com" } } },
        { Sid = "RejectOtherConfigurationSecretContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = try(aws_kms_key.configuration[name].arn, ""), Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretARN" = try(aws_secretsmanager_secret.configuration[name].arn, "") } } },
        { Sid = "RejectOtherConfigurationVersionContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = try(aws_kms_key.configuration[name].arn, ""), Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = lookup(var.configuration_versions, name, "") } } }
    ] : statement if contains(local.configured, name)])
  }) }
}
