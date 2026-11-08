mock_provider "aws" { override_during = plan }
override_resource {
  override_during = plan
  target          = aws_iam_policy.task_boundary
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-relay-task-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_iam_policy.execution_boundary
  values          = { arn = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-relay-execution-boundary" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.configuration
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000001", key_id = "30000000-0000-0000-0000-000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_kms_key.logs
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000002", key_id = "30000000-0000-0000-0000-000000000002" }
}
override_resource {
  override_during = plan
  target          = aws_secretsmanager_secret.configuration
  values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/relay/config-Abcd90" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.task
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-task", id = "saveit-pilot-staging-relay-task" }
}
override_resource {
  override_during = plan
  target          = aws_iam_role.execution
  values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution", id = "saveit-pilot-staging-relay-execution" }
}
variables {
  account_id               = "111122223333"
  region                   = "us-east-1"
  cell                     = "pilot"
  environment              = "staging"
  key_administrators       = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
  configuration_version_id = "10000000-0000-0000-0000-000000000009"
}

run "null_bootstrap_denies_both_roles_even_with_resource_mentions" {
  command = plan
  assert {
    condition     = alltrue([for policy in [aws_iam_policy.task_boundary.policy, aws_iam_policy.execution_boundary.policy] : jsondecode(policy).Statement == jsondecode("[{\"Action\":\"*\",\"Effect\":\"Deny\",\"Resource\":\"*\",\"Sid\":\"UnboundNoAWSAuthority\"}]")]) && length(aws_iam_role_policy.task) == 0 && length(aws_iam_role_policy.execution) == 0 && !output.binding_ready && output.image_repository == null
    error_message = "Neither bootstrap identity can acquire AWS authority through extra identity/session grants."
  }
  assert {
    condition     = aws_kms_key.configuration.enable_key_rotation && aws_kms_key.logs.enable_key_rotation && aws_kms_key.configuration.deletion_window_in_days == 30 && aws_kms_key.logs.deletion_window_in_days == 30 && aws_secretsmanager_secret.configuration.recovery_window_in_days == 30 && aws_cloudwatch_log_group.relay.deletion_protection_enabled && aws_cloudwatch_log_group.relay.retention_in_days == 90 && aws_cloudwatch_log_group.relay.kms_key_id == aws_kms_key.logs.arn && aws_secretsmanager_secret.configuration.kms_key_id == aws_kms_key.configuration.arn
    error_message = "Null bootstrap retains distinct protected rotating configuration/log stores without payloads."
  }
}
run "bound_exact_declared_store_contract" {
  command = plan
  variables {
    image_repository = {
      arn                  = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment"
      url                  = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/deployment"
      pull_execution_roles = ["arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution", "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution"]
    }
  }
  assert {
    condition     = output.binding_ready && length(aws_iam_role_policy.task) == 1 && length(aws_iam_role_policy.execution) == 1 && aws_iam_role.task.permissions_boundary == aws_iam_policy.task_boundary.arn && aws_iam_role.execution.permissions_boundary == aws_iam_policy.execution_boundary.arn && aws_iam_role.task.max_session_duration == 3600 && aws_iam_role.execution.max_session_duration == 3600
    error_message = "Binding adds only separate own task/execution inline grants under explicit boundaries."
  }
  assert {
    condition     = jsondecode(aws_iam_role.task.assume_role_policy).Statement == jsondecode(aws_iam_role.execution.assume_role_policy).Statement && jsondecode(aws_iam_role.task.assume_role_policy).Statement[0].Condition.StringEquals["aws:SourceAccount"] == var.account_id && jsondecode(aws_iam_role.task.assume_role_policy).Statement[0].Condition.ArnLike["aws:SourceArn"] == "arn:aws:ecs:us-east-1:111122223333:*"
    error_message = "Trust stays exact ECS service/account/region without inventing unsupported cluster-specific trust."
  }
  assert {
    condition     = length(aws_iam_policy.task_boundary.policy) <= 6144 && length(aws_iam_policy.execution_boundary.policy) <= 6144 && length(aws_iam_role_policy.task[0].policy) <= 10240 && length(aws_iam_role_policy.execution[0].policy) <= 10240
    error_message = "Never truncate or widen policies to fit IAM limits."
  }
}
run "task_reads_only_own_whole_secret_uuid_and_context" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition     = toset([for s in jsondecode(aws_iam_role_policy.task[0].policy).Statement : s.Action]) == toset(["secretsmanager:GetSecretValue", "kms:Decrypt"]) && one([for s in jsondecode(aws_iam_role_policy.task[0].policy).Statement : s if s.Action == "secretsmanager:GetSecretValue"]).Resource == output.configuration.arn && one([for s in jsondecode(aws_iam_role_policy.task[0].policy).Statement : s if s.Action == "secretsmanager:GetSecretValue"]).Condition.StringEquals["secretsmanager:VersionId"] == var.configuration_version_id && tomap(one([for s in jsondecode(aws_iam_role_policy.task[0].policy).Statement : s if s.Action == "kms:Decrypt"]).Condition.StringEquals) == tomap({ "kms:ViaService" = "secretsmanager.us-east-1.amazonaws.com", "kms:CallerAccount" = "111122223333", "kms:EncryptionContext:SecretARN" = output.configuration.arn, "kms:EncryptionContext:SecretVersionId" = var.configuration_version_id })
    error_message = "Task gets exactly own pinned whole JSON and via-Secrets-Manager/account/ARN/version decrypt, never selectors or other credentials."
  }
  assert {
    condition     = toset(one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectAllOtherActions"]).NotAction) == toset(["secretsmanager:GetSecretValue", "kms:Decrypt"]) && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherSecrets"]).NotResource == output.configuration.arn && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherDecryptKeys"]).NotResource == output.configuration.key_arn && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherSecretVersions"]).Condition.StringNotEqualsIfExists["secretsmanager:VersionId"] == var.configuration_version_id && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherVersionContexts"]).Condition.StringNotEqualsIfExists["kms:EncryptionContext:SecretVersionId"] == var.configuration_version_id
    error_message = "Missing/wrong secret versions/keys are explicit denies even with additional grants."
  }
}
run "execution_never_reads_configuration_and_only_uses_own_log_key" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition     = length([for s in jsondecode(aws_iam_role_policy.execution[0].policy).Statement : s if startswith(s.Sid, "Own")]) == 3 && one([for s in jsondecode(aws_iam_role_policy.execution[0].policy).Statement : s if s.Sid == "ExactDeploymentImagePull"]).Resource == output.image_repository.arn && one([for s in jsondecode(aws_iam_role_policy.execution[0].policy).Statement : s if s.Sid == "OwnEncryptedLogData"]).Resource == output.operational_log.key_arn && one([for s in jsondecode(aws_iam_role_policy.execution[0].policy).Statement : s if s.Sid == "OwnEncryptedLogData"]).Condition.ArnEquals["kms:EncryptionContext:aws:logs:arn"] == "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay" && one([for s in jsondecode(aws_iam_role_policy.execution[0].policy).Statement : s if s.Sid == "OwnLogKeyDescription"]).Condition == jsondecode("{\"StringEquals\":{\"kms:ViaService\":\"logs.us-east-1.amazonaws.com\",\"kms:CallerAccount\":\"111122223333\"}}")
    error_message = "Execution owns exact deployment pull and own Logs crypto; DescribeKey is via Logs without encryption context."
  }
  assert {
    condition     = !contains(one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectAllOtherActions"]).NotAction, "secretsmanager:GetSecretValue") && one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectOtherCryptoKeys"]).NotResource == output.operational_log.key_arn && output.configuration.key_arn != output.operational_log.key_arn && one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectOtherCallerAccounts"]).Condition.StringNotEqualsIfExists["kms:CallerAccount"] == var.account_id && one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectOtherLogContexts"]).Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"] == "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay"
    error_message = "Execution cannot read/decrypt configuration or escape own key/service/account/context via extra grants."
  }
}
run "secret_and_key_resource_denies_preserve_runtime_and_custodian_split" {
  command = plan
  assert {
    condition     = aws_secretsmanager_secret_policy.configuration.block_public_policy && toset(one([for s in jsondecode(aws_secretsmanager_secret_policy.configuration.policy).Statement : s if s.Sid == "ExactReadersOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(["arn:aws:iam::111122223333:role/SaveITSecretCustodian", "arn:aws:iam::111122223333:root", "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-task"]) && one([for s in jsondecode(aws_secretsmanager_secret_policy.configuration.policy).Statement : s if s.Sid == "TaskVersionOnly"]).Condition.StringNotEqualsIfExists["secretsmanager:VersionId"] == var.configuration_version_id && one([for s in jsondecode(aws_kms_key.configuration.policy).Statement : s if s.Sid == "TaskVersionOnly"]).Condition.ArnEquals["aws:PrincipalArn"] == output.roles.task_arn && one([for s in jsondecode(aws_kms_key.configuration.policy).Statement : s if s.Sid == "ConfigurationSlotOnly"]).Condition.StringNotLikeIfExists["kms:EncryptionContext:SecretARN"] == "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/relay/config-??????"
    error_message = "Only runtime task version is pinned; trusted custodian key validation context remains separate."
  }
}
run "log_key_requires_service_caller_and_exact_group_without_context_on_description" {
  command = plan
  assert {
    condition     = one([for s in jsondecode(aws_kms_key.logs.policy).Statement : s if s.Sid == "CallerAccountOnly"]).Condition.StringNotEqualsIfExists["kms:CallerAccount"] == var.account_id && tomap(one([for s in jsondecode(aws_kms_key.logs.policy).Statement : s if s.Sid == "RegionalLogsOrForwardingOnly"]).Condition.StringNotEqualsIfExists) == tomap({ "aws:PrincipalServiceName" = "logs.us-east-1.amazonaws.com", "kms:ViaService" = "logs.us-east-1.amazonaws.com" }) && one([for s in jsondecode(aws_kms_key.logs.policy).Statement : s if s.Sid == "ExactLogContext"]).Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"] == "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay" && !contains(keys(one([for s in jsondecode(aws_kms_key.logs.policy).Statement : s if s.Sid == "RegionalLogDescription"]).Condition), "ArnEquals")
    error_message = "Crypto needs the own-group context while separate DescribeKey does not; service/account refusals remain fail-closed."
  }
}
run "withdraw_binding_retains_stores_and_denies_both_roles" {
  command = plan
  assert {
    condition     = !output.binding_ready && length(aws_iam_role_policy.task) == 0 && length(aws_iam_role_policy.execution) == 0 && aws_secretsmanager_secret.configuration.name == "saveit/pilot/staging/relay/config" && aws_cloudwatch_log_group.relay.name == "/saveit/pilot/staging/relay" && output.roles.task_arn == run.bound_exact_declared_store_contract.roles.task_arn
    error_message = "Unbound plan shape retains stores/identities; it is not an applied withdrawal/revocation proof."
  }
}

run "all_task_and_execution_deny_boundaries_are_complete" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition     = toset([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s.Sid if s.Effect == "Deny"]) == toset(["RejectAllOtherActions", "RejectOtherRegions", "RejectOtherSecrets", "RejectInsecureConfigurationReads", "RejectOtherSecretVersions", "RejectOtherDecryptKeys", "RejectDirectConfigurationDecrypt", "RejectOtherCallerAccounts", "RejectOtherSecretContexts", "RejectOtherVersionContexts"]) && toset([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s.Sid if s.Effect == "Deny"]) == toset(["RejectAllOtherActions", "RejectOtherRegions", "RejectOtherRepositories", "RejectOtherLogStreams", "RejectOtherCryptoKeys", "RejectDirectLogKeyUse", "RejectOtherCallerAccounts", "RejectOtherLogContexts"])
    error_message = "No deny may disappear when extra identity/session grants are considered."
  }
  assert {
    condition     = alltrue([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s.Resource == "*" if s.Effect == "Deny" && !contains(["RejectOtherSecrets", "RejectOtherDecryptKeys"], s.Sid)]) && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectDirectConfigurationDecrypt"]).Condition.StringNotEqualsIfExists["kms:ViaService"] == "secretsmanager.us-east-1.amazonaws.com" && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherCallerAccounts"]).Condition.StringNotEqualsIfExists["kms:CallerAccount"] == var.account_id && one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectOtherSecretContexts"]).Condition.StringNotEqualsIfExists["kms:EncryptionContext:SecretARN"] == output.configuration.arn && one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectDirectLogKeyUse"]).Condition.StringNotEqualsIfExists["kms:ViaService"] == "logs.us-east-1.amazonaws.com"
    error_message = "Missing/wrong service/account/context must explicitly deny, not merely lack an Allow."
  }
}
run "configuration_key_denies_every_nonreviewed_reader_service_account_slot" {
  command = plan
  assert {
    condition     = toset(one([for s in jsondecode(aws_kms_key.configuration.policy).Statement : s if s.Sid == "ConfigurationPrincipalsOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(["arn:aws:iam::111122223333:root", "arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:role/SaveITSecretCustodian", output.roles.task_arn]) && one([for s in jsondecode(aws_kms_key.configuration.policy).Statement : s if s.Sid == "SecretsManagerOnly"]).Condition.StringNotEqualsIfExists["kms:ViaService"] == "secretsmanager.us-east-1.amazonaws.com" && one([for s in jsondecode(aws_kms_key.configuration.policy).Statement : s if s.Sid == "CallerAccountOnly"]).Condition.StringNotEqualsIfExists["kms:CallerAccount"] == var.account_id
    error_message = "Configuration key excludes execution/web/legacy roles and rejects missing/wrong service/account."
  }
}
run "same_config_and_log_key_fixture_is_refused" {
  command = plan
  override_resource {
    override_during = plan
    target          = aws_kms_key.logs
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000001", key_id = "30000000-0000-0000-0000-000000000001" }
  }
  expect_failures = [aws_iam_policy.task_boundary, aws_iam_policy.execution_boundary]
}
run "wrong_account_refused" {
  command = plan
  variables { account_id = "11112222333*" }
  expect_failures = [var.account_id]
}
run "region_payload_refused" {
  command = plan
  variables { region = "us-east-1:*" }
  expect_failures = [var.region]
}
run "cell_path_refused" {
  command = plan
  variables { cell = "pilot/other" }
  expect_failures = [var.cell]
}
run "development_refused" {
  command = plan
  variables { environment = "development" }
  expect_failures = [var.environment]
}
run "mutable_current_stage_refused" {
  command = plan
  variables { configuration_version_id = "AWSCURRENT" }
  expect_failures = [var.configuration_version_id]
}
run "legacy_web_admin_refused" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task"] }
  expect_failures = [var.key_administrators]
}
run "relay_execution_custodian_refused" {
  command = plan
  variables { secret_custodians = ["arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution"] }
  expect_failures = [var.secret_custodians]
}
run "case_variant_workload_admin_refused" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/SAVEIT-PILOT-STAGING-RELAY-TASK"] }
  expect_failures = [var.key_administrators]
}
run "foreign_custodian_refused" {
  command = plan
  variables { secret_custodians = ["arn:aws:iam::999900001111:role/SaveITSecretCustodian"] }
  expect_failures = [var.secret_custodians]
}
run "wrong_repository_refused" {
  command = plan
  variables { image_repository = merge(run.bound_exact_declared_store_contract.image_repository, { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api" }) }
  expect_failures = [var.image_repository]
}
run "wrong_registry_url_refused" {
  command = plan
  variables { image_repository = merge(run.bound_exact_declared_store_contract.image_repository, { url = "111122223333.dkr.ecr.us-west-2.amazonaws.com/saveit/pilot/staging/deployment" }) }
  expect_failures = [var.image_repository]
}
run "missing_relay_reader_refused" {
  command = plan
  variables { image_repository = merge(run.bound_exact_declared_store_contract.image_repository, { pull_execution_roles = ["arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution", "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"] }) }
  expect_failures = [var.image_repository]
}
run "duplicate_relay_reader_refused" {
  command = plan
  variables { image_repository = merge(run.bound_exact_declared_store_contract.image_repository, { pull_execution_roles = concat(run.bound_exact_declared_store_contract.image_repository.pull_execution_roles, ["arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution"]) }) }
  expect_failures = [var.image_repository]
}
run "null_readers_refused" {
  command = plan
  variables { image_repository = merge(run.bound_exact_declared_store_contract.image_repository, { pull_execution_roles = null }) }
  expect_failures = [var.image_repository]
}
run "invalid_retention_refused" {
  command = plan
  variables { log_retention_days = 0 }
  expect_failures = [var.log_retention_days]
}

run "maximum_trusted_inputs_fit_all_iam_and_resource_policies" {
  command = plan
  variables {
    key_administrators = [for i in range(8) : "arn:aws:iam::111122223333:role/${join("", [for segment in range(210) : "a"])}/Administrator${i}"]
    secret_custodians  = [for i in range(8) : "arn:aws:iam::111122223333:role/${join("", [for segment in range(214) : "c"])}/Custodian${i}"]
    image_repository   = run.bound_exact_declared_store_contract.image_repository
  }
  assert {
    condition     = length(aws_iam_policy.task_boundary.policy) <= 6144 && length(aws_iam_policy.execution_boundary.policy) <= 6144 && length(aws_iam_role_policy.task[0].policy) <= 10240 && length(aws_iam_role_policy.execution[0].policy) <= 10240 && length(aws_kms_key.configuration.policy) <= 32768 && length(aws_kms_key.logs.policy) <= 32768 && length(aws_secretsmanager_secret_policy.configuration.policy) <= 20480
    error_message = "Largest bounded trusted-role sets must fit service limits without truncating explicit denies."
  }
  assert {
    condition     = alltrue([for arn in setunion(var.key_administrators, var.secret_custodians) : length(arn) == 256])
    error_message = "The maximum-size fixture must actually exercise the accepted ARN bound."
  }
}
run "exact_runtime_configuration_field_names_are_metadata_only" {
  command = plan
  assert {
    condition     = toset(output.configuration.fields) == toset(["CertificateBase64", "CertificatePassword", "ApiCaSha256", "ClientLeafSha256", "ServerCertificateBase64", "ServerCertificatePassword", "ServerLeafSha256"]) && length(output.configuration.fields) == 7 && output.configuration.version_id == var.configuration_version_id
    error_message = "Expose exact seven field names and pinned metadata, not selectors, values or an implemented loader."
  }
}

# Independently reviewed synthetic policy contract. Do not regenerate this file
# from planned policies: a weakened production statement must fail equality.
run "complete_policy_contract_includes_every_statement_attribute" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition = alltrue([
      jsondecode(aws_iam_role.task.assume_role_policy) == jsondecode(file("tests/policies.json")).trust,
      jsondecode(aws_iam_role.execution.assume_role_policy) == jsondecode(file("tests/policies.json")).trust,
      jsondecode(aws_iam_role_policy.task[0].policy) == jsondecode(file("tests/policies.json")).task,
      jsondecode(aws_iam_policy.task_boundary.policy) == jsondecode(file("tests/policies.json")).task_boundary,
      jsondecode(aws_iam_role_policy.execution[0].policy) == jsondecode(file("tests/policies.json")).execution,
      jsondecode(aws_iam_policy.execution_boundary.policy) == jsondecode(file("tests/policies.json")).execution_boundary,
      jsondecode(aws_secretsmanager_secret_policy.configuration.policy) == jsondecode(file("tests/policies.json")).secret,
      jsondecode(aws_kms_key.configuration.policy) == jsondecode(file("tests/policies.json")).configuration_key,
      jsondecode(aws_kms_key.logs.policy) == jsondecode(file("tests/policies.json")).log_key
    ])
    error_message = "Exact full policies must retain effects/actions/resources/principals/conditions and every deny, without extra exemptions or grants."
  }
}

# Narrow condition predicates, NOT an IAM simulator. The complete contract above
# separately couples these predicates to exact actions, resources and Deny effects.
run "each_task_condition_rejects_missing_and_wrong_context" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition = alltrue(flatten([for sample in [
      { sid = "RejectOtherRegions", key = "aws:RequestedRegion", good = "us-east-1", bad = "us-west-2" },
      { sid = "RejectOtherSecretVersions", key = "secretsmanager:VersionId", good = "10000000-0000-0000-0000-000000000009", bad = "AWSCURRENT" },
      { sid = "RejectDirectConfigurationDecrypt", key = "kms:ViaService", good = "secretsmanager.us-east-1.amazonaws.com", bad = "logs.us-east-1.amazonaws.com" },
      { sid = "RejectOtherCallerAccounts", key = "kms:CallerAccount", good = "111122223333", bad = "999900001111" },
      { sid = "RejectOtherSecretContexts", key = "kms:EncryptionContext:SecretARN", good = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/relay/config-Abcd90", bad = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd90" },
      { sid = "RejectOtherVersionContexts", key = "kms:EncryptionContext:SecretVersionId", good = "10000000-0000-0000-0000-000000000009", bad = "RequestToValidateKeyAccess" }
      ] : [for context in [{ value = sample.good, denied = false }, { value = sample.bad, denied = true }, { value = null, denied = true }] :
      (context.value != one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == sample.sid]).Condition.StringNotEqualsIfExists[sample.key]) == context.denied
    ]]))
    error_message = "Every task region/service/account/secret/version condition independently rejects wrong or absent context."
  }
  assert {
    condition = alltrue([for context in [{ value = "true", denied = false }, { value = "false", denied = true }, { value = null, denied = true }] :
      (context.value == null || context.value == one([for s in jsondecode(aws_iam_policy.task_boundary.policy).Statement : s if s.Sid == "RejectInsecureConfigurationReads"]).Condition.BoolIfExists["aws:SecureTransport"]) == context.denied
    ])
    error_message = "Missing/false TLS context is explicitly refused."
  }
}

