locals {
  name            = "saveit-${var.cell}-${var.environment}"
  root            = "arn:aws:iam::${var.account_id}:root"
  workloads       = toset(["api", "web", "worker", "migration", "access"])
  configured      = toset(["api", "worker", "migration", "access"])
  task_arns       = { for name in local.workloads : name => "arn:aws:iam::${var.account_id}:role/${local.name}-${name}-task" }
  exec_arns       = { for name in local.workloads : name => "arn:aws:iam::${var.account_id}:role/${local.name}-${name}-execution" }
  admins          = concat(sort(tolist(var.key_administrators)), [local.root])
  custodians      = concat(sort(tolist(var.secret_custodians)), [local.root])
  secret_names    = { for name in local.configured : name => "saveit/${var.cell}/${var.environment}/${name}/config" }
  secret_patterns = { for name, path in local.secret_names : name => "arn:aws:secretsmanager:${var.region}:${var.account_id}:secret:${path}-??????" }
  repositories    = { for name in local.workloads : name => "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/${contains(["migration", "access"], name) ? "deployment" : name}" }
  log_groups      = { for name in local.workloads : name => "arn:aws:logs:${var.region}:${var.account_id}:log-group:/saveit/${var.cell}/${var.environment}/${name}" }
  ring            = var.session_key_storage == null ? {} : { api = var.session_key_storage }
  filesystem      = var.session_key_storage == null ? "" : "arn:aws:elasticfilesystem:${var.region}:${var.account_id}:file-system/${var.session_key_storage.file_system_id}"
  access_point    = var.session_key_storage == null ? "" : "arn:aws:elasticfilesystem:${var.region}:${var.account_id}:access-point/${var.session_key_storage.access_point_id}"
  trust = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "OnlyAccountRegionalECSTasks", Effect = "Allow", Action = "sts:AssumeRole", Principal = { Service = "ecs-tasks.amazonaws.com" },
      Condition = { StringEquals = { "aws:SourceAccount" = var.account_id }, ArnLike = { "aws:SourceArn" = "arn:aws:ecs:${var.region}:${var.account_id}:*" } }
    }]
  })
  fields = {
    api = concat(["ConnectionStrings__App", "Identity__Okta__ClientSecret", "Identity__KeyCertificateBase64", "Identity__KeyCertificatePassword", "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePassword"], flatten([
      for i in range(max(0, min(4, floor(var.api_retired_certificates)))) : ["Identity__PreviousKeyCertificates__${i}__Base64", "Identity__PreviousKeyCertificates__${i}__Password"]
    ]))
    worker    = ["ConnectionStrings__SearchWorker"]
    migration = ["SAVEIT_DEPLOYMENT_CONNECTION"]
    access    = ["SAVEIT_ACCESS_CONNECTION"]
    web       = []
  }
}

# There is intentionally no secret-version/value resource or secret-value lookup.
resource "aws_kms_key" "configuration" {
  for_each                = local.configured
  description             = "${local.name} ${each.key} deployment configuration"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root }, Action = "kms:*", Resource = "*" },
      { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
      Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
      { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } },
      { Sid = "ConfigurationPrincipalsOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = sort(distinct(concat(local.admins, local.custodians, [local.exec_arns[each.key]]))) } } },
      { Sid = "SecretsManagerOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com" } } },
      { Sid = "OnlyThisCallerAccount", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotEqualsIfExists = { "kms:CallerAccount" = var.account_id } } },
      { Sid = "ConfigurationSlotOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins }, StringNotLikeIfExists = { "kms:EncryptionContext:SecretARN" = local.secret_patterns[each.key] } } },
      { Sid = "ExecutionVersionOnly", Effect = "Deny", Principal = "*", Action = "kms:Decrypt", Resource = "*",
      Condition = { ArnEquals = { "aws:PrincipalArn" = local.exec_arns[each.key] }, StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = lookup(var.configuration_versions, each.key, "") } } },
      { Sid = "IAMDelegationThroughSecretsManager", Effect = "Allow", Principal = { AWS = local.root }, Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = "*",
      Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } } }
    ]
  })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "configuration" {
  for_each      = local.configured
  name          = "alias/${local.name}-${each.key}-configuration"
  target_key_id = aws_kms_key.configuration[each.key].key_id
}
resource "aws_secretsmanager_secret" "configuration" {
  for_each                = local.configured
  name                    = local.secret_names[each.key]
  description             = "Pinned ${each.key} JSON deployment configuration; populate through protected operations, never Terraform"
  kms_key_id              = aws_kms_key.configuration[each.key].arn
  recovery_window_in_days = 30
  lifecycle { prevent_destroy = true }
}
resource "aws_secretsmanager_secret_policy" "configuration" {
  # UpdateSecret is required for metadata maintenance but can also carry values.
  # Trusted metadata admins can therefore replace payloads as well as policies;
  # VersionCustodiansOnly is not an exclusive-value-writer guarantee.
  for_each            = local.configured
  secret_arn          = aws_secretsmanager_secret.configuration[each.key].arn
  block_public_policy = true
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "secretsmanager:*", Resource = aws_secretsmanager_secret.configuration[each.key].arn,
      Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
      { Sid = "ExactReadersOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.configuration[each.key].arn,
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(local.custodians, [local.exec_arns[each.key]]) } } },
      { Sid = "MetadataAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret", "secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = aws_secretsmanager_secret.configuration[each.key].arn,
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } },
      { Sid = "VersionCustodiansOnly", Effect = "Deny", Principal = "*", Action = ["secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = aws_secretsmanager_secret.configuration[each.key].arn,
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.custodians } } },
      { Sid = "ExecutionVersionOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.configuration[each.key].arn,
      Condition = { ArnEquals = { "aws:PrincipalArn" = local.exec_arns[each.key] }, StringNotEqualsIfExists = { "secretsmanager:VersionId" = lookup(var.configuration_versions, each.key, "") } } }
    ]
  })
}

