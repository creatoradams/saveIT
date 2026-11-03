locals {
  pull_actions = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
  log_actions  = ["logs:CreateLogStream", "logs:PutLogEvents"]
  ring_actions = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"]
  ring_grants = [{
    Sid       = "ExactProtectedRing", Effect = "Allow", Action = local.ring_actions, Resource = local.filesystem,
    Condition = { Bool = { "aws:SecureTransport" = "true", "elasticfilesystem:AccessedViaMountTarget" = "true" }, StringEquals = { "elasticfilesystem:AccessPointArn" = local.access_point } }
  }]
  task_boundaries = { for name in local.workloads : name => name == "api" && var.session_key_storage != null ? jsonencode({
    Version = "2012-10-17"
    Statement = concat(local.ring_grants, [
      { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.ring_actions, Resource = "*" },
      { Sid = "RejectOtherFilesystems", Effect = "Deny", Action = local.ring_actions, NotResource = local.filesystem },
      { Sid = "RejectOtherAccessPoints", Effect = "Deny", Action = local.ring_actions, Resource = "*", Condition = { StringNotEqualsIfExists = { "elasticfilesystem:AccessPointArn" = local.access_point } } },
      { Sid = "RejectInsecureMounts", Effect = "Deny", Action = local.ring_actions, Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
      { Sid = "RejectOutsideMountTargets", Effect = "Deny", Action = local.ring_actions, Resource = "*", Condition = { BoolIfExists = { "elasticfilesystem:AccessedViaMountTarget" = "false" } } }
    ])
  }) : jsonencode({ Version = "2012-10-17", Statement = [{ Sid = "NoAWSWorkloadAuthority", Effect = "Deny", Action = "*", Resource = "*" }] }) }

  execution_actions = { for name in local.workloads : name => concat(["ecr:GetAuthorizationToken"], local.pull_actions, local.log_actions,
  contains(local.configured, name) ? ["secretsmanager:GetSecretValue", "kms:Decrypt"] : []) }
  execution_grants = { for name in local.workloads : name => jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      { Sid = "RegionalRegistryAuthorization", Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*", Condition = { StringEquals = { "aws:RequestedRegion" = var.region } } },
      { Sid = "ExactImagePull", Effect = "Allow", Action = local.pull_actions, Resource = local.repositories[name] },
      { Sid = "OwnLogStreamsOnly", Effect = "Allow", Action = local.log_actions, Resource = "${local.log_groups[name]}:log-stream:*" }
      ], [for statement in [
        { Sid = "PinnedConfigurationVersion", Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = try(aws_secretsmanager_secret.configuration[name].arn, ""), Condition = { StringEquals = { "secretsmanager:VersionId" = lookup(var.configuration_versions, name, "") } } },
        { Sid = "ConfigurationDecryptionOnly", Effect = "Allow", Action = "kms:Decrypt", Resource = try(aws_kms_key.configuration[name].arn, ""),
        Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com", "kms:EncryptionContext:SecretARN" = try(aws_secretsmanager_secret.configuration[name].arn, ""), "kms:EncryptionContext:SecretVersionId" = lookup(var.configuration_versions, name, "") } } }
    ] : statement if contains(local.configured, name)])
  }) }
  execution_boundaries = { for name in local.workloads : name => jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      # A boundary caps permissions; this Allow is not attached as a grant.
      # Explicit action/resource/context denies also constrain session grants.
      { Sid = "MaximumExecutionActions", Effect = "Allow", Action = local.execution_actions[name], Resource = "*" },
      { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = local.execution_actions[name], Resource = "*" },
      { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = var.region } } },
      { Sid = "RejectOtherRepositories", Effect = "Deny", Action = local.pull_actions, NotResource = local.repositories[name] },
      { Sid = "RejectOtherLogStreams", Effect = "Deny", Action = local.log_actions, NotResource = "${local.log_groups[name]}:log-stream:*" }
      ], [for statement in [
        { Sid = "RejectOtherSecrets", Effect = "Deny", Action = "secretsmanager:GetSecretValue", NotResource = try(aws_secretsmanager_secret.configuration[name].arn, "") },
        { Sid = "RejectOtherSecretVersions", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { StringNotEqualsIfExists = { "secretsmanager:VersionId" = lookup(var.configuration_versions, name, "") } } },
        { Sid = "RejectOtherKeys", Effect = "Deny", Action = "kms:Decrypt", NotResource = try(aws_kms_key.configuration[name].arn, "") },
        { Sid = "RejectDirectDecryption", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com" } } },
        { Sid = "RejectOtherSecretContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretARN" = try(aws_secretsmanager_secret.configuration[name].arn, "") } } },
        { Sid = "RejectOtherVersionContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = lookup(var.configuration_versions, name, "") } } }
    ] : statement if contains(local.configured, name)])
  }) }
}

resource "aws_iam_policy" "task_boundary" {
  for_each   = local.workloads
  name       = "${local.name}-${each.key}-task-boundary"
  policy     = local.task_boundaries[each.key]
  depends_on = [data.aws_efs_file_system.ring, data.aws_efs_access_point.ring]
  lifecycle {
    precondition {
      condition     = length(local.task_boundaries[each.key]) <= 6144
      error_message = "Task permissions boundary exceeds IAM's policy limit."
    }
  }
}
resource "aws_iam_policy" "execution_boundary" {
  for_each = local.workloads
  name     = "${local.name}-${each.key}-execution-boundary"
  policy   = local.execution_boundaries[each.key]
  lifecycle {
    precondition {
      condition     = length(local.execution_boundaries[each.key]) <= 6144
      error_message = "Execution boundary exceeds IAM's policy limit; do not widen or truncate scope."
    }
  }
}
