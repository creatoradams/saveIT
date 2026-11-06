mock_provider "aws" { override_during = plan }
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

override_data {
  override_during = plan
  target          = data.aws_vpc.cell
  values = {
    "id" : "vpc-00000000000000001",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
    "owner_id" : "111122223333",
    "default" : false,
    "enable_dns_support" : true,
    "enable_dns_hostnames" : true,
    "cidr_block" : "10.84.0.0/16",
    "cidr_block_associations" : [
      {
        "association_id" : "vpc-cidr-assoc-00000000000000001",
        "cidr_block" : "10.84.0.0/16",
        "state" : "associated"
      }
    ],
    "ipv6_cidr_block_associations" : [],
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_subnet.cell["application-0"]
  values = {
    "id" : "subnet-00000000000000001",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
    "vpc_id" : "vpc-00000000000000001",
    "availability_zone" : "us-east-1a",
    "map_public_ip_on_launch" : false,
    "ipv6_cidr_block" : "",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Tier" : "application"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_subnet.cell["application-1"]
  values = {
    "id" : "subnet-00000000000000002",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002",
    "vpc_id" : "vpc-00000000000000001",
    "availability_zone" : "us-east-1b",
    "map_public_ip_on_launch" : false,
    "ipv6_cidr_block" : "",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Tier" : "application"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["api"]
  values = {
    "id" : "sg-00000000000000002",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "api"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["worker"]
  values = {
    "id" : "sg-00000000000000003",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000003",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "worker"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["deployment"]
  values = {
    "id" : "sg-00000000000000004",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000004",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "deployment"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["web"]
  values = {
    "id" : "sg-00000000000000005",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000005",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "web"
    }
  }
}
override_resource {
  override_during = plan
  target          = aws_service_discovery_private_dns_namespace.cell
  values = {
    "id" : "ns-0000000000000001",
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
    "hosted_zone" : "Z0000000000000001"
  }
}
override_resource {
  override_during = plan
  target          = aws_service_discovery_service.api
  values = {
    "id" : "srv-0000000000000001",
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001"
  }
}


run "actual_bound_identity_contract" {
  command = plan
  module { source = "../workload-identity" }
  variables {
    key_administrators   = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    secret_custodians    = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    session_key_storage  = var.identity.session_key_binding
    operational_log_keys = var.identity.operational_log_keys
    configuration_versions = {
      api       = "10000000-0000-0000-0000-000000000001"
      worker    = "10000000-0000-0000-0000-000000000002"
      migration = "10000000-0000-0000-0000-000000000003"
      access    = "10000000-0000-0000-0000-000000000004"
    }
  }
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
  override_resource {
    override_during = plan
    target          = aws_iam_role.task["api"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["api"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.task["web"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["web"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.task["worker"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-task" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["worker"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.task["migration"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-task" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["migration"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.task["access"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-task" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["access"]
    values          = { arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution" }
  }
  assert {
    condition     = output.session_key_binding_ready && output.log_key_binding_ready && jsonencode(output.session_key_binding) == jsonencode(var.identity.session_key_binding) && jsonencode(output.operational_log_keys) == jsonencode(var.identity.operational_log_keys)
    error_message = "The actual identity module must export the concrete bound EFS/log references as well as readiness."
  }
}
run "tasks_consume_actual_identity_outputs" {
  command = plan
  variables {
    api_client_trust = { ca_sha256 = format("%064x", 192), leaf_sha256 = [format("%064x", 1), format("%064x", 2)] }
    identity = {
      workloads                 = run.actual_bound_identity_contract.workloads
      session_key_binding_ready = run.actual_bound_identity_contract.session_key_binding_ready
      log_key_binding_ready     = run.actual_bound_identity_contract.log_key_binding_ready
      session_key_binding       = run.actual_bound_identity_contract.session_key_binding
      operational_log_keys      = run.actual_bound_identity_contract.operational_log_keys
    }
  }
  assert {
    condition     = alltrue([for name, d in aws_ecs_task_definition.application : d.task_role_arn == run.actual_bound_identity_contract.workloads[name].task_role_arn && d.execution_role_arn == run.actual_bound_identity_contract.workloads[name].execution_role_arn && one(jsondecode(d.container_definitions)).secrets == [for s in run.actual_bound_identity_contract.workloads[name].secrets : { name = s.name, valueFrom = s.valueFrom }]])
    error_message = "Real alternate-module outputs must wire unchanged into task identities and exact pinned field selectors; mocks are not IAM/injection evidence."
  }
  assert {
    condition = ({ for e in one(jsondecode(aws_ecs_task_definition.application["api"].container_definitions)).environment : e.name => e.value if startswith(e.name, "Deployment__ApiTls__ClientAuthentication__") } == {
      Deployment__ApiTls__ClientAuthentication__Enabled       = "true"
      Deployment__ApiTls__ClientAuthentication__CaSha256      = format("%064x", 192)
      Deployment__ApiTls__ClientAuthentication__LeafSha256__0 = format("%064x", 1)
      Deployment__ApiTls__ClientAuthentication__LeafSha256__1 = format("%064x", 2)
    })
    error_message = "Public receiver bindings coexist with actual unchanged workload-identity outputs and do not add secret selectors."
  }
}
run "actual_identity_access_point_disagreement_fails" {
  command = plan
  variables {
    identity = {
      workloads                 = run.actual_bound_identity_contract.workloads
      session_key_binding_ready = run.actual_bound_identity_contract.session_key_binding_ready
      log_key_binding_ready     = run.actual_bound_identity_contract.log_key_binding_ready
      session_key_binding       = merge(run.actual_bound_identity_contract.session_key_binding, { access_point_id = "fsap-00000000000000002" })
      operational_log_keys      = run.actual_bound_identity_contract.operational_log_keys
    }
  }
  expect_failures = [var.identity]
}
run "actual_identity_log_key_disagreement_fails" {
  command = plan
  variables {
    identity = {
      workloads                 = run.actual_bound_identity_contract.workloads
      session_key_binding_ready = run.actual_bound_identity_contract.session_key_binding_ready
      log_key_binding_ready     = run.actual_bound_identity_contract.log_key_binding_ready
      session_key_binding       = run.actual_bound_identity_contract.session_key_binding
      operational_log_keys      = merge(run.actual_bound_identity_contract.operational_log_keys, { api = "arn:aws:kms:us-east-1:111122223333:key/90000000-0000-0000-0000-000000000001" })
    }
  }
  expect_failures = [data.aws_cloudwatch_log_group.workload["api"]]
}

run "discovery_consumes_actual_task_transport" {
  command = plan
  module { source = "../private-discovery" }
  variables {
    network = {
      "vpc_id" : "vpc-00000000000000001",
      "application_subnet_ids" : [
        "subnet-00000000000000001",
        "subnet-00000000000000002"
      ],
      "client_security_groups" : {
        "api" : "sg-00000000000000002",
        "worker" : "sg-00000000000000003",
        "deployment" : "sg-00000000000000004",
        "web" : "sg-00000000000000005"
      }
    }
    api_transport = run.tasks_consume_actual_identity_outputs.api_transport
  }
  assert {
    condition     = output.api_discovery.origin == run.tasks_consume_actual_identity_outputs.api_transport.origin && output.api_discovery.port == 8443 && output.api_discovery.hostname == "api.pilot.staging.saveit.internal" && aws_vpc_security_group_ingress_rule.api_https.to_port == run.tasks_consume_actual_identity_outputs.api_transport.port
    error_message = "Actual guarded task outputs must agree with the private DNS and fixed TLS path; not live identity or TLS evidence."
  }
}
run "actual_task_plaintext_port_disagreement_fails" {
  command = plan
  module { source = "../private-discovery" }
  variables {
    network = {
      "vpc_id" : "vpc-00000000000000001",
      "application_subnet_ids" : [
        "subnet-00000000000000001",
        "subnet-00000000000000002"
      ],
      "client_security_groups" : {
        "api" : "sg-00000000000000002",
        "worker" : "sg-00000000000000003",
        "deployment" : "sg-00000000000000004",
        "web" : "sg-00000000000000005"
      }
    }
    api_transport = merge(run.tasks_consume_actual_identity_outputs.api_transport, { port = 8080 })
  }
  expect_failures = [var.api_transport]
}
