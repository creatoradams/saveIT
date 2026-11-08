locals {
  endpoint_workloads = merge(var.identity.workloads, var.relay == null ? {} : {
    relay = { execution_role_arn = var.relay.roles.execution_arn, repository_arn = try(var.relay.image_repository.arn, ""), log_group_arn = var.relay.operational_log.arn }
  })
  configuration_readers = merge({ for name, c in var.identity.configuration : name => {
    arn = c.arn, version_id = c.version_id, role_arn = var.identity.workloads[name].execution_role_arn
    } }, var.relay == null ? {} : {
    relay = { arn = var.relay.configuration.arn, version_id = var.relay.configuration.version_id, role_arn = var.relay.roles.task_arn }
  })
  execution_roles  = sort([for w in values(local.endpoint_workloads) : w.execution_role_arn])
  configured_roles = sort([for c in values(local.configuration_readers) : c.role_arn])
  pull_actions     = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
  log_actions      = ["logs:CreateLogStream", "logs:PutLogEvents"]
  tls_deny         = { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } }
  ecr_policy = jsonencode({ Version = "2012-10-17", Statement = concat([
    local.tls_deny,
    { Sid = "ExecutionIdentitiesOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = local.execution_roles } } },
    { Sid = "PullActionsOnly", Effect = "Deny", Principal = "*", NotAction = concat(["ecr:GetAuthorizationToken"], local.pull_actions), Resource = "*" },
    { Sid = "AuthenticateExecutions", Effect = "Allow", Principal = "*", Action = "ecr:GetAuthorizationToken", Resource = "*", Condition = { ArnEquals = { "aws:PrincipalArn" = local.execution_roles } } }
    ], flatten([for name, w in local.endpoint_workloads : [
      { Sid = "Pull${title(name)}", Effect = "Allow", Principal = "*", Action = local.pull_actions, Resource = w.repository_arn, Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } },
      { Sid = "RejectOther${title(name)}Repositories", Effect = "Deny", Principal = "*", Action = local.pull_actions, NotResource = w.repository_arn, Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } }
  ]])) })
  logs_policy = jsonencode({ Version = "2012-10-17", Statement = concat([
    local.tls_deny,
    { Sid = "ExecutionIdentitiesOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = local.execution_roles } } },
    { Sid = "WriteStreamsOnly", Effect = "Deny", Principal = "*", NotAction = local.log_actions, Resource = "*" }
    ], flatten([for name, w in local.endpoint_workloads : [
      { Sid = "Write${title(name)}Streams", Effect = "Allow", Principal = "*", Action = local.log_actions, Resource = "${w.log_group_arn}:log-stream:*", Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } },
      { Sid = "RejectOther${title(name)}Streams", Effect = "Deny", Principal = "*", Action = local.log_actions, NotResource = "${w.log_group_arn}:log-stream:*", Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } }
  ]])) })
  secrets_policy = jsonencode({ Version = "2012-10-17", Statement = concat([
    local.tls_deny,
    { Sid = var.relay == null ? "ConfiguredExecutionsOnly" : "ConfiguredReadersOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = local.configured_roles } } },
    { Sid = "ReadValueOnly", Effect = "Deny", Principal = "*", NotAction = "secretsmanager:GetSecretValue", Resource = "*" }
    ], flatten([for name, c in local.configuration_readers : [
      { Sid = "Read${title(name)}Version", Effect = "Allow", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = c.arn,
      Condition = { ArnEquals = { "aws:PrincipalArn" = c.role_arn }, StringEquals = { "secretsmanager:VersionId" = c.version_id } } },
      { Sid = "RejectOther${title(name)}Configurations", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", NotResource = c.arn,
      Condition = { ArnEquals = { "aws:PrincipalArn" = c.role_arn } } },
      { Sid = "RejectOther${title(name)}Versions", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = c.arn,
      Condition = { ArnEquals = { "aws:PrincipalArn" = c.role_arn }, StringNotEqualsIfExists = { "secretsmanager:VersionId" = c.version_id } } }
  ]])) })
  layer_objects = "arn:aws:s3:::prod-${var.region}-starport-layer-bucket/*"
  s3_policy = jsonencode({ Version = "2012-10-17", Statement = [
    local.tls_deny,
    { Sid = "LayerReadOnly", Effect = "Deny", Principal = "*", NotAction = "s3:GetObject", Resource = "*" },
    { Sid = "RegionalLayerObjectsOnly", Effect = "Deny", Principal = "*", Action = "s3:GetObject", NotResource = local.layer_objects },
    { Sid = "ECRPresignedLayers", Effect = "Allow", Principal = "*", Action = "s3:GetObject", Resource = local.layer_objects }
  ] })
}
