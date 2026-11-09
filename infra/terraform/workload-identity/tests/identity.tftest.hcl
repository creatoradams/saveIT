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
}

run "bootstrap_roles_fail_closed_and_exact_trust" {
  command = plan
  assert {
    condition     = output.session_key_binding == null && output.operational_log_keys == null && !output.log_key_binding_ready
    error_message = "Concrete binding outputs must retain null fail-closed bootstrap behavior."
  }
  assert {
    condition = length(aws_iam_role.task) == 5 && length(aws_iam_role.execution) == 5 && !output.session_key_binding_ready && length(aws_iam_role_policy.api_ring) == 0 && alltrue([
      for name, role in aws_iam_role.task : role.permissions_boundary == aws_iam_policy.task_boundary[name].arn && role.max_session_duration == 3600 && jsondecode(aws_iam_policy.task_boundary[name].policy).Statement == [
        { Sid = "NoAWSWorkloadAuthority", Effect = "Deny", Action = "*", Resource = "*" }
      ]
    ])
    error_message = "Bootstrap and all non-API tasks must have explicit deny-all boundaries, with no mount grant."
  }
  assert {
    condition = alltrue([for name, role in merge({ for name, role in aws_iam_role.task : "task-${name}" => role }, { for name, role in aws_iam_role.execution : "execution-${name}" => role }) :
      jsondecode(role.assume_role_policy).Statement == [{
        Sid       = "OnlyAccountRegionalECSTasks", Effect = "Allow", Action = "sts:AssumeRole", Principal = { Service = "ecs-tasks.amazonaws.com" },
        Condition = { StringEquals = { "aws:SourceAccount" = "111122223333" }, ArnLike = { "aws:SourceArn" = "arn:aws:ecs:us-east-1:111122223333:*" } }
      }]
    ]) && alltrue([for name, role in aws_iam_role.execution : role.permissions_boundary == aws_iam_policy.execution_boundary[name].arn])
    error_message = "Every distinct role must trust only account/regional ECS tasks and attach its own boundary."
  }
}
run "separate_configuration_and_same_version_selectors" {
  command = plan
  assert {
    condition = toset(keys(output.configuration)) == toset(["api", "worker", "migration", "access"]) && length(output.workloads.web.secrets) == 0 && alltrue([for name, configuration in output.configuration :
      configuration.version_id == var.configuration_versions[name] && configuration.arn == aws_secretsmanager_secret.configuration[name].arn &&
      aws_secretsmanager_secret.configuration[name].kms_key_id == aws_kms_key.configuration[name].arn &&
      aws_kms_key.configuration[name].enable_key_rotation && aws_kms_key.configuration[name].deletion_window_in_days == 30 &&
      aws_secretsmanager_secret.configuration[name].recovery_window_in_days == 30 &&
      alltrue([for selector in output.workloads[name].secrets : selector.valueFrom == "${configuration.arn}:${selector.name}::${configuration.version_id}"]) &&
      length(distinct([for other in values(output.configuration) : other.arn])) == 4 &&
      length(distinct([for other in values(output.configuration) : other.key_arn])) == 4
    ])
    error_message = "Four separate encrypted configuration containers must pin all of each workload's fields to one version; web has none."
  }
  assert {
    condition = (output.configuration.api.fields == ["ConnectionStrings__App", "Identity__Okta__ClientSecret", "Identity__KeyCertificateBase64", "Identity__KeyCertificatePassword", "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePassword"] &&
      output.configuration.worker.fields == ["ConnectionStrings__SearchWorker"] && output.configuration.migration.fields == ["SAVEIT_DEPLOYMENT_CONNECTION"] &&
    output.configuration.access.fields == ["SAVEIT_ACCESS_CONNECTION"])
    error_message = "Configuration selectors must use the exact runtime field names and isolate privileged connections."
  }
}
run "execution_grants_resources_and_explicit_boundary_denies" {
  command = plan
  assert {
    condition = alltrue([for name, policy in aws_iam_role_policy.execution :
      policy.role == aws_iam_role.execution[name].id &&
      jsondecode(policy.policy).Statement[0] == { Sid = "RegionalRegistryAuthorization", Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*", Condition = { StringEquals = { "aws:RequestedRegion" = "us-east-1" } } } &&
      jsondecode(policy.policy).Statement[1].Resource == "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/${contains(["migration", "access"], name) ? "deployment" : name}" &&
      toset(jsondecode(policy.policy).Statement[1].Action) == toset(["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]) &&
      jsondecode(policy.policy).Statement[2].Resource == "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/${name}:log-stream:*" &&
      toset(jsondecode(policy.policy).Statement[2].Action) == toset(["logs:CreateLogStream", "logs:PutLogEvents"]) &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[1].Effect == "Deny" &&
      toset(jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[1].NotAction) == toset(concat(["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage", "logs:CreateLogStream", "logs:PutLogEvents"], name == "web" ? [] : ["secretsmanager:GetSecretValue", "kms:Decrypt"])) &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[2] == { Sid = "RejectOtherRegions", Effect = "Deny", Action = "*", Resource = "*", Condition = { StringNotEqualsIfExists = { "aws:RequestedRegion" = "us-east-1" } } } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[3] == { Sid = "RejectOtherRepositories", Effect = "Deny", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], NotResource = jsondecode(policy.policy).Statement[1].Resource } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[4] == { Sid = "RejectOtherLogStreams", Effect = "Deny", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], NotResource = jsondecode(policy.policy).Statement[2].Resource }
    ]) && length(jsondecode(aws_iam_role_policy.execution["web"].policy).Statement) == 3 && length(jsondecode(aws_iam_policy.execution_boundary["web"].policy).Statement) == 5
    error_message = "Exact image/log grants and explicit action/resource/region denials must cap additional identity/session allows, including STS/IAM/SSM/secret writes."
  }
  assert {
    condition = alltrue([for name, configuration in output.configuration :
      jsondecode(aws_iam_role_policy.execution[name].policy).Statement[3] == {
        Sid       = "PinnedConfigurationVersion", Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = configuration.arn,
        Condition = { StringEquals = { "secretsmanager:VersionId" = configuration.version_id } }
      } &&
      jsondecode(aws_iam_role_policy.execution[name].policy).Statement[4] == {
        Sid       = "ConfigurationDecryptionOnly", Effect = "Allow", Action = "kms:Decrypt", Resource = configuration.key_arn,
        Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com", "kms:EncryptionContext:SecretARN" = configuration.arn, "kms:EncryptionContext:SecretVersionId" = configuration.version_id } }
      } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[5] == { Sid = "RejectOtherSecrets", Effect = "Deny", Action = "secretsmanager:GetSecretValue", NotResource = configuration.arn } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[6] == { Sid = "RejectOtherSecretVersions", Effect = "Deny", Action = "secretsmanager:GetSecretValue", Resource = "*", Condition = { StringNotEqualsIfExists = { "secretsmanager:VersionId" = configuration.version_id } } } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[7] == { Sid = "RejectOtherKeys", Effect = "Deny", Action = "kms:Decrypt", NotResource = configuration.key_arn } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[8] == { Sid = "RejectDirectDecryption", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com" } } } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[9] == { Sid = "RejectOtherSecretContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretARN" = configuration.arn } } } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[10] == { Sid = "RejectOtherVersionContexts", Effect = "Deny", Action = "kms:Decrypt", Resource = "*", Condition = { StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = configuration.version_id } } }
    ])
    error_message = "Each execution role must be explicitly denied other secrets/keys/versions and direct or wrong-context decryption."
  }
}
run "secret_and_key_resource_policy_defenses" {
  command = plan
  assert {
    condition = alltrue([for name, policy in aws_secretsmanager_secret_policy.configuration :
      policy.block_public_policy && policy.secret_arn == output.configuration[name].arn &&
      jsondecode(policy.policy).Statement == [
        { Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "secretsmanager:*", Resource = policy.secret_arn, Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
        { Sid = "ExactReadersOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = policy.secret_arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.secret_custodians)), ["arn:aws:iam::111122223333:root", output.workloads[name].execution_role_arn]) } } },
        { Sid = "MetadataAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret", "secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = policy.secret_arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) } } },
        { Sid = "VersionCustodiansOnly", Effect = "Deny", Principal = "*", Action = ["secretsmanager:PutSecretValue", "secretsmanager:UpdateSecretVersionStage"], Resource = policy.secret_arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.secret_custodians)), ["arn:aws:iam::111122223333:root"]) } } },
        { Sid = "ExecutionVersionOnly", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = policy.secret_arn, Condition = { ArnEquals = { "aws:PrincipalArn" = output.workloads[name].execution_role_arn }, StringNotEqualsIfExists = { "secretsmanager:VersionId" = output.configuration[name].version_id } } }
      ]
    ])
    error_message = "Secret policies must deny plaintext, other readers, execution version mismatch and unauthorized value/control writes."
  }
  assert {
    condition = alltrue([for name, key in aws_kms_key.configuration :
      jsondecode(key.policy).Statement[2].Effect == "Deny" &&
      jsondecode(key.policy).Statement[2].Condition.ArnNotEquals["aws:PrincipalArn"] == concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) &&
      toset(jsondecode(key.policy).Statement[3].Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(concat(tolist(var.key_administrators), tolist(var.secret_custodians), ["arn:aws:iam::111122223333:root", output.workloads[name].execution_role_arn])) &&
      jsondecode(key.policy).Statement[4].Condition.StringNotEqualsIfExists == { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com" } &&
      jsondecode(key.policy).Statement[5].Condition.StringNotEqualsIfExists == { "kms:CallerAccount" = "111122223333" } &&
      jsondecode(key.policy).Statement[6].Condition.StringNotLikeIfExists == { "kms:EncryptionContext:SecretARN" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/${name}/config-??????" } &&
      jsondecode(key.policy).Statement[7].Condition == { ArnEquals = { "aws:PrincipalArn" = output.workloads[name].execution_role_arn }, StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = output.configuration[name].version_id } } &&
      alltrue([for index in [3, 4, 5, 6, 7] : jsondecode(key.policy).Statement[index].Effect == "Deny" && jsondecode(key.policy).Statement[index].Principal == "*"])
    ])
    error_message = "Independent KMS deny statements must constrain principal, service, account, slot and execution version, including missing context."
  }
}
run "decryption_predicates_reject_each_wrong_or_missing_context" {
  command = plan
  # Narrow StringEquals allow predicates only; not a general IAM simulator.
  assert {
    condition = alltrue([for context in [
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = output.configuration.api.arn, version = var.configuration_versions.api, allowed = true },
      { service = null, secret = output.configuration.api.arn, version = var.configuration_versions.api, allowed = false },
      { service = "secretsmanager.us-west-2.amazonaws.com", secret = output.configuration.api.arn, version = var.configuration_versions.api, allowed = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = null, version = var.configuration_versions.api, allowed = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = output.configuration.worker.arn, version = var.configuration_versions.api, allowed = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = output.configuration.api.arn, version = null, allowed = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = output.configuration.api.arn, version = var.configuration_versions.worker, allowed = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", secret = output.configuration.api.arn, version = "AWSCURRENT", allowed = false }
      ] : (context.service == jsondecode(aws_iam_role_policy.execution["api"].policy).Statement[4].Condition.StringEquals["kms:ViaService"] &&
      context.secret == jsondecode(aws_iam_role_policy.execution["api"].policy).Statement[4].Condition.StringEquals["kms:EncryptionContext:SecretARN"] &&
    context.version == jsondecode(aws_iam_role_policy.execution["api"].policy).Statement[4].Condition.StringEquals["kms:EncryptionContext:SecretVersionId"]) == context.allowed])
    error_message = "Changing or omitting any decryption predicate must fail the actual generated allow condition."
  }
}
run "bound_api_mount_only_and_maximum_retired_selectors" {
  command = plan
  variables {
    session_key_storage      = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" }
    api_retired_certificates = 4
  }
  assert {
    condition = (output.session_key_binding_ready && length(aws_iam_role_policy.api_ring) == 1 &&
      jsondecode(aws_iam_role_policy.api_ring["api"].policy).Statement == [{
        Sid       = "ExactProtectedRing", Effect = "Allow", Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"],
        Resource  = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001",
        Condition = { Bool = { "aws:SecureTransport" = "true", "elasticfilesystem:AccessedViaMountTarget" = "true" }, StringEquals = { "elasticfilesystem:AccessPointArn" = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001" } }
      }] &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[0] == jsondecode(aws_iam_role_policy.api_ring["api"].policy).Statement[0] &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[1] == { Sid = "RejectAllOtherActions", Effect = "Deny", NotAction = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = "*" } &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[2] == { Sid = "RejectOtherFilesystems", Effect = "Deny", Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], NotResource = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001" } &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[3] == { Sid = "RejectOtherAccessPoints", Effect = "Deny", Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = "*", Condition = { StringNotEqualsIfExists = { "elasticfilesystem:AccessPointArn" = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001" } } } &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[4] == { Sid = "RejectInsecureMounts", Effect = "Deny", Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } } &&
      jsondecode(aws_iam_policy.task_boundary["api"].policy).Statement[5] == { Sid = "RejectOutsideMountTargets", Effect = "Deny", Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = "*", Condition = { BoolIfExists = { "elasticfilesystem:AccessedViaMountTarget" = "false" } } } &&
    alltrue([for name in ["web", "worker", "migration", "access"] : jsondecode(aws_iam_policy.task_boundary[name].policy).Statement[0].Action == "*" && jsondecode(aws_iam_policy.task_boundary[name].policy).Statement[0].Effect == "Deny"]))
    error_message = "Only the bound API may mount/write the exact protected ring; root/other AWS operations must remain explicitly denied."
  }
  assert {
    condition = (length(output.workloads.api.secrets) == 14 && slice(output.configuration.api.fields, 6, 14) == flatten([for index in range(4) : ["Identity__PreviousKeyCertificates__${index}__Base64", "Identity__PreviousKeyCertificates__${index}__Password"]]) &&
    alltrue([for selector in output.workloads.api.secrets : selector.valueFrom == "${output.configuration.api.arn}:${selector.name}::${var.configuration_versions.api}"]))
    error_message = "All current and four retired PFX/password pairs must reference one pinned API configuration version."
  }
}
run "maximum_privileged_inputs_fit_service_policy_limits" {
  command = plan
  variables {
    cell                     = "abcdefghijkl"
    environment              = "production"
    key_administrators       = [for index in range(8) : "arn:aws:iam::111122223333:role/${join("", [for unused in range(218) : "a"])}/Admin${index}"]
    secret_custodians        = [for index in range(8) : "arn:aws:iam::111122223333:role/${join("", [for unused in range(218) : "c"])}/User${index}"]
    api_retired_certificates = 4
  }
  assert {
    condition = (alltrue([for policy in values(aws_iam_policy.execution_boundary) : length(policy.policy) <= 6144]) &&
      alltrue([for policy in values(aws_iam_policy.task_boundary) : length(policy.policy) <= 6144]) &&
      alltrue([for policy in values(aws_iam_role_policy.execution) : length(policy.policy) <= 10240]) &&
      alltrue([for key in values(aws_kms_key.configuration) : length(key.policy) <= 32768]) &&
    alltrue([for policy in values(aws_secretsmanager_secret_policy.configuration) : length(policy.policy) <= 20480]))
    error_message = "Even largest accepted privileged-role inputs must fit managed/inline/KMS/secret policy service limits."
  }
}


