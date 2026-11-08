mock_provider "aws" { override_during = plan }
override_resource {
  override_during = plan
  target          = aws_kms_key.configuration
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000001", key_id = "30000000-0000-0000-0000-000000000001" }
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
}
run "bootstrap_exact_role_before_store_binding" {
  command = plan
  override_resource {
    override_during = plan
    target          = aws_kms_key.logs
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000002", key_id = "30000000-0000-0000-0000-000000000002" }
  }
  variables {
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"
  }
  assert {
    condition     = !output.binding_ready && output.roles.execution_arn == "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution"
    error_message = "Exact role reference is available without usable bootstrap authority."
  }
}
run "actual_store_owns_optional_deployment_reader_binding" {
  command = plan
  module { source = "../artifact-stores" }
  variables {
    publisher_role           = "arn:aws:iam::111122223333:role/SaveITProtectedPublisher"
    relay_execution_role_arn = run.bootstrap_exact_role_before_store_binding.roles.execution_arn
  }
  assert {
    condition     = length(aws_ecr_repository_policy.images) == 4 && length(aws_ecr_repository.images) == 4 && length(aws_cloudwatch_log_group.workloads) == 5 && contains(output.repositories.deployment.pull_execution_roles, run.bootstrap_exact_role_before_store_binding.roles.execution_arn) && contains(one([for s in jsondecode(aws_ecr_repository_policy.images["deployment"].policy).Statement : s if s.Sid == "ExactImageReaders"]).Condition.ArnNotEquals["aws:PrincipalArn"], run.bootstrap_exact_role_before_store_binding.roles.execution_arn)
    error_message = "Single actual store-policy owner changes only the declared deployment pull reader contract."
  }
}
run "relay_consumes_actual_store_output_after_bootstrap" {
  command = plan
  override_resource {
    override_during = plan
    target          = aws_kms_key.logs
    values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/30000000-0000-0000-0000-000000000002", key_id = "30000000-0000-0000-0000-000000000002" }
  }
  variables {
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"
    image_repository         = run.actual_store_owns_optional_deployment_reader_binding.repositories.deployment
  }
  assert {
    condition     = output.binding_ready && output.image_repository.arn == run.actual_store_owns_optional_deployment_reader_binding.repositories.deployment.arn && contains(output.image_repository.pull_execution_roles, output.roles.execution_arn) && length(aws_iam_role_policy.execution) == 1
    error_message = "Bound role consumes actual adjacent-root declared output, not an invented policy-admission claim."
  }
}
run "actual_default_store_has_no_relay_admission" {
  command = plan
  module { source = "../artifact-stores" }
  variables { publisher_role = "arn:aws:iam::111122223333:role/SaveITProtectedPublisher" }
  assert {
    condition     = !contains(output.repositories.deployment.pull_execution_roles, run.bootstrap_exact_role_before_store_binding.roles.execution_arn)
    error_message = "Null store binding retains legacy exact readers."
  }
}
run "actual_unbound_store_cannot_enable_relay" {
  command = plan
  variables {
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"
    image_repository         = run.actual_default_store_has_no_relay_admission.repositories.deployment
  }
  expect_failures = [var.image_repository]
}
run "tampered_actual_repository_output_refused" {
  command = plan
  variables {
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"
    image_repository         = merge(run.actual_store_owns_optional_deployment_reader_binding.repositories.deployment, { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web" })
  }
  expect_failures = [var.image_repository]
}