data "aws_efs_file_system" "ring" {
  for_each       = local.ring
  file_system_id = each.value.file_system_id
  lifecycle {
    postcondition {
      condition     = self.arn == local.filesystem && self.encrypted && try(self.tags["Project"], "") == "SaveIT" && try(self.tags["Cell"], "") == var.cell && try(self.tags["Environment"], "") == var.environment
      error_message = "Bind only the designated account/region/cell's encrypted session-key filesystem."
    }
  }
}
data "aws_efs_access_point" "ring" {
  for_each        = local.ring
  access_point_id = each.value.access_point_id
  lifecycle {
    postcondition {
      condition = (self.arn == local.access_point && self.file_system_id == each.value.file_system_id &&
        try(one(self.posix_user).uid, 0) == 1654 && try(one(self.posix_user).gid, 0) == 1654 &&
        try(one(self.root_directory).path, "") == "/saveit-session-keys" &&
        try(one(one(self.root_directory).creation_info).owner_uid, 0) == 1654 && try(one(one(self.root_directory).creation_info).owner_gid, 0) == 1654 &&
        contains(["0700", "700"], try(one(one(self.root_directory).creation_info).permissions, "")) &&
      try(self.tags["Project"], "") == "SaveIT" && try(self.tags["Cell"], "") == var.cell && try(self.tags["Environment"], "") == var.environment)
      error_message = "Bind only the corresponding cell's nonroot private session-key access-point definition."
    }
  }
}

resource "aws_iam_role" "task" {
  for_each             = local.workloads
  name                 = "${local.name}-${each.key}-task"
  assume_role_policy   = local.trust
  permissions_boundary = aws_iam_policy.task_boundary[each.key].arn
  max_session_duration = 3600
}
resource "aws_iam_role" "execution" {
  for_each             = local.workloads
  name                 = "${local.name}-${each.key}-execution"
  assume_role_policy   = local.trust
  permissions_boundary = aws_iam_policy.execution_boundary[each.key].arn
  max_session_duration = 3600
  # The resource guards are installed before execution identities become usable.
  depends_on = [aws_secretsmanager_secret_policy.configuration]
}
resource "aws_iam_role_policy" "execution" {
  for_each = local.workloads
  name     = "ReviewedExecutionOnly"
  role     = aws_iam_role.execution[each.key].id
  policy   = local.execution_grants[each.key]
  lifecycle {
    precondition {
      condition     = length(local.execution_grants[each.key]) <= 10240
      error_message = "Execution inline policy exceeds the IAM limit; do not widen or truncate policy scope."
    }
    precondition {
      condition     = !contains([for key in values(aws_kms_key.configuration) : key.arn], lookup(local.bound_log_keys, each.key, ""))
      error_message = "Log keys must remain separate from every configuration key; never conflate service/context authorities."
    }
  }
}
resource "aws_iam_role_policy" "api_ring" {
  for_each   = local.ring
  name       = "ExactSessionKeyRing"
  role       = aws_iam_role.task["api"].id
  policy     = jsonencode({ Version = "2012-10-17", Statement = local.ring_grants })
  depends_on = [data.aws_efs_file_system.ring, data.aws_efs_access_point.ring]
}
