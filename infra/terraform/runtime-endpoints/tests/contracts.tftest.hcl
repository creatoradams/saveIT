mock_provider "aws" { override_during = plan }
override_data {
  target = data.aws_vpc.cell
  values = {
    "id"                   = "vpc-00000000000000001"
    "arn"                  = "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001"
    "owner_id"             = "111122223333"
    "default"              = false
    "enable_dns_support"   = true
    "enable_dns_hostnames" = true
    "cidr_block"           = "10.84.0.0/16"
    "cidr_block_associations" = [{
      "association_id" = "vpc-cidr-assoc-00000000000000001"
      "cidr_block"     = "10.84.0.0/16"
      "state"          = "associated"
    }]
    "ipv6_cidr_block_associations" = []
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["application-0"]
  values = {
    "id"                      = "subnet-00000000000000001"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1a"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["application-1"]
  values = {
    "id"                      = "subnet-00000000000000002"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1b"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["database-0"]
  values = {
    "id"                      = "subnet-00000000000000003"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000003"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1a"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["database-1"]
  values = {
    "id"                      = "subnet-00000000000000004"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000004"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1b"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
  }
}
override_data {
  target = data.aws_security_group.client["api"]
  values = {
    "id"     = "sg-00000000000000002"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "api"
    }
  }
}
override_data {
  target = data.aws_security_group.client["worker"]
  values = {
    "id"     = "sg-00000000000000003"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000003"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "worker"
    }
  }
}
override_data {
  target = data.aws_security_group.client["deployment"]
  values = {
    "id"     = "sg-00000000000000004"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000004"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "deployment"
    }
  }
}
override_data {
  target = data.aws_prefix_list.s3
  values = {
    "id"          = "pl-00000001"
    "name"        = "com.amazonaws.us-east-1.s3"
    "cidr_blocks" = ["52.216.0.0/15"]
  }
}
override_data {
  target = data.aws_route_table.cell["application"]
  values = {
    "id"       = "rtb-00000000000000001"
    "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
    "owner_id" = "111122223333"
    "vpc_id"   = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
    "routes" = []
    "associations" = [{
      "subnet_id"                  = "subnet-00000000000000001"
      "route_table_id"             = "rtb-00000000000000001"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000001"
      }, {
      "subnet_id"                  = "subnet-00000000000000002"
      "route_table_id"             = "rtb-00000000000000001"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000002"
    }]
  }
}
override_data {
  target = data.aws_route_table.cell["database"]
  values = {
    "id"       = "rtb-00000000000000002"
    "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000002"
    "owner_id" = "111122223333"
    "vpc_id"   = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
    "routes" = []
    "associations" = [{
      "subnet_id"                  = "subnet-00000000000000003"
      "route_table_id"             = "rtb-00000000000000002"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000001"
      }, {
      "subnet_id"                  = "subnet-00000000000000004"
      "route_table_id"             = "rtb-00000000000000002"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000002"
    }]
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.web
  values = {
    "id" = "sg-00000000000000005"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["ecr-api"]
  values = {
    "id" = "sg-00000000000000010"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["ecr-dkr"]
  values = {
    "id" = "sg-00000000000000011"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["logs"]
  values = {
    "id" = "sg-00000000000000012"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["configuration"]
  values = {
    "id" = "sg-00000000000000013"
  }
}
variables {
  account_id  = "111122223333"
  region      = "us-east-1"
  cell        = "pilot"
  environment = "staging"
  network = {
    "vpc_id"                     = "vpc-00000000000000001"
    "application_subnet_ids"     = ["subnet-00000000000000001", "subnet-00000000000000002"]
    "database_subnet_ids"        = ["subnet-00000000000000003", "subnet-00000000000000004"]
    "application_route_table_id" = "rtb-00000000000000001"
    "database_route_table_id"    = "rtb-00000000000000002"
    "database_security_group"    = "sg-00000000000000001"
    "client_security_groups" = {
      "api"        = "sg-00000000000000002"
      "worker"     = "sg-00000000000000003"
      "deployment" = "sg-00000000000000004"
    }
  }
  identity = {
    "workloads" = {
      "api" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api"
      }
      "web" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/web"
      }
      "worker" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/worker"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker"
      }
      "migration" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/migration"
      }
      "access" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/access"
      }
    }
    "configuration" = {
      "api" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00"
        "version_id" = "10000000-0000-0000-0000-000000000001"
      }
      "worker" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd01"
        "version_id" = "10000000-0000-0000-0000-000000000002"
      }
      "migration" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/migration/config-Abcd02"
        "version_id" = "10000000-0000-0000-0000-000000000003"
      }
      "access" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/access/config-Abcd03"
        "version_id" = "10000000-0000-0000-0000-000000000004"
      }
    }
  }
}
run "actual_foundation_reference_contract" {
  command = plan
  module { source = "../cell-foundation" }
  variables {
    availability_zones      = ["us-east-1a", "us-east-1b"]
    vpc_cidr                = "10.84.0.0/16"
    key_administrators      = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    database_engine_version = "18"
    database_instance_class = "db.m7g.large"
    database_storage_gib    = 20
  }
  override_resource {
    override_during = plan
    target          = aws_vpc.cell
    values = {
      "id" = "vpc-00000000000000001"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_security_group.database
    values = {
      "id" = "sg-00000000000000001"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_security_group.client["api"]
    values = {
      "id" = "sg-00000000000000002"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_security_group.client["worker"]
    values = {
      "id" = "sg-00000000000000003"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_security_group.client["deployment"]
    values = {
      "id" = "sg-00000000000000004"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_subnet.application["0"]
    values = {
      "id" = "subnet-00000000000000001"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_subnet.application["1"]
    values = {
      "id" = "subnet-00000000000000002"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_subnet.database["0"]
    values = {
      "id" = "subnet-00000000000000003"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_subnet.database["1"]
    values = {
      "id" = "subnet-00000000000000004"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_route_table.application
    values = {
      "id" = "rtb-00000000000000001"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_route_table.isolated
    values = {
      "id" = "rtb-00000000000000002"
    }
  }
  assert {
    condition     = jsonencode(output.network) == jsonencode(var.network)
    error_message = "Actual foundation outputs must retain the exact separated routing/subnet/process-group reference contract."
  }
}
run "actual_identity_reference_contract" {
  command = plan
  module { source = "../workload-identity" }
  variables {
    key_administrators        = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    secret_custodians         = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    web_workload_pool_enabled = true
    configuration_versions = {
      "api"       = "10000000-0000-0000-0000-000000000001"
      "worker"    = "10000000-0000-0000-0000-000000000002"
      "migration" = "10000000-0000-0000-0000-000000000003"
      "access"    = "10000000-0000-0000-0000-000000000004"
    }
  }
  override_resource {
    target          = aws_cognito_identity_pool.web[0]
    override_during = plan
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["api"]
    values = {
      "arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["web"]
    values = {
      "arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["worker"]
    values = {
      "arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["migration"]
    values = {
      "arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_iam_role.execution["access"]
    values = {
      "arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["api"]
    values = {
      "arn"    = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000001"
      "key_id" = "10000000-0000-0000-0000-000000000001"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["api"]
    values = {
      "arn" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["worker"]
    values = {
      "arn"    = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000002"
      "key_id" = "10000000-0000-0000-0000-000000000002"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["worker"]
    values = {
      "arn" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd01"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["migration"]
    values = {
      "arn"    = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000003"
      "key_id" = "10000000-0000-0000-0000-000000000003"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["migration"]
    values = {
      "arn" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/migration/config-Abcd02"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_kms_key.configuration["access"]
    values = {
      "arn"    = "arn:aws:kms:us-east-1:111122223333:key/10000000-0000-0000-0000-000000000004"
      "key_id" = "10000000-0000-0000-0000-000000000004"
    }
  }
  override_resource {
    override_during = plan
    target          = aws_secretsmanager_secret.configuration["access"]
    values = {
      "arn" = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/access/config-Abcd03"
    }
  }
  assert {
    condition     = (!output.session_key_binding_ready && !output.log_key_binding_ready && output.session_key_binding == null && output.operational_log_keys == null && alltrue([for n, p in aws_iam_policy.task_boundary : jsondecode(p.policy).Statement[0].Effect == "Deny" && jsondecode(p.policy).Statement[0].Action == "*"]))
    error_message = "Preparing endpoint references must not turn unbound task identities into usable compute authority."
  }
}
run "endpoints_consume_actual_modules" {
  command = plan
  variables {
    network  = run.actual_foundation_reference_contract.network
    identity = { workloads = run.actual_identity_reference_contract.workloads, configuration = run.actual_identity_reference_contract.configuration }
  }
  assert {
    condition = (aws_vpc_endpoint.layers.route_table_ids == toset([run.actual_foundation_reference_contract.network.application_route_table_id]) &&
      !contains(aws_vpc_endpoint.layers.route_table_ids, run.actual_foundation_reference_contract.network.database_route_table_id) &&
      alltrue([for name, w in run.actual_identity_reference_contract.workloads : one([for s in jsondecode(aws_vpc_endpoint.interface["ecr-api"].policy).Statement : s if s.Sid == "Pull${title(name)}"]).Resource == w.repository_arn && one([for s in jsondecode(aws_vpc_endpoint.interface["logs"].policy).Statement : s if s.Sid == "Write${title(name)}Streams"]).Resource == "${w.log_group_arn}:log-stream:*"]) &&
    alltrue([for name, c in run.actual_identity_reference_contract.configuration : one([for s in jsondecode(aws_vpc_endpoint.interface["configuration"].policy).Statement : s if s.Sid == "Read${title(name)}Version"]).Resource == c.arn && one([for s in jsondecode(aws_vpc_endpoint.interface["configuration"].policy).Statement : s if s.Sid == "Read${title(name)}Version"]).Condition.StringEquals["secretsmanager:VersionId"] == c.version_id]))
    error_message = "Use the actual foundation and identity outputs unchanged for routes and exact per-execution resource/version filters. Mocks are not IAM or reachability evidence."
  }
}
run "actual_shared_table_contract_fails" {
  command = plan
  variables {
    network = merge(run.actual_foundation_reference_contract.network, { application_route_table_id = run.actual_foundation_reference_contract.network.database_route_table_id })
  }
  expect_failures = [var.network]
}

run "discovery_consumes_actual_endpoint_network" {
  command = plan
  module { source = "../private-discovery" }
  variables {
    network = run.endpoints_consume_actual_modules.runtime_network
    api_transport = {
      "origin" : "https://api.pilot.staging.saveit.internal:8443",
      "port" : 8443,
      "server_tls_declared" : true
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

  assert {
    condition     = output.api_discovery.vpc_id == run.endpoints_consume_actual_modules.runtime_network.vpc_id && output.api_discovery.application_subnet_ids == run.endpoints_consume_actual_modules.runtime_network.application_subnet_ids && output.api_discovery.client_security_groups == tomap(run.endpoints_consume_actual_modules.runtime_network.client_security_groups) && aws_vpc_security_group_ingress_rule.api_https.referenced_security_group_id == run.endpoints_consume_actual_modules.runtime_network.client_security_groups.web
    error_message = "Actual endpoint-network outputs must flow unchanged into private discovery and the exact web/API path."
  }
}
run "actual_network_shared_web_api_group_fails" {
  command = plan
  module { source = "../private-discovery" }
  variables {
    network = merge(run.endpoints_consume_actual_modules.runtime_network, { client_security_groups = merge(run.endpoints_consume_actual_modules.runtime_network.client_security_groups, { web = run.endpoints_consume_actual_modules.runtime_network.client_security_groups.api }) })
    api_transport = {
      "origin" : "https://api.pilot.staging.saveit.internal:8443",
      "port" : 8443,
      "server_tls_declared" : true
    }
  }
  expect_failures = [var.network]
}

# Actual local modules, provider-mocked at plan time. No IAM or packet-path evidence.
run "actual_relay_bootstrap_before_endpoint_admission" {
  command = plan
  module { source = "../relay-identity" }
  variables {

    key_administrators       = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"

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
  override_resource {
    override_during = plan
    target          = aws_cloudwatch_log_group.relay
    values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay" }
  }

  assert {
    condition     = !output.binding_ready && output.image_repository == null && length(aws_iam_role_policy.task) == 0 && length(aws_iam_role_policy.execution) == 0
    error_message = "The actual relay root's bootstrap metadata has no usable task/execution grants."
  }
}
run "actual_bootstrap_output_cannot_admit_endpoint_calls" {
  command = plan
  variables {
    relay = { roles = run.actual_relay_bootstrap_before_endpoint_admission.roles, configuration = run.actual_relay_bootstrap_before_endpoint_admission.configuration, operational_log = run.actual_relay_bootstrap_before_endpoint_admission.operational_log, image_repository = run.actual_relay_bootstrap_before_endpoint_admission.image_repository, binding_ready = run.actual_relay_bootstrap_before_endpoint_admission.binding_ready }
  }
  expect_failures = [var.relay]
}
run "actual_store_prepares_relay_endpoint_repository" {
  command = plan
  module { source = "../artifact-stores" }
  variables {
    key_administrators       = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    publisher_role           = "arn:aws:iam::111122223333:role/SaveITProtectedPublisher"
    relay_execution_role_arn = run.actual_relay_bootstrap_before_endpoint_admission.roles.execution_arn
  }
  override_resource {
    override_during = plan
    target          = aws_ecr_repository.images["deployment"]
    values          = { arn = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment", repository_url = "111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/deployment", registry_id = "111122223333" }
  }
  assert {
    condition     = length(output.repositories.deployment.pull_execution_roles) == 3 && contains(output.repositories.deployment.pull_execution_roles, run.actual_relay_bootstrap_before_endpoint_admission.roles.execution_arn)
    error_message = "Actual store metadata must explicitly include the isolated relay execution reader."
  }
}
run "actual_bound_relay_output_for_endpoint_filters" {
  command = plan
  module { source = "../relay-identity" }
  variables {

    key_administrators       = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    secret_custodians        = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_version_id = "10000000-0000-0000-0000-000000000009"

    image_repository = run.actual_store_prepares_relay_endpoint_repository.repositories.deployment
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
  override_resource {
    override_during = plan
    target          = aws_cloudwatch_log_group.relay
    values          = { arn = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/relay" }
  }

  assert {
    condition     = output.binding_ready && length(aws_iam_role_policy.task) == 1 && length(aws_iam_role_policy.execution) == 1
    error_message = "Actual relay output must consume the staged store binding before being admitted by endpoints."
  }
}
run "endpoints_consume_actual_bound_relay_outputs" {
  command = plan
  variables {
    relay = { roles = run.actual_bound_relay_output_for_endpoint_filters.roles, configuration = run.actual_bound_relay_output_for_endpoint_filters.configuration, operational_log = run.actual_bound_relay_output_for_endpoint_filters.operational_log, image_repository = run.actual_bound_relay_output_for_endpoint_filters.image_repository, binding_ready = run.actual_bound_relay_output_for_endpoint_filters.binding_ready }
  }
  assert {
    condition = (output.relay_admission.roles.task_arn == run.actual_bound_relay_output_for_endpoint_filters.roles.task_arn &&
      output.relay_admission.roles.execution_arn == run.actual_bound_relay_output_for_endpoint_filters.roles.execution_arn &&
      output.relay_admission.configuration.arn == run.actual_bound_relay_output_for_endpoint_filters.configuration.arn &&
      one([for s in jsondecode(aws_vpc_endpoint.interface["configuration"].policy).Statement : s if s.Sid == "ReadRelayVersion"]).Condition.ArnEquals["aws:PrincipalArn"] == output.relay_admission.roles.task_arn &&
      one([for s in jsondecode(aws_vpc_endpoint.interface["ecr-api"].policy).Statement : s if s.Sid == "PullRelay"]).Resource == run.actual_store_prepares_relay_endpoint_repository.repositories.deployment.arn &&
    jsonencode(output.runtime_network.client_security_groups) == jsonencode(merge(var.network.client_security_groups, { web = "sg-00000000000000005" })))
    error_message = "Consume actual mocked producer outputs without altering the existing four-group discovery contract; this is not real IAM/network verification."
  }
}


run "actual_bound_relay_network_output" {
  command = plan
  variables {
    relay                 = { roles = run.actual_bound_relay_output_for_endpoint_filters.roles, configuration = run.actual_bound_relay_output_for_endpoint_filters.configuration, operational_log = run.actual_bound_relay_output_for_endpoint_filters.operational_log, image_repository = run.actual_bound_relay_output_for_endpoint_filters.image_repository, binding_ready = run.actual_bound_relay_output_for_endpoint_filters.binding_ready }
    relay_network_enabled = true
  }

  override_resource {
    override_during = plan
    target          = aws_security_group.relay[0]
    values          = { id = "sg-00000000000000006" }
  }

  assert {
    condition     = output.relay_network.security_group_id == "sg-00000000000000006" && output.relay_network.vpc_id == var.network.vpc_id && output.relay_admission.roles.task_arn == run.actual_bound_relay_output_for_endpoint_filters.roles.task_arn
    error_message = "Dedicated network mode consumes actual staged producer admission without sharing a legacy group."
  }
}
run "discovery_consumes_actual_relay_network_output" {
  command = plan
  module { source = "../private-discovery" }
  variables {
    network       = run.actual_bound_relay_network_output.runtime_network
    relay_network = run.actual_bound_relay_network_output.relay_network
    api_transport = { origin = "https://api.pilot.staging.saveit.internal:8443", port = 8443, server_tls_declared = true }
  }

  override_data {
    override_during = plan
    target          = data.aws_security_group.relay[0]
    values = {
      "id" : "sg-00000000000000006",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000006",
      "vpc_id" : "vpc-00000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "relay"
      }
    }
  }

  override_data {
    override_during = plan
    target          = data.aws_security_group.client["web"]
    values          = { id = "sg-00000000000000005", arn = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000005", vpc_id = "vpc-00000000000000001", tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging", Process = "web" } }
  }
  assert {
    condition     = output.relay_discovery.security_group_id == run.actual_bound_relay_network_output.relay_network.security_group_id && output.relay_discovery.origin == "https://relay.pilot.staging.saveit.internal:8443" && aws_vpc_security_group_ingress_rule.api_https.referenced_security_group_id == output.relay_discovery.security_group_id
    error_message = "Actual mocked endpoint relay network must select relay/API and web/relay paths, not the direct legacy API path."
  }
}

run "endpoint_consumes_actual_workload_pool" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000001", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.pilot.staging.web", "allow_unauthenticated_identities" : false, "allow_classic_flow" : false, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : {}, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  override_resource {
    override_during = plan
    target          = aws_vpc_endpoint.web_token[0]
    values          = { id = "vpce-00000000000000009" }
  }
  override_resource {
    override_during = plan
    target          = aws_security_group.web_token[0]
    values          = { id = "sg-00000000000000009" }
  }
  assert {
    condition     = output.web_workload_token_endpoint.pool_id == run.actual_identity_reference_contract.web_workload_pool.pool_id && jsondecode(aws_vpc_endpoint.web_token[0].policy) == jsondecode(file("../workload-identity/tests/web-token-endpoint-policy.json")) && aws_vpc_security_group_egress_rule.web_token[0].security_group_id == aws_security_group.web.id && aws_vpc_security_group_ingress_rule.web_token[0].referenced_security_group_id == aws_security_group.web.id && aws_vpc_security_group_ingress_rule.web_token[0].security_group_id == aws_security_group.web_token[0].id && aws_vpc_endpoint.web_token[0].private_dns_enabled && toset(aws_vpc_endpoint.web_token[0].subnet_ids) == toset(var.network.application_subnet_ids)
    error_message = "Actual mocked producer metadata must create only the own web-token path and complete endpoint policy; not IAM evidence."
  }
}

run "reject_workload_pool_public_identity" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000001", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.pilot.staging.web", "allow_unauthenticated_identities" : true, "allow_classic_flow" : false, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : {}, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  expect_failures = [data.aws_cognito_identity_pool.web[0]]
}

run "reject_workload_pool_classic_flow" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000001", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.pilot.staging.web", "allow_unauthenticated_identities" : false, "allow_classic_flow" : true, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : {}, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  expect_failures = [data.aws_cognito_identity_pool.web[0]]
}

run "reject_workload_pool_foreign_pool" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000002", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.pilot.staging.web", "allow_unauthenticated_identities" : false, "allow_classic_flow" : false, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : {}, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  expect_failures = [data.aws_cognito_identity_pool.web[0]]
}

run "reject_workload_pool_wrong_provider" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000001", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.other.staging.web", "allow_unauthenticated_identities" : false, "allow_classic_flow" : false, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : {}, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  expect_failures = [data.aws_cognito_identity_pool.web[0]]
}

run "reject_workload_pool_public_provider" {
  command = plan
  variables {
    web_workload_pool = run.actual_identity_reference_contract.web_workload_pool
  }
  override_data {
    override_during = plan
    target          = data.aws_cognito_identity_pool.web[0]
    values          = { "id" : "us-east-1:10000000-0000-0000-0000-000000000001", "arn" : "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001", "identity_pool_name" : "saveit-pilot-staging-web-workload", "developer_provider_name" : "saveit.pilot.staging.web", "allow_unauthenticated_identities" : false, "allow_classic_flow" : false, "cognito_identity_providers" : [], "openid_connect_provider_arns" : [], "saml_provider_arns" : [], "supported_login_providers" : { "accounts.google.com" : "synthetic" }, "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web" } }
  }

  expect_failures = [data.aws_cognito_identity_pool.web[0]]
}

run "identity_rebinds_actual_token_endpoint" {
  command = plan
  module { source = "../workload-identity" }
  variables {
    key_administrators          = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
    secret_custodians           = ["arn:aws:iam::111122223333:role/SaveITSecretCustodian"]
    configuration_versions      = { for process, reference in run.actual_identity_reference_contract.configuration : process => reference.version_id }
    web_workload_pool_enabled   = true
    web_workload_token_endpoint = run.endpoint_consumes_actual_workload_pool.web_workload_token_endpoint
  }
  override_resource {
    override_during = plan
    target          = aws_cognito_identity_pool.web[0]
    values          = { id = "us-east-1:10000000-0000-0000-0000-000000000001", arn = "arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001" }
  }
  override_data {
    override_during = plan
    target          = data.aws_vpc_endpoint.web_token[0]
    values          = { "id" : "vpce-00000000000000009", "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc-endpoint/vpce-00000000000000009", "owner_id" : "111122223333", "vpc_id" : "vpc-00000000000000001", "service_name" : "com.amazonaws.us-east-1.cognito-identity", "vpc_endpoint_type" : "Interface", "state" : "available", "private_dns_enabled" : true, "ip_address_type" : "ipv4", "subnet_ids" : ["subnet-00000000000000001", "subnet-00000000000000002"], "security_group_ids" : ["sg-00000000000000009"], "tags" : { "Project" : "SaveIT", "Cell" : "pilot", "Environment" : "staging", "Process" : "web-token" }, "policy" : "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"OnlyWebTokenIssuance\",\"Effect\":\"Allow\",\"Principal\":{\"AWS\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"},\"Action\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectOtherPrincipals\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"ArnNotEquals\":{\"aws:PrincipalArn\":\"arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task\"}}},{\"Sid\":\"RejectOtherActions\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"NotAction\":\"cognito-identity:GetOpenIdTokenForDeveloperIdentity\",\"Resource\":\"*\"},{\"Sid\":\"RejectOtherPools\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"NotResource\":\"arn:aws:cognito-identity:us-east-1:111122223333:identitypool/us-east-1:10000000-0000-0000-0000-000000000001\"},{\"Sid\":\"RejectInsecureTransport\",\"Effect\":\"Deny\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\",\"Condition\":{\"BoolIfExists\":{\"aws:SecureTransport\":\"false\"}}}]}" }
  }

  assert {
    condition     = output.web_workload_admission.binding_ready && output.web_workload_admission.endpoint == run.endpoint_consumes_actual_workload_pool.web_workload_token_endpoint && jsondecode(aws_iam_role_policy.web_token[0].policy) == jsondecode(file("../workload-identity/tests/web-token-boundary.json"))
    error_message = "Rebind only the actual staged pool/endpoint output while preserving the complete policy; mock plans are not IAM enforcement."
  }
}