run "complete_kms_boundaries_and_independent_account_service_predicates" {
  command = plan
  assert {
    condition = alltrue([for name, key in aws_kms_key.configuration : jsondecode(key.policy) == jsondecode(jsonencode({
      Version = "2012-10-17"
      Statement = [
        { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = "arn:aws:iam::111122223333:root" }, Action = "kms:*", Resource = "*" },
        { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
        Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
        { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
        Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) } } },
        { Sid = "ConfigurationPrincipalsOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
        Condition = { ArnNotEquals = { "aws:PrincipalArn" = sort(distinct(concat(tolist(var.key_administrators), tolist(var.secret_custodians), ["arn:aws:iam::111122223333:root", output.workloads[name].execution_role_arn]))) } } },
        { Sid = "SecretsManagerOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
        Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com" } } },
        { Sid = "OnlyThisCallerAccount", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
        Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "kms:CallerAccount" = "111122223333" } } },
        { Sid = "ConfigurationSlotOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
        Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotLikeIfExists = { "kms:EncryptionContext:SecretARN" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/${name}/config-??????" } } },
        { Sid = "ExecutionVersionOnly", Effect = "Deny", Principal = "*", Action = "kms:Decrypt", Resource = "*",
        Condition = { ArnEquals = { "aws:PrincipalArn" = output.workloads[name].execution_role_arn }, StringNotEqualsIfExists = { "kms:EncryptionContext:SecretVersionId" = var.configuration_versions[name] } } },
        { Sid = "IAMDelegationThroughSecretsManager", Effect = "Allow", Principal = { AWS = "arn:aws:iam::111122223333:root" }, Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = "*",
        Condition = { StringEquals = { "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com", "kms:CallerAccount" = "111122223333" } } }
      ]
    }))])
    error_message = "Complete KMS denies must preserve actions, resources, principals and independent context guards; root/admin delegation is explicitly trusted."
  }
  # Two separate deny statements are essential: combining the not-equals keys in
  # one condition would require BOTH to be wrong. These are narrow predicates.
  assert {
    condition = alltrue([for context in [
      { service = "secretsmanager.us-east-1.amazonaws.com", account = "111122223333", denied = false },
      { service = "secretsmanager.us-east-1.amazonaws.com", account = null, denied = true },
      { service = "secretsmanager.us-east-1.amazonaws.com", account = "999900001111", denied = true },
      { service = null, account = "111122223333", denied = true },
      { service = "secretsmanager.us-west-2.amazonaws.com", account = "111122223333", denied = true },
      { service = null, account = null, denied = true }
      ] : (context.service != jsondecode(aws_kms_key.configuration["api"].policy).Statement[4].Condition.StringNotEqualsIfExists["kms:ViaService"] ||
    context.account != jsondecode(aws_kms_key.configuration["api"].policy).Statement[5].Condition.StringNotEqualsIfExists["kms:CallerAccount"]) == context.denied])
    error_message = "For a nonadministrator principal, either missing/wrong service OR account must satisfy an independent deny predicate."
  }
}