run "each_execution_condition_rejects_missing_and_wrong_context" {
  command = plan
  variables { image_repository = run.bound_exact_declared_store_contract.image_repository }
  assert {
    condition = alltrue(flatten([for sample in [
      { sid = "RejectOtherRegions", key = "aws:RequestedRegion", good = "us-east-1", bad = "us-west-2" },
      { sid = "RejectDirectLogKeyUse", key = "kms:ViaService", good = "logs.us-east-1.amazonaws.com", bad = "secretsmanager.us-east-1.amazonaws.com" },
      { sid = "RejectOtherCallerAccounts", key = "kms:CallerAccount", good = "111122223333", bad = "999900001111" }
      ] : [for context in [{ value = sample.good, denied = false }, { value = sample.bad, denied = true }, { value = null, denied = true }] :
      (context.value != one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == sample.sid]).Condition.StringNotEqualsIfExists[sample.key]) == context.denied
    ]]))
    error_message = "Execution independently refuses missing/wrong region, service and caller account."
  }
  assert {
    condition = alltrue([for context in [
      { group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay", denied = false },
      { group = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api", denied = true },
      { group = null, denied = true }
      ] : (context.group != one([for s in jsondecode(aws_iam_policy.execution_boundary.policy).Statement : s if s.Sid == "RejectOtherLogContexts"]).Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"]) == context.denied
    ])
    error_message = "Log crypto rejects other/missing group context; separate DescribeKey intentionally has no encryption context."
  }
}
