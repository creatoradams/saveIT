mock_provider "aws" { override_during = plan }

run "bounded_nonroot_tasks_and_exact_wiring" {
  command = plan
  assert {
    condition = toset(keys(aws_ecs_task_definition.application)) == toset(["api", "web", "worker"]) && alltrue([for name, d in aws_ecs_task_definition.application :
      d.network_mode == "awsvpc" && d.requires_compatibilities == toset(["FARGATE"]) &&
      one(d.runtime_platform).operating_system_family == "LINUX" && one(d.runtime_platform).cpu_architecture == "X86_64" &&
      d.cpu == (name == "worker" ? "256" : "512") && d.memory == "1024" &&
      d.task_role_arn == var.identity.workloads[name].task_role_arn && d.execution_role_arn == var.identity.workloads[name].execution_role_arn &&
      one(jsondecode(d.container_definitions)).image == var.images[name] &&
      one(jsondecode(d.container_definitions)).user == "1654:1654" && one(jsondecode(d.container_definitions)).readonlyRootFilesystem &&
      !one(jsondecode(d.container_definitions)).privileged && !one(jsondecode(d.container_definitions)).interactive &&
      !one(jsondecode(d.container_definitions)).pseudoTerminal && one(jsondecode(d.container_definitions)).stopTimeout == 30 &&
      one(jsondecode(d.container_definitions)).linuxParameters.capabilities.drop == ["ALL"] &&
      one(jsondecode(d.container_definitions)).secrets == [for s in var.identity.workloads[name].secrets : { name = s.name, valueFrom = s.valueFrom }] &&
      one(jsondecode(d.container_definitions)).healthCheck == { command = local.health[name], interval = 10, timeout = 5, retries = 3, startPeriod = 30 } &&
      one(jsondecode(d.container_definitions)).logConfiguration == {
        logDriver = "awslogs", options = { awslogs-group = "/saveit/pilot/staging/${name}", awslogs-region = "us-east-1", awslogs-stream-prefix = name, awslogs-create-group = "false", mode = "non-blocking", max-buffer-size = "4m" }
      }
    ])
    error_message = "Three digest-pinned nonroot read-only Fargate tasks must retain exact identities, secret selectors, bounded health/stop/resource settings and bounded nonblocking logs."
  }
  assert {
    condition = alltrue([for name, d in aws_ecs_task_definition.application :
      length(d.volume) == (name == "api" ? 2 : 1) &&
      length([for v in d.volume : v if v.name == "runtime" && length(v.efs_volume_configuration) == 0 && v.host_path == null]) == 1 &&
      one(jsondecode(d.container_definitions)).mountPoints == concat([{ sourceVolume = "runtime", containerPath = "/var/saveit/runtime", readOnly = false }], name == "api" ? [{ sourceVolume = "session-keys", containerPath = "/var/saveit/keys", readOnly = false }] : [])
      ]) && jsonencode(one([for v in aws_ecs_task_definition.application["api"].volume : one(v.efs_volume_configuration) if v.name == "session-keys"])) == jsonencode({
      file_system_id       = "fs-00000000000000001", root_directory = "/", transit_encryption = "ENABLED", transit_encryption_port = null,
      authorization_config = [{ access_point_id = "fsap-00000000000000001", iam = "ENABLED" }]
    })
    error_message = "Only API gets the exact IAM/TLS access-point mount; all tasks have only their own unhosted scratch volume."
  }
  assert {
    condition = (
      { for e in one(jsondecode(aws_ecs_task_definition.application["api"].container_definitions)).environment : e.name => e.value } == local.environment.api &&
      local.environment.api.DevelopmentIdentity__Enabled == "false" && local.environment.api.Identity__Okta__Enabled == "true" &&
      local.environment.api.Identity__KeyDirectory == "/var/saveit/keys" &&
      local.environment.api.Deployment__ApiTls__Enabled == "true" &&
      local.environment.api.Deployment__Trust__DatabaseCaSha256 == var.public_trust.database_ca_sha256 &&
      local.environment.worker.Deployment__Trust__DatabaseCaSha256 == var.public_trust.database_ca_sha256 &&
      local.environment.web.SAVEIT_API_CA_SHA256 == var.public_trust.api_ca_sha256 &&
      local.environment.web.NODE_EXTRA_CA_CERTS == "/opt/saveit/trust/api-ca.crt" &&
      local.environment.web.SAVEIT_API_ORIGIN == "https://api.pilot.staging.saveit.internal:8443" &&
      local.environment.worker.SearchWorker__HealthPath == "/var/saveit/runtime/saveit-worker-health.json" &&
      local.environment.worker.SearchWorker__Tenants__0 == var.runtime.worker_tenants[0] &&
      one(jsondecode(aws_ecs_task_definition.application["worker"].container_definitions)).portMappings == [] &&
      one(one(jsondecode(aws_ecs_task_definition.application["api"].container_definitions)).portMappings).containerPort == 8443 &&
      one(one(jsondecode(aws_ecs_task_definition.application["web"].container_definitions)).portMappings).containerPort == 3000
    )
    error_message = "Production identity and server-configured tenant metadata must remain fixed; workers expose no port or extra configuration path."
  }
}
run "maximum_task_transport_tenants_fit_budget" {
  command = plan
  variables {
    runtime = merge(var.runtime, { worker_tenants = [for i in range(100) : format("30000000-0000-0000-0000-%012d", i + 1)] })
  }
  assert {
    condition = (length(aws_ecs_task_definition.application["worker"].container_definitions) < 16000 &&
    length(one(jsondecode(aws_ecs_task_definition.application["worker"].container_definitions)).environment) == 105)
    error_message = "The largest accepted task tenant list must fit comfortably below the guarded container JSON budget."
  }
}
run "all_four_retired_pairs_remain_pinned_and_bounded" {
  command = plan
  variables {
    identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, {
      secrets = concat(var.identity.workloads.api.secrets, flatten([for i in range(4) : [for field in ["Base64", "Password"] : {
        name      = "Identity__PreviousKeyCertificates__${i}__${field}"
        valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Identity__PreviousKeyCertificates__${i}__${field}::10000000-0000-0000-0000-000000000001"
      }]]))
    }) }) })
  }
  assert {
    condition     = length(one(jsondecode(aws_ecs_task_definition.application["api"].container_definitions)).secrets) == 14 && length(aws_ecs_task_definition.application["api"].container_definitions) < 16000
    error_message = "Current plus all four contiguous retired PFX/password pairs must fit the task transport and retain one exact version."
  }
}
run "unbound_efs" {
  command = plan
  variables { identity = merge(var.identity, { session_key_binding_ready = false }) }
  expect_failures = [var.identity]
}
run "missing_transport_certificate_field" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = [for s in var.identity.workloads.api.secrets : s if s.name != "Deployment__ApiTls__CertificateBase64"] }) }) }) }
  expect_failures = [var.identity]
}
run "transport_certificate_version_mismatch" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = [for s in var.identity.workloads.api.secrets : s.name == "Deployment__ApiTls__CertificatePassword" ? merge(s, { valueFrom = replace(s.valueFrom, "10000000-0000-0000-0000-000000000001", "10000000-0000-0000-0000-000000000002") }) : s] }) }) }) }
  expect_failures = [var.identity]
}
run "unapproved_transport_file_selector" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = [for s in var.identity.workloads.api.secrets : { name = replace(s.name, "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePath"), valueFrom = replace(s.valueFrom, "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePath") }] }) }) }) }
  expect_failures = [var.identity]
}
run "public_trust_never_accepts_pem_payloads" {
  command = plan
  variables { public_trust = { api_ca_sha256 = "-----BEGIN CERTIFICATE-----", database_ca_sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } }
  expect_failures = [var.public_trust]
}
run "public_trust_requires_canonical_hashes" {
  command = plan
  variables { public_trust = { api_ca_sha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", database_ca_sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } }
  expect_failures = [var.public_trust]
}
run "unbound_logs" {
  command = plan
  variables { identity = merge(var.identity, { log_key_binding_ready = false }) }
  expect_failures = [var.identity]
}
run "null_log_binding" {
  command = plan
  variables { identity = merge(var.identity, { operational_log_keys = null }) }
  expect_failures = [var.identity]
}
run "different_identity_access_point" {
  command = plan
  variables { identity = merge(var.identity, { session_key_binding = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000002" } }) }
  expect_failures = [var.identity]
}
run "duplicate_log_key" {
  command = plan
  variables { identity = merge(var.identity, { operational_log_keys = merge(var.identity.operational_log_keys, { web = var.identity.operational_log_keys.api }) }) }
  expect_failures = [var.identity]
}
run "cross_account_role" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { task_role_arn = "arn:aws:iam::999900001111:role/saveit-pilot-staging-api-task" }) }) }) }
  expect_failures = [var.identity]
}
run "cross_workload_repository" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { repository_arn = var.identity.workloads.web.repository_arn }) }) }) }
  expect_failures = [var.identity]
}
run "web_secret" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { web = merge(var.identity.workloads.web, { secrets = var.identity.workloads.worker.secrets }) }) }) }
  expect_failures = [var.identity]
}
run "duplicate_api_field" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = concat(var.identity.workloads.api.secrets, [var.identity.workloads.api.secrets[0]]) }) }) }) }
  expect_failures = [var.identity]
}
run "selector_stage" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { worker = merge(var.identity.workloads.worker, { secrets = [{ name = "ConnectionStrings__SearchWorker", valueFrom = replace(var.identity.workloads.worker.secrets[0].valueFrom, "::", ":AWSCURRENT:") }] }) }) }) }
  expect_failures = [var.identity]
}
run "mixed_api_versions" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = concat(slice(var.identity.workloads.api.secrets, 0, 3), [{ name = "Identity__KeyCertificatePassword", valueFrom = replace(var.identity.workloads.api.secrets[3].valueFrom, "10000000-0000-0000-0000-000000000001", "10000000-0000-0000-0000-000000000002") }]) }) }) }) }
  expect_failures = [var.identity]
}
run "retired_pair_gap" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { secrets = concat(var.identity.workloads.api.secrets, [{ name = "Identity__PreviousKeyCertificates__1__Base64", valueFrom = replace(var.identity.workloads.api.secrets[2].valueFrom, "Identity__KeyCertificateBase64", "Identity__PreviousKeyCertificates__1__Base64") }]) }) }) }) }
  expect_failures = [var.identity]
}
run "mutable_image_tag" {
  command = plan
  variables { images = merge(var.images, { api = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/api:latest" }) }
  expect_failures = [var.images]
}
run "wrong_image_repository" {
  command = plan
  variables { images = merge(var.images, { api = var.images.web }) }
  expect_failures = [var.images]
}
run "disable_efs_tls" {
  command = plan
  variables { session_key_storage = merge(var.session_key_storage, { task_volume = merge(var.session_key_storage.task_volume, { transitEncryption = "DISABLED" }) }) }
  expect_failures = [var.session_key_storage]
}
run "root_efs_user" {
  command = plan
  variables { session_key_storage = merge(var.session_key_storage, { container_uid = 0 }) }
  expect_failures = [var.session_key_storage]
}
run "insecure_origin" {
  command = plan
  variables { runtime = merge(var.runtime, { public_origin = "http://saveit.example.com" }) }
  expect_failures = [var.runtime]
}
run "duplicate_tenants" {
  command = plan
  variables { runtime = merge(var.runtime, { worker_tenants = concat(var.runtime.worker_tenants, var.runtime.worker_tenants) }) }
  expect_failures = [var.runtime]
}
run "oversized_task_tenants" {
  command = plan
  variables { runtime = merge(var.runtime, { worker_tenants = [for i in range(101) : format("30000000-0000-0000-0000-%012d", i + 1)] }) }
  expect_failures = [var.runtime]
}
run "wrong_role_boundary" {
  command = plan
  override_data {
    target = data.aws_iam_role.workload["web-task"]
    values = {
      arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task"
      path                 = "/"
      permissions_boundary = "arn:aws:iam::111122223333:policy/Other"
      assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
    }
  }
  expect_failures = [data.aws_iam_role.workload["web-task"]]
}
run "wrong_role_trust" {
  command = plan
  override_data {
    target = data.aws_iam_role.workload["web-task"]
    values = {
      arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task"
      path                 = "/"
      permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-web-task-boundary"
      assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[]}"
    }
  }
  expect_failures = [data.aws_iam_role.workload["web-task"]]
}
run "log_key_differs_from_identity_binding" {
  command = plan
  override_data {
    target = data.aws_cloudwatch_log_group.workload["api"]
    values = {
      arn               = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api"
      kms_key_id        = "arn:aws:kms:us-east-1:111122223333:key/90000000-0000-0000-0000-000000000001"
      retention_in_days = 90
    }
  }
  expect_failures = [data.aws_cloudwatch_log_group.workload["api"]]
}
run "unencrypted_filesystem" {
  command = plan
  override_data {
    target = data.aws_efs_file_system.ring
    values = {
      arn       = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001"
      encrypted = false
      tags      = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
    }
  }
  expect_failures = [data.aws_efs_file_system.ring]
}
run "wrong_access_point_owner" {
  command = plan
  override_data {
    target = data.aws_efs_access_point.ring
    values = {
      arn            = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
      file_system_id = "fs-00000000000000001"
      posix_user     = [{ uid = 0, gid = 0, secondary_gids = [] }]
      root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
      tags           = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
    }
  }
  expect_failures = [data.aws_efs_access_point.ring]
}
override_data {
  target = data.aws_iam_role.workload["api-task"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-api-task-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_iam_role.workload["api-execution"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-api-execution-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_ecr_repository.image["api"]
  values = {
    arn                      = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api"
    repository_url           = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/api"
    image_tag_mutability     = "IMMUTABLE"
    encryption_configuration = [{ encryption_type = "KMS", kms_key = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000000" }]
  }
}
override_data {
  target = data.aws_cloudwatch_log_group.workload["api"]
  values = {
    arn               = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api"
    kms_key_id        = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001"
    retention_in_days = 90
  }
}
override_data {
  target = data.aws_iam_role.workload["web-task"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-web-task-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_iam_role.workload["web-execution"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-web-execution-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_ecr_repository.image["web"]
  values = {
    arn                      = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web"
    repository_url           = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/web"
    image_tag_mutability     = "IMMUTABLE"
    encryption_configuration = [{ encryption_type = "KMS", kms_key = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000000" }]
  }
}
override_data {
  target = data.aws_cloudwatch_log_group.workload["web"]
  values = {
    arn               = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/web"
    kms_key_id        = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002"
    retention_in_days = 90
  }
}
override_data {
  target = data.aws_iam_role.workload["worker-task"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-task"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-worker-task-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_iam_role.workload["worker-execution"]
  values = {
    arn                  = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution"
    path                 = "/"
    permissions_boundary = "arn:aws:iam::111122223333:policy/saveit-pilot-staging-worker-execution-boundary"
    assume_role_policy   = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyAccountRegionalECSTasks\",\"Effect\":\"Allow\",\"Action\":\"sts:AssumeRole\",\"Principal\":{\"Service\":\"ecs-tasks.amazonaws.com\"},\"Condition\":{\"StringEquals\":{\"aws:SourceAccount\":\"111122223333\"},\"ArnLike\":{\"aws:SourceArn\":\"arn:aws:ecs:us-east-1:111122223333:*\"}}}]}"
  }
}
override_data {
  target = data.aws_ecr_repository.image["worker"]
  values = {
    arn                      = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/worker"
    repository_url           = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/worker"
    image_tag_mutability     = "IMMUTABLE"
    encryption_configuration = [{ encryption_type = "KMS", kms_key = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000000" }]
  }
}
override_data {
  target = data.aws_cloudwatch_log_group.workload["worker"]
  values = {
    arn               = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker"
    kms_key_id        = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003"
    retention_in_days = 90
  }
}
override_data {
  target = data.aws_efs_file_system.ring
  values = {
    arn       = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001"
    encrypted = true
    tags      = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
  }
}
override_data {
  target = data.aws_efs_access_point.ring
  values = {
    arn            = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001"
    file_system_id = "fs-00000000000000001"
    posix_user     = [{ uid = 1654, gid = 1654, secondary_gids = [] }]
    root_directory = [{ path = "/saveit-session-keys", creation_info = [{ owner_uid = 1654, owner_gid = 1654, permissions = "0700" }] }]
    tags           = { Project = "SaveIT", Cell = "pilot", Environment = "staging" }
  }
}
variables {
  public_trust = { api_ca_sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", database_ca_sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
  account_id   = "111122223333"
  region       = "us-east-1"
  cell         = "pilot"
  environment  = "staging"
  identity = {
    session_key_binding_ready = true
    log_key_binding_ready     = true
    session_key_binding       = { file_system_id = "fs-00000000000000001", access_point_id = "fsap-00000000000000001" }
    operational_log_keys = {
      api       = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000001"
      web       = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000002"
      worker    = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000003"
      migration = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000004"
      access    = "arn:aws:kms:us-east-1:111122223333:key/20000000-0000-0000-0000-000000000005"
    }
    workloads = {
      api = {
        task_role_arn      = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task"
        execution_role_arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution"
        repository_arn     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api"
        log_group_arn      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api"
        secrets = [{ name = "ConnectionStrings__App", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:ConnectionStrings__App::10000000-0000-0000-0000-000000000001" },
          { name = "Identity__Okta__ClientSecret", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Identity__Okta__ClientSecret::10000000-0000-0000-0000-000000000001" },
          { name = "Identity__KeyCertificateBase64", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Identity__KeyCertificateBase64::10000000-0000-0000-0000-000000000001" },
          { name = "Identity__KeyCertificatePassword", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Identity__KeyCertificatePassword::10000000-0000-0000-0000-000000000001" },
          { name = "Deployment__ApiTls__CertificateBase64", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Deployment__ApiTls__CertificateBase64::10000000-0000-0000-0000-000000000001" },
        { name = "Deployment__ApiTls__CertificatePassword", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:Deployment__ApiTls__CertificatePassword::10000000-0000-0000-0000-000000000001" }]
      }
      web = {
        task_role_arn      = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task"
        execution_role_arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution"
        repository_arn     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web"
        log_group_arn      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/web"
        secrets            = []
      }
      worker = {
        task_role_arn      = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-task"
        execution_role_arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution"
        repository_arn     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/worker"
        log_group_arn      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker"
        secrets            = [{ name = "ConnectionStrings__SearchWorker", valueFrom = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd00:ConnectionStrings__SearchWorker::10000000-0000-0000-0000-000000000001" }]
      }
    }
  }
  images = {
    api    = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/api@sha256:1111111111111111111111111111111111111111111111111111111111111111"
    web    = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/web@sha256:2222222222222222222222222222222222222222222222222222222222222222"
    worker = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/worker@sha256:3333333333333333333333333333333333333333333333333333333333333333"
  }
  session_key_storage = {
    file_system_id = "fs-00000000000000001"
    access_point   = "fsap-00000000000000001"
    container_path = "/var/saveit/keys"
    container_uid  = 1654
    task_volume = {
      fileSystemId        = "fs-00000000000000001"
      rootDirectory       = "/"
      transitEncryption   = "ENABLED"
      authorizationConfig = { accessPointId = "fsap-00000000000000001", iam = "ENABLED" }
    }
  }
  runtime = {
    public_origin  = "https://saveit.example.com"
    okta_issuer    = "https://synthetic.example.okta.com/oauth2/default"
    okta_client_id = "synthetic-client"
    okta_tenant_id = "30000000-0000-0000-0000-000000000001"
    worker_tenants = ["30000000-0000-0000-0000-000000000001"]
  }
}