run "reject_foreign_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::999900001111:role/Admin"] }
  expect_failures = [var.key_administrators]
}

run "reject_wildcard_custodian" {
  command = plan
  variables { secret_custodians = ["arn:aws:iam::111122223333:role/Custodian*"] }
  expect_failures = [var.secret_custodians]
}

run "reject_task_administrator_collision" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/path/saveit-pilot-staging-api-task"] }
  expect_failures = [var.key_administrators]
}

run "reject_execution_custodian_collision" {
  command = plan
  variables { secret_custodians = ["arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"] }
  expect_failures = [var.secret_custodians]
}

run "reject_empty_administrators" {
  command = plan
  variables { key_administrators = [] }
  expect_failures = [var.key_administrators]
}

run "reject_too_many_custodians" {
  command = plan
  variables { secret_custodians = [for index in range(9) : "arn:aws:iam::111122223333:role/Custodian${index}"] }
  expect_failures = [var.secret_custodians]
}

run "reject_missing_configuration" {
  command = plan
  variables { configuration_versions = { api = "10000000-0000-0000-0000-000000000001" } }
  expect_failures = [var.configuration_versions]
}

run "reject_extra_web_configuration" {
  command = plan
  variables { configuration_versions = { for name in ["api", "web", "worker", "migration", "access"] : name => "10000000-0000-0000-0000-000000000001" } }
  expect_failures = [var.configuration_versions]
}

