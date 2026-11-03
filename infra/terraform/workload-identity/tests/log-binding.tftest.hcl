mock_provider "aws" { override_during = plan }

override_resource {
  override_during = plan
  target          = aws_kms_key.configuration["api"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000001", key_id = "00000000-0000-0000-0000-000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_secretsmanager_secret.configuration["api"]
  values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.configuration["worker"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000002", key_id = "00000000-0000-0000-0000-000000000002" }
}
override_resource {
  override_during = plan
  target          = aws_secretsmanager_secret.configuration["worker"]
  values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd01" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.configuration["migration"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000003", key_id = "00000000-0000-0000-0000-000000000003" }
}
override_resource {
  override_during = plan
  target          = aws_secretsmanager_secret.configuration["migration"]
  values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/migration/config-Abcd02" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.configuration["access"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000004", key_id = "00000000-0000-0000-0000-000000000004" }
}
override_resource {
  override_during = plan
  target          = aws_secretsmanager_secret.configuration["access"]
  values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/access/config-Abcd03" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task["api"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task", id = "saveit-pilot-staging-api-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary["api"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-api-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution["api"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution", id = "saveit-pilot-staging-api-execution" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary["api"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-api-execution-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task["web"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task", id = "saveit-pilot-staging-web-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary["web"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-web-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution["web"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution", id = "saveit-pilot-staging-web-execution" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary["web"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-web-execution-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task["worker"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-task", id = "saveit-pilot-staging-worker-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary["worker"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-worker-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution["worker"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution", id = "saveit-pilot-staging-worker-execution" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary["worker"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-worker-execution-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task["migration"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-task", id = "saveit-pilot-staging-migration-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary["migration"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-migration-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution["migration"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", id = "saveit-pilot-staging-migration-execution" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary["migration"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-migration-execution-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task["access"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-task", id = "saveit-pilot-staging-access-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary["access"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-access-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution["access"]
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution", id = "saveit-pilot-staging-access-execution" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary["access"]
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-access-execution-boundary" }
}

override_data {
  target = data.aws_efs_file_system.ring["api"]
  values = {
    arn       = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001"
    encrypted = true
    tags      = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
  }
}
override_data {
  target = data.aws_efs_access_point.ring["api"]
  values = {
    arn            = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
    file_system_id = "fs-00000000000000001"
    posix_user     = [{ uid = 1654, gid = 1654 }]
    root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags           = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
  }
}
variables {
  account_id         = "111122223333"
  region             = "us-east-1"
  cell               = "pilot"
  environment        = "staging"
  key_administrators = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  secret_custodians  = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
  configuration_versions = {
    api       = "10000000-0000-0000-0000-000000000001"
    worker    = "10000000-0000-0000-0000-000000000002"
    migration = "10000000-0000-0000-0000-000000000003"
    access    = "10000000-0000-0000-0000-000000000004"
  }
  operational_log_keys = { api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", web = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", worker = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", access = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005" }
}

run "exact_log_crypto_and_description_grants" {
  command = plan
  assert {
    condition = output.log_key_binding_ready && alltrue([for name, policy in aws_iam_role_policy.execution :
      one([for statement in jsondecode(policy.policy).Statement : statement if statement.Sid == "OwnEncryptedLogData"]) == {
        Sid       = "OwnEncryptedLogData", Effect = "Allow", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = var.operational_log_keys[name],
        Condition = { StringEquals = { "kms:ViaService" = "logs.us-east-1.amazonaws.com" }, ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = format("arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/%s", name) } }
      } &&
      one([for statement in jsondecode(policy.policy).Statement : statement if statement.Sid == "OwnLogKeyDescription"]) == {
        Sid = "OwnLogKeyDescription", Effect = "Allow", Action = "kms:DescribeKey", Resource = var.operational_log_keys[name], Condition = { StringEquals = { "kms:ViaService" = "logs.us-east-1.amazonaws.com" } }
      }
    ])
    error_message = "Each execution role must have exact-key via-Logs crypto/context and separate description without unavailable encryption context; no general KMS or query grant."
  }
}
run "complete_bound_execution_boundaries" {
  command = plan
  assert {
    condition = alltrue([for name, boundary in aws_iam_policy.execution_boundary : jsondecode(boundary.policy) == jsondecode(jsonencode({
      Version = "2012-10-17"
      Statement = concat([
        { Sid = "MaximumExecutionActions", Effect = "Allow", Action = distinct(concat(["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage", "logs:CreateLogStream", "logs:PutLogEvents"], name == "web" ? [] : ["secretsmanager:GetSecretValue", "kms:Decrypt"], ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"])), Resource = "*" },
        { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = distinct(concat(["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage", "logs:CreateLogStream", "logs:PutLogEvents"], name == "web" ? [] : ["secretsmanager:GetSecretValue", "kms:Decrypt"], ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"])), Resource = "*" },
        { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = "us-east-1" } } },
        { Sid = "RejectOtherRepositories", Effect = "Deny", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], NotResource = output.workloads[name].repository_arn },
        { Sid = "RejectOtherLogStreams", Effect = "Deny", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], NotResource = format("%s:log-stream:*", output.workloads[name].log_group_arn) },
        { Sid = "RejectOtherCryptoKeys", Effect = "Deny", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], NotResource = concat([var.operational_log_keys[name]], [for key in [try(aws_kms_key.configuration[name].arn, "")] : key if name != "web"]) },
        { Sid = "RejectOtherDescribedKeys", Effect = "Deny", Action = "kms:DescribeKey", NotResource = var.operational_log_keys[name] },
        { Sid = "RejectDirectLogKeyUse", Effect = "Deny", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = var.operational_log_keys[name], Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "logs.us-east-1.amazonaws.com" } } },
        { Sid = "RejectOtherLogContexts", Effect = "Deny", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = var.operational_log_keys[name], Condition = { ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = output.workloads[name].log_group_arn } } }
        ], [for statement in [
          { Sid = "RejectOtherSecrets", Effect = "Deny", Action = "secretsmanager:GetSecretValue", NotResource = try(output.configuration[name].arn, "") },
          { Sid = "RejectOtherSecretVersions", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { StringNotEqualsIfExists = { "secretsmanager:VersionId" = lookup(var.configuration_versions, name, "") } } },
          { Sid = "ConfigurationDecryptOnly", Effect = "Deny", Action = ["kms:Encrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = try(output.configuration[name].key_arn, "") },
          { Sid = "RejectDirectConfigurationDecryption", Effect = "Deny", Action = "kms:Decrypt", Resource = try(output.configuration[name].key_arn, ""), Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com" } } },
          { Sid = "RejectOtherConfigurationSecretContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = try(output.configuration[name].key_arn, ""), Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretARN" = try(output.configuration[name].arn, "") } } },
          { Sid = "RejectOtherConfigurationVersionContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = try(output.configuration[name].key_arn, ""), Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = lookup(var.configuration_versions, name, "") } } }
      ] : statement if name != "web"])
    }))])
    error_message = "Complete bound policy must preserve all explicit action/resource/region/secret/version denies while permitting only own-log KMS use; web gains no secret/query actions."
  }
}
run "own_log_crypto_predicates_reject_wrong_missing_cross_workload_contexts" {
  command = plan
  assert {
    condition = alltrue([for context in [
      { key = var.operational_log_keys.api, service = "logs.us-east-1.amazonaws.com", group = output.workloads.api.log_group_arn, admitted = true },
      { key = var.operational_log_keys.worker, service = "logs.us-east-1.amazonaws.com", group = output.workloads.api.log_group_arn, admitted = false },
      { key = output.configuration.api.key_arn, service = "logs.us-east-1.amazonaws.com", group = output.workloads.api.log_group_arn, admitted = false },
      { key = var.operational_log_keys.api, service = null, group = output.workloads.api.log_group_arn, admitted = false },
      { key = var.operational_log_keys.api, service = "secretsmanager.us-east-1.amazonaws.com", group = output.workloads.api.log_group_arn, admitted = false },
      { key = var.operational_log_keys.api, service = "logs.us-east-1.amazonaws.com", group = null, admitted = false },
      { key = var.operational_log_keys.api, service = "logs.us-east-1.amazonaws.com", group = output.workloads.worker.log_group_arn, admitted = false }
      ] : (context.key == var.operational_log_keys.api &&
      context.service == jsondecode(aws_iam_policy.execution_boundary["api"].policy).Statement[7].Condition.StringNotEqualsIfExists["kms:ViaService"] &&
    context.group == jsondecode(aws_iam_policy.execution_boundary["api"].policy).Statement[8].Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"]) == context.admitted])
    error_message = "Own-log admission predicates must reject every missing/wrong key, service or log context; full statement tests couple them to explicit denies."
  }
}
run "maximum_log_bound_policies_fit_iam_limits" {
  command = plan
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["api"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/abcdefghijkl/production/api/config-Abcd00" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["worker"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/abcdefghijkl/production/worker/config-Abcd01" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["migration"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/abcdefghijkl/production/migration/config-Abcd02" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["access"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/abcdefghijkl/production/access/config-Abcd03" }
  }

  variables {
    cell        = "abcdefghijkl"
    environment = "production"
  }
  assert {
    condition = (alltrue([for policy in values(aws_iam_policy.execution_boundary) : length(policy.policy) <= 6144]) &&
    alltrue([for policy in values(aws_iam_role_policy.execution) : length(policy.policy) <= 10240]))
    error_message = "Log-bound execution policies must remain within IAM limits at the largest cell name."
  }
}

run "reject_missing_log_key" {
  command = plan
  variables { operational_log_keys = { api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001" } }
  expect_failures = [var.operational_log_keys]
}

run "reject_duplicate_log_keys" {
  command = plan
  variables { operational_log_keys = { for name in ["api", "web", "worker", "migration", "access"] : name => "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001" } }
  expect_failures = [var.operational_log_keys]
}

run "reject_foreign_log_account" {
  command = plan
  variables { operational_log_keys = merge({ api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", web = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", worker = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", access = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005" }, { api = "arn:aws:kms:us-east-1:999900001111:key/20000000-0000-0000-0000-000000000001" }) }
  expect_failures = [var.operational_log_keys]
}

run "reject_foreign_log_region" {
  command = plan
  variables { operational_log_keys = merge({ api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", web = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", worker = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", access = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005" }, { api = "arn:aws:kms:us-west-2:111122223333:key/20000000-0000-0000-0000-000000000001" }) }
  expect_failures = [var.operational_log_keys]
}

run "reject_configuration_key_as_log_key" {
  command = plan
  variables { operational_log_keys = merge({ api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", web = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", worker = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", access = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005" }, { api = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000001" }) }
  expect_failures = [aws_iam_role_policy.execution["api"]]
}

run "reject_configuration_other_role_key_as_log_key" {
  command = plan
  variables { operational_log_keys = merge({ api = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", web = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", worker = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", access = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005" }, { api = "arn:aws:kms:us-east-1:111122223333:key/00000000-0000-0000-0000-000000000002" }) }
  expect_failures = [aws_iam_role_policy.execution["api"]]
}
