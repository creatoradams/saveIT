mock_provider "aws" { override_during = plan }

variables {
  account_id         = "111122223333"
  region             = "us-east-1"
  cell               = "pilot"
  environment        = "staging"
  key_administrators = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
}
run "store_reference_contract" {
  command = plan
  variables { publisher_role = "arn:aws:iam::111122223333:role/SaveITProtectedPublisher" }
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

}
run "existing_identity_module_uses_exact_store_references" {
  command = plan
  module { source = "../workload-identity" }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["api"]
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000001", key_id = "10000000-0000-0000-0000-000000000001" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["api"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00" }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["worker"]
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000002", key_id = "10000000-0000-0000-0000-000000000002" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["worker"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd01" }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["migration"]
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000003", key_id = "10000000-0000-0000-0000-000000000003" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["migration"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/migration/config-Abcd02" }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["access"]
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000004", key_id = "10000000-0000-0000-0000-000000000004" }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["access"]
    values          = { arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/access/config-Abcd03" }
  }

  variables {
    secret_custodians    = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    operational_log_keys = { for name, group in run.store_reference_contract.log_groups : name => group.key_arn }
    configuration_versions = {
      api       = "10000000-0000-0000-0000-000000000001"
      worker    = "10000000-0000-0000-0000-000000000002"
      migration = "10000000-0000-0000-0000-000000000003"
      access    = "10000000-0000-0000-0000-000000000004"
    }
  }
  assert {
    condition = alltrue([for name in ["api", "web", "worker", "migration", "access"] :
      output.workloads[name].repository_arn == run.store_reference_contract.repositories[contains(["migration", "access"], name) ? "deployment" : name].arn &&
      output.workloads[name].log_group_arn == run.store_reference_contract.log_groups[name].arn &&
      jsondecode(aws_iam_role_policy.execution[name].policy).Statement[1].Resource == output.workloads[name].repository_arn &&
      jsondecode(aws_iam_role_policy.execution[name].policy).Statement[2].Resource == format("%s:log-stream:*", run.store_reference_contract.log_groups[name].arn) &&
      format("arn:aws:iam::111122223333:role/%s", aws_iam_role.execution[name].name) == run.store_reference_contract.log_groups[name].execution_role_arn &&
      contains(run.store_reference_contract.repositories[contains(["migration", "access"], name) ? "deployment" : name].pull_execution_roles, format("arn:aws:iam::111122223333:role/%s", aws_iam_role.execution[name].name))
    ])
    error_message = "Plan the real existing identity module: its exact image/log grants and role names must match the new store resources, including shared deployment image and separate job logs."
  }
  assert {
    condition = output.log_key_binding_ready && alltrue([for name in ["api", "web", "worker", "migration", "access"] :
      one([for statement in jsondecode(aws_iam_role_policy.execution[name].policy).Statement : statement if statement.Sid == "OwnEncryptedLogData"]) == {
        Sid       = "OwnEncryptedLogData", Effect = "Allow", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = run.store_reference_contract.log_groups[name].key_arn,
        Condition = { StringEquals = { "kms:ViaService" = "logs.us-east-1.amazonaws.com" }, ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = run.store_reference_contract.log_groups[name].arn } }
      } &&
      one([for statement in jsondecode(aws_iam_role_policy.execution[name].policy).Statement : statement if statement.Sid == "OwnLogKeyDescription"]) == {
        Sid = "OwnLogKeyDescription", Effect = "Allow", Action = "kms:DescribeKey", Resource = run.store_reference_contract.log_groups[name].key_arn, Condition = { StringEquals = { "kms:ViaService" = "logs.us-east-1.amazonaws.com" } }
      } &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[7].Resource == run.store_reference_contract.log_groups[name].key_arn &&
      jsondecode(aws_iam_policy.execution_boundary[name].policy).Statement[8].Condition.ArnNotEqualsIfExists["kms:EncryptionContext:aws:logs:arn"] == run.store_reference_contract.log_groups[name].arn
    ])
    error_message = "The actual identity module must grant own-log crypto and context-free exact-key description through Logs, with matching bound key/context constraints. Managed ingestion is still untested."
  }
}