run "reject_stage_instead_of_version" {
  command = plan
  variables { configuration_versions = { for name in ["api", "worker", "migration", "access"] : name => "AWSCURRENT" } }
  expect_failures = [var.configuration_versions]
}

run "reject_negative_retired" {
  command = plan
  variables { api_retired_certificates = -1 }
  expect_failures = [var.api_retired_certificates]
}

run "reject_fractional_retired" {
  command = plan
  variables { api_retired_certificates = 1.5 }
  expect_failures = [var.api_retired_certificates]
}

run "reject_too_many_retired" {
  command = plan
  variables { api_retired_certificates = 5 }
  expect_failures = [var.api_retired_certificates]
}

run "reject_bad_storage_ids" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-*", access_point_id = "fsap-*" } }
  expect_failures = [var.session_key_storage]
}

run "reject_foreign_filesystem" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_file_system.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:999900001111:file-system/fs-00000000000000001"
      encrypted = true
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_file_system.ring["api"]]
}

run "reject_unencrypted_filesystem" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_file_system.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001"
      encrypted = false
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_file_system.ring["api"]]
}

run "reject_wrong_cell_filesystem" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_file_system.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001"
      encrypted = true
    tags = { Project = "SaveIT", Cell = "other", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_file_system.ring["api"]]
}

