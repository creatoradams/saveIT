mock_provider "aws" { override_during = plan }
override_resource {
  override_during = plan
  target          = aws_kms_key.registry
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000000", key_id = "20000000-0000-0000-0000-000000000000" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs["api"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001", key_id = "20000000-0000-0000-0000-000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_cloudwatch_log_group.workloads["api"]
  values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs["web"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002", key_id = "20000000-0000-0000-0000-000000000002" }
}
override_resource {
  override_during = plan
  target          = aws_cloudwatch_log_group.workloads["web"]
  values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/web" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs["worker"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003", key_id = "20000000-0000-0000-0000-000000000003" }
}
override_resource {
  override_during = plan
  target          = aws_cloudwatch_log_group.workloads["worker"]
  values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs["migration"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004", key_id = "20000000-0000-0000-0000-000000000004" }
}
override_resource {
  override_during = plan
  target          = aws_cloudwatch_log_group.workloads["migration"]
  values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/migration" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs["access"]
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005", key_id = "20000000-0000-0000-0000-000000000005" }
}
override_resource {
  override_during = plan
  target          = aws_cloudwatch_log_group.workloads["access"]
  values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/access" }
}
override_resource {
  override_during = plan
  target          = aws_ecr_repository.images["api"]
  values          = { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", repository_url = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/api", registry_id = "111122223333" }
}
override_resource {
  override_during = plan
  target          = aws_ecr_repository.images["web"]
  values          = { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web", repository_url = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/web", registry_id = "111122223333" }
}
override_resource {
  override_during = plan
  target          = aws_ecr_repository.images["worker"]
  values          = { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/worker", repository_url = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/worker", registry_id = "111122223333" }
}
override_resource {
  override_during = plan
  target          = aws_ecr_repository.images["deployment"]
  values          = { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment", repository_url = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/deployment", registry_id = "111122223333" }
}
variables {
  account_id         = "111122223333"
  region             = "us-east-1"
  cell               = "pilot"
  environment        = "staging"
  key_administrators = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  publisher_role     = "arn:aws:iam::111122223333:role/SaveITProtectedPublisher"
}

run "private_images_are_immutable_encrypted_and_retained" {
  command = plan
  assert {
    condition = (
      toset(keys(aws_ecr_repository.images)) == toset(["api", "web", "worker", "deployment"]) && alltrue([for name, repository in aws_ecr_repository.images :
        repository.name == format("saveit/pilot/staging/%s", name) && repository.image_tag_mutability == "IMMUTABLE" &&
        length(repository.image_tag_mutability_exclusion_filter) == 0 && !repository.force_delete &&
        one(repository.image_scanning_configuration).scan_on_push &&
        one(repository.encryption_configuration).encryption_type == "KMS" && one(repository.encryption_configuration).kms_key == aws_kms_key.registry.arn &&
        output.repositories[name].arn == repository.arn && output.repositories[name].url == format("111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/%s", name)
    ]))
    error_message = "Four private image stores must pin immutable tags without exceptions, KMS, scan-on-push and non-force deletion."
  }
  assert {
    condition = (
      toset(output.repositories.deployment.pull_execution_roles) == toset(["arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"]) &&
      alltrue([for name in ["api", "web", "worker"] : toset(output.repositories[name].pull_execution_roles) == toset([format("arn:aws:iam::111122223333:role/saveit-pilot-staging-%s-execution", name)])])
    )
    error_message = "Only migration/access share an image; application execution roles stay separate."
  }
}

run "separate_bounded_protected_log_groups_and_keys" {
  command = plan
  assert {
    condition = (toset(keys(aws_cloudwatch_log_group.workloads)) == toset(["api", "web", "worker", "migration", "access"]) &&
      length(distinct([for key in values(aws_kms_key.logs) : key.arn])) == 5 &&
      !contains([for key in values(aws_kms_key.logs) : key.arn], aws_kms_key.registry.arn) &&
      alltrue([for name, group in aws_cloudwatch_log_group.workloads :
        group.name == format("/saveit/pilot/staging/%s", name) && group.log_group_class == "STANDARD" && group.retention_in_days == 90 &&
        group.deletion_protection_enabled && group.kms_key_id == aws_kms_key.logs[name].arn &&
        output.log_groups[name].arn == group.arn && output.log_groups[name].retention_days == 90 &&
        output.log_groups[name].execution_role_arn == format("arn:aws:iam::111122223333:role/saveit-pilot-staging-%s-execution", name)
    ]) && alltrue([for key in concat([aws_kms_key.registry], values(aws_kms_key.logs)) : key.enable_key_rotation && key.deletion_window_in_days == 30]))
    error_message = "Five protected finite-retention log stores must have distinct keys and identities, separate from registry storage."
  }
}

run "complete_repository_policy_denies" {
  command = plan
  assert {
    condition = (
      alltrue([for name, policy in aws_ecr_repository_policy.images :
        policy.repository == aws_ecr_repository.images[name].name && length(policy.policy) <= 10240 &&
        jsondecode(policy.policy) == jsondecode(jsonencode({
          Version = "2012-10-17"
          Statement = [
            { Sid = "RequireTLS", Effect = "Deny", Principal = "*", Action = "ecr:*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
            { Sid = "ExactImageReaders", Effect = "Deny", Principal = "*", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(name == "deployment" ? ["arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"] : [format("arn:aws:iam::111122223333:role/saveit-pilot-staging-%s-execution", name)], [var.publisher_role], concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"])) } } },
            { Sid = "ProtectedPublisherOnly", Effect = "Deny", Principal = "*", Action = ["ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = var.publisher_role } } },
            { Sid = "PreserveImagesAndTags", Effect = "Deny", Principal = "*", Action = "ecr:BatchDeleteImage", Resource = "*" },
            { Sid = "RepositoryAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) } } }
          ]
    }))]))
    error_message = "Preserve complete TLS, reader, sole-publisher, deletion and control-plane deny shapes; these are not identity grants."
  }
}

run "repository_identity_and_transport_predicates" {
  command = plan
  assert {
    condition = (
      alltrue([for context in [
        { image = "api", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution", admitted = true },
        { image = "api", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task", admitted = false },
        { image = "api", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution", admitted = false },
        { image = "api", role = "arn:aws:iam::999900001111:role/saveit-pilot-staging-api-execution", admitted = false },
        { image = "api", role = null, admitted = false },
        { image = "api", role = var.publisher_role, admitted = true },
        { image = "api", role = one(var.key_administrators), admitted = true },
        { image = "deployment", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", admitted = true },
        { image = "deployment", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution", admitted = true },
        { image = "deployment", role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution", admitted = false }
    ] : try(contains(jsondecode(aws_ecr_repository_policy.images[context.image].policy).Statement[1].Condition.ArnNotEquals["aws:PrincipalArn"], context.role), false) == context.admitted]))
    error_message = "Task, foreign/wrong/missing identity predicates must exclude image pulls; this is not IAM simulation."
  }
  assert {
    condition = (
      alltrue([for context in [
        { role = var.publisher_role, admitted = true },
        { role = one(var.key_administrators), admitted = false },
        { role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution", admitted = false },
        { role = null, admitted = false }
      ] : (context.role == jsondecode(aws_ecr_repository_policy.images["api"].policy).Statement[2].Condition.ArnNotEquals["aws:PrincipalArn"]) == context.admitted]) &&
      alltrue([for context in [{ tls = "true", admitted = true }, { tls = "false", admitted = false }, { tls = null, admitted = false }] :
        (context.tls != null && context.tls != jsondecode(aws_ecr_repository_policy.images["api"].policy).Statement[0].Condition.BoolIfExists["aws:SecureTransport"]) == context.admitted
    ]))
    error_message = "Only the publisher meets its role predicate; missing/false TLS is rejected by the deny intent."
  }
}

run "complete_registry_kms_policy" {
  command = plan
  assert {
    condition = (
      jsondecode(aws_kms_key.registry.policy) == jsondecode(jsonencode({
        Version = "2012-10-17"
        Statement = concat([
          { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = "arn:aws:iam::111122223333:root" }, Action = "kms:*", Resource = "*" },
          { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) }, Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
          { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) } } }
          ], [
          { Sid = "RegionalECROnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "kms:ViaService" = "ecr.us-east-1.amazonaws.com" } } },
          { Sid = "CallerAccountOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "kms:CallerAccount" = "111122223333" } } },
          { Sid = "ExactRepositoryContexts", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "kms:EncryptionContext:aws:ecr:arn" = [for name in ["api", "deployment", "web", "worker"] : format("arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/%s", name)] } } },
          { Sid = "AWSResourceGrantsOnly", Effect = "Deny", Principal = "*", Action = "kms:CreateGrant", Resource = "*", Condition = { BoolIfExists = { "kms:GrantIsForAWSResource" = "false" } } },
          { Sid = "RegionalRepositoryData", Effect = "Allow", Principal = { AWS = "*" }, Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = "*", Condition = { StringEquals = { "kms:ViaService" = "ecr.us-east-1.amazonaws.com", "kms:CallerAccount" = "111122223333", "kms:EncryptionContext:aws:ecr:arn" = [for name in ["api", "deployment", "web", "worker"] : format("arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/%s", name)] } } }
        ])
    })))
    error_message = "Preserve full ECR KMS action/resource/principal/service/account/context/grant boundaries and trusted administration."
  }
}

run "complete_log_kms_policies" {
  command = plan
  assert {
    condition = (
      alltrue([for name, key in aws_kms_key.logs : jsondecode(key.policy) == jsondecode(jsonencode({
        Version = "2012-10-17"
        Statement = concat([
          { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = "arn:aws:iam::111122223333:root" }, Action = "kms:*", Resource = "*" },
          { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) }, Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
          { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) } } }
          ], [
          { Sid = "RegionalLogsOrForwardingOnly", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, StringNotEqualsIfExists = { "aws:PrincipalServiceName" = "logs.us-east-1.amazonaws.com", "kms:ViaService" = "logs.us-east-1.amazonaws.com" } } },
          { Sid = "ExactLogContext", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::111122223333:root"]) }, ArnNotEqualsIfExists = { "kms:EncryptionContext:aws:logs:arn" = format("arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/%s", name) } } },
          { Sid = "RegionalLogData", Effect = "Allow", Principal = { Service = "logs.us-east-1.amazonaws.com" }, Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*"], Resource = "*", Condition = { ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = format("arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/%s", name) } } }
        ])
    }))]))
    error_message = "Log keys must preserve complete service/forwarding and context boundaries; forwarding needs external caller IAM grants."
  }
}

run "registry_context_and_service_grant_predicates" {
  command = plan
  assert {
    condition = (
      alltrue([for context in [
        { service = "ecr.us-east-1.amazonaws.com", account = "111122223333", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", admitted = true },
        { service = "ecr.us-east-1.amazonaws.com", account = "111122223333", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment", admitted = true },
        { service = null, account = "111122223333", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", admitted = false },
        { service = "ecr.us-west-2.amazonaws.com", account = "111122223333", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", admitted = false },
        { service = "ecr.us-east-1.amazonaws.com", account = null, repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", admitted = false },
        { service = "ecr.us-east-1.amazonaws.com", account = "999900001111", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api", admitted = false },
        { service = "ecr.us-east-1.amazonaws.com", account = "111122223333", repository = null, admitted = false },
        { service = "ecr.us-east-1.amazonaws.com", account = "111122223333", repository = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/other/staging/api", admitted = false }
        ] : (context.service == jsondecode(aws_kms_key.registry.policy).Statement[7].Condition.StringEquals["kms:ViaService"] &&
        context.account == jsondecode(aws_kms_key.registry.policy).Statement[7].Condition.StringEquals["kms:CallerAccount"] &&
      try(contains(jsondecode(aws_kms_key.registry.policy).Statement[7].Condition.StringEquals["kms:EncryptionContext:aws:ecr:arn"], context.repository), false)) == context.admitted]) &&
      alltrue([for context in [{ resource = "true", admitted = true }, { resource = "false", admitted = false }, { resource = null, admitted = false }] :
    (context.resource != null && context.resource != jsondecode(aws_kms_key.registry.policy).Statement[6].Condition.BoolIfExists["kms:GrantIsForAWSResource"]) == context.admitted]))
    error_message = "Every missing/wrong service/account/repository and missing/false service-grant context must fail the generated narrow predicates."
  }
}

run "logs_direct_or_forwarded_context_predicates" {
  command = plan
  assert {
    condition = (
      alltrue([for context in [
        { principal = "logs.us-east-1.amazonaws.com", via = null, group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = true },
        { principal = null, via = "logs.us-east-1.amazonaws.com", group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = true },
        { principal = "other.amazonaws.com", via = "logs.us-east-1.amazonaws.com", group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = true },
        { principal = "logs.us-east-1.amazonaws.com", via = "other.amazonaws.com", group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = true },
        { principal = null, via = null, group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = false },
        { principal = "logs.us-west-2.amazonaws.com", via = "logs.us-west-2.amazonaws.com", group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", admitted = false },
        { principal = "logs.us-east-1.amazonaws.com", via = null, group = null, admitted = false },
        { principal = "logs.us-east-1.amazonaws.com", via = null, group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker", admitted = false }
        ] : ((context.principal == jsondecode(aws_kms_key.logs["api"].policy).Statement[3].Condition.StringNotEqualsIfExists["aws:PrincipalServiceName"] ||
        context.via == jsondecode(aws_kms_key.logs["api"].policy).Statement[3].Condition.StringNotEqualsIfExists["kms:ViaService"]) &&
    context.group == jsondecode(aws_kms_key.logs["api"].policy).Statement[4].Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"]) == context.admitted]))
    error_message = "Nonadministrator data-use boundary requires the exact group and either direct Logs or Logs forwarding; neither route independently grants access."
  }
}

run "largest_inputs_fit_service_limits" {
  command = plan
  variables {
    cell               = "abcdefghijkl"
    environment        = "production"
    publisher_role     = format("arn:aws:iam::111122223333:role/%s/Publisher0", join("", [for unused in range(214) : "p"]))
    key_administrators = [for index in range(8) : format("arn:aws:iam::111122223333:role/%s/Admin%s", join("", [for unused in range(218) : "a"]), index)]
    log_retention_days = 365
  }
  assert {
    condition = (
      alltrue([for policy in values(aws_ecr_repository_policy.images) : length(policy.policy) <= 10240]) &&
      length(aws_kms_key.registry.policy) <= 32768 &&
      alltrue([for key in values(aws_kms_key.logs) : length(key.policy) <= 32768]) &&
    alltrue([for group in values(aws_cloudwatch_log_group.workloads) : group.retention_in_days == 365]))
    error_message = "Largest accepted ARN/namespace sets must fit policy limits and finite retention."
  }
}

run "shortest_finite_retention" {
  command = plan
  variables { log_retention_days = 30 }
  assert {
    condition     = (alltrue([for group in values(aws_cloudwatch_log_group.workloads) : group.retention_in_days == 30 && group.deletion_protection_enabled]))
    error_message = "Retention selection must retain deletion protection."
  }
}

run "reject_foreign_publisher" {
  command = plan
  variables { publisher_role = "arn:aws:iam::999900001111:role/Publisher" }
  expect_failures = [var.publisher_role]
}

run "reject_wildcard_publisher" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/Publisher*" }
  expect_failures = [var.publisher_role]
}

run "reject_task_publisher" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task" }
  expect_failures = [var.publisher_role]
}

run "reject_execution_publisher_path_alias" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/path/saveit-pilot-staging-access-execution" }
  expect_failures = [var.publisher_role]
}

run "reject_administrator_as_publisher" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator" }
  expect_failures = [var.publisher_role]
}

run "reject_foreign_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::999900001111:role/Admin"] }
  expect_failures = [var.key_administrators]
}

run "reject_task_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/path/saveit-pilot-staging-worker-task"] }
  expect_failures = [var.key_administrators]
}

run "reject_empty_administrators" {
  command = plan
  variables { key_administrators = [] }
  expect_failures = [var.key_administrators]
}

run "reject_too_many_administrators" {
  command = plan
  variables { key_administrators = [for index in range(9) : format("arn:aws:iam::111122223333:role/Admin%s", index)] }
  expect_failures = [var.key_administrators]
}

run "reject_wildcard_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/Admin*"] }
  expect_failures = [var.key_administrators]
}

run "reject_invalid_role_path" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/path//Publisher" }
  expect_failures = [var.publisher_role]
}

run "reject_oversized_publisher" {
  command = plan
  variables { publisher_role = format("arn:aws:iam::111122223333:role/%s/Publisher", join("", [for unused in range(250) : "p"])) }
  expect_failures = [var.publisher_role]
}

run "reject_indefinite_retention" {
  command = plan
  variables { log_retention_days = 0 }
  expect_failures = [var.log_retention_days]
}

run "reject_too_short_retention" {
  command = plan
  variables { log_retention_days = 14 }
  expect_failures = [var.log_retention_days]
}

run "reject_fractional_retention" {
  command = plan
  variables { log_retention_days = 30.5 }
  expect_failures = [var.log_retention_days]
}

run "reject_oversized_retention" {
  command = plan
  variables { log_retention_days = 731 }
  expect_failures = [var.log_retention_days]
}

run "reject_unreviewed_environment" {
  command = plan
  variables { environment = "development" }
  expect_failures = [var.environment]
}

run "reject_unreviewed_partition" {
  command = plan
  variables { region = "cn-north-1" }
  expect_failures = [var.region]
}

run "reject_invalid_cell" {
  command = plan
  variables { cell = "Pilot*" }
  expect_failures = [var.cell]
}