run "reject_wrong_access_point_region" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-west-2:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 1654, gid = 1654 }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "reject_wrong_access_point_filesystem" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000002"
      posix_user     = [{ uid = 1654, gid = 1654 }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "reject_wrong_access_point_uid" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 0, gid = 1654 }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "reject_wrong_access_point_root" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 1654, gid = 1654 }]
      root_directory = [{ path = "/", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "reject_wrong_access_point_permission" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 1654, gid = 1654 }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0755" }] }]
    tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "reject_wrong_access_point_cell" {
  command = plan
  variables { session_key_storage = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" } }
  override_data {
    target = data.aws_efs_access_point.ring["api"]
    values = { arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 1654, gid = 1654 }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags = { Project = "SaveIT", Cell = "other", Environment = "staging" } }
  }
  expect_failures = [data.aws_efs_access_point.ring["api"]]
}

run "web_pool_bootstrap_retains_deny_all" {

  command = plan
  variables {
    web_workload_pool_enabled = true
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }

  }
  assert {
    condition     = output.web_workload_pool == jsondecode(file("tests/web-workload-pool.json")) && output.web_workload_admission == null && length(aws_iam_role_policy.web_token) == 0 && jsondecode(aws_iam_policy.task_boundary["web"].policy).Statement == [{ Sid = "NoAWSWorkloadAuthority", Effect = "Deny", Action = "*", Resource = "*" }]
    error_message = "Pool creation alone must grant no token or other AWS authority."
  }
  assert {
    condition     = !aws_cognito_identity_pool.web[0].allow_classic_flow && !aws_cognito_identity_pool.web[0].allow_unauthenticated_identities && length(aws_cognito_identity_pool.web[0].openid_connect_provider_arns) == 0 && length(aws_cognito_identity_pool.web[0].saml_provider_arns) == 0 && length(aws_cognito_identity_pool.web[0].supported_login_providers) == 0
    error_message = "Developer-only pool must not add public identities, providers or classic flow."
  }

}
run "bound_web_token_has_complete_exact_policy" {

  command = plan
  variables {
    web_workload_pool_enabled   = true
    web_workload_token_endpoint = jsondecode(file("tests/web-token-endpoint.json"))
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }

  }
  override_data {
    override_during = plan
    target          = data.aws_vpc_endpoint.web_token[0]
    values          = { "id" : "vpce-00000000000000009", "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc-endpoint/vpce-00000000000000009", "owner_id" : "111122223333", "vpc_id" : "vpc-00000000000000001", "service_name" : "com.amazonaws.us-east-1.cognito-identity", "vpc_endpoint_type" : "Interface", "state" : "available", "private_dns_enabled" : true, "ip_address_type" : "ipv4", "subnet_ids" : ["subnet-00000000000000001", "subnet-00000000000000002"], "security_group_ids" : ["sg-00000000000000009"], "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web-token" }, "policy" : "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyWebTokenIssuance\",\"Effect\":\"Allow\",\"Principal\":{\"AWS\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"},\"Action\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectOtherPrincipals\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"ArnNotEquals\":{\"aws:PrincipalArn\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"}}},{\"Sid\":\"RejectOtherActions\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"NotAction\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"*\"},{\"Sid\":\"RejectOtherPools\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"NotResource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectInsecureTransport\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"BoolIfExists\":{\"aws:SecureTransport\":\"false\"}}}]}" }
  }
  assert {
    condition     = jsondecode(aws_iam_policy.task_boundary["web"].policy) == jsondecode(file("tests/web-token-boundary.json")) && jsondecode(aws_iam_role_policy.web_token[0].policy) == jsondecode(file("tests/web-token-boundary.json")) && output.web_workload_admission.binding_ready && output.workloads.web.secrets == []
    error_message = "Only exact developer-token minting through own endpoint, never secret/STS authority."
  }

}
run "reject_web_token_policy" {

  command = plan
  variables {
    web_workload_pool_enabled   = true
    web_workload_token_endpoint = jsondecode(file("tests/web-token-endpoint.json"))
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }

  }
  override_data {
    override_during = plan
    target          = data.aws_vpc_endpoint.web_token[0]
    values          = { "id" : "vpce-00000000000000009", "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc-endpoint/vpce-00000000000000009", "owner_id" : "111122223333", "vpc_id" : "vpc-00000000000000001", "service_name" : "com.amazonaws.us-east-1.cognito-identity", "vpc_endpoint_type" : "Interface", "state" : "available", "private_dns_enabled" : true, "ip_address_type" : "ipv4", "subnet_ids" : ["subnet-00000000000000001", "subnet-00000000000000002"], "security_group_ids" : ["sg-00000000000000009"], "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web-token" }, "policy" : "{\"Version\":\"2012-10-17\",\"Statement\":[]}" }
  }
  expect_failures = [data.aws_vpc_endpoint.web_token[0]]

}
run "reject_web_token_public_dns" {

  command = plan
  variables {
    web_workload_pool_enabled   = true
    web_workload_token_endpoint = jsondecode(file("tests/web-token-endpoint.json"))
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }

  }
  override_data {
    override_during = plan
    target          = data.aws_vpc_endpoint.web_token[0]
    values          = { "id" : "vpce-00000000000000009", "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc-endpoint/vpce-00000000000000009", "owner_id" : "111122223333", "vpc_id" : "vpc-00000000000000001", "service_name" : "com.amazonaws.us-east-1.cognito-identity", "vpc_endpoint_type" : "Interface", "state" : "available", "private_dns_enabled" : false, "ip_address_type" : "ipv4", "subnet_ids" : ["subnet-00000000000000001", "subnet-00000000000000002"], "security_group_ids" : ["sg-00000000000000009"], "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web-token" }, "policy" : "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyWebTokenIssuance\",\"Effect\":\"Allow\",\"Principal\":{\"AWS\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"},\"Action\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectOtherPrincipals\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"ArnNotEquals\":{\"aws:PrincipalArn\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"}}},{\"Sid\":\"RejectOtherActions\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"NotAction\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"*\"},{\"Sid\":\"RejectOtherPools\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"NotResource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectInsecureTransport\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"BoolIfExists\":{\"aws:SecureTransport\":\"false\"}}}]}" }
  }
  expect_failures = [data.aws_vpc_endpoint.web_token[0]]

}
run "reject_web_token_foreign_owner" {

  command = plan
  variables {
    web_workload_pool_enabled   = true
    web_workload_token_endpoint = jsondecode(file("tests/web-token-endpoint.json"))
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }

  }
  override_data {
    override_during = plan
    target          = data.aws_vpc_endpoint.web_token[0]
    values          = { "id" : "vpce-00000000000000009", "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc-endpoint/vpce-00000000000000009", "owner_id" : "444455556666", "vpc_id" : "vpc-00000000000000001", "service_name" : "com.amazonaws.us-east-1.cognito-identity", "vpc_endpoint_type" : "Interface", "state" : "available", "private_dns_enabled" : true, "ip_address_type" : "ipv4", "subnet_ids" : ["subnet-00000000000000001", "subnet-00000000000000002"], "security_group_ids" : ["sg-00000000000000009"], "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web-token" }, "policy" : "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyWebTokenIssuance\",\"Effect\":\"Allow\",\"Principal\":{\"AWS\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"},\"Action\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectOtherPrincipals\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"ArnNotEquals\":{\"aws:PrincipalArn\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"}}},{\"Sid\":\"RejectOtherActions\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"NotAction\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"*\"},{\"Sid\":\"RejectOtherPools\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"NotResource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectInsecureTransport\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"BoolIfExists\":{\"aws:SecureTransport\":\"false\"}}}]}" }
  }
  expect_failures = [data.aws_vpc_endpoint.web_token[0]]

}
run "reject_endpoint_before_pool_bootstrap" {

  command = plan
  variables {
    web_workload_token_endpoint = jsondecode(file("tests/web-token-endpoint.json"))
  }
  expect_failures = [var.web_workload_token_endpoint]

}
