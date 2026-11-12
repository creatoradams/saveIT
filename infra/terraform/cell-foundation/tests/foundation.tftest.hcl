mock_provider "aws" {
  override_during = plan
  mock_resource "aws_vpc" {
    defaults = { id = "vpc-00000000000000001" }
  }
  mock_resource "aws_kms_key" {
    defaults = { arn = "arn:aws:kms:us-east-1:111122223333:key/11111111-1111-1111-1111-111111111111", key_id = "11111111-1111-1111-1111-111111111111" }
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.database
  values          = { id = "sg-00000000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_security_group.client["api"]
  values          = { id = "sg-00000000000000002" }
}
override_resource {
  override_during = plan
  target          = aws_security_group.client["worker"]
  values          = { id = "sg-00000000000000003" }
}
override_resource {
  override_during = plan
  target          = aws_security_group.client["deployment"]
  values          = { id = "sg-00000000000000004" }
}
variables {
  account_id              = "111122223333"
  region                  = "us-east-1"
  cell                    = "pilot"
  environment             = "staging"
  availability_zones      = ["us-east-1a", "us-east-1b"]
  vpc_cidr                = "10.84.0.0/16"
  key_administrators      = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  database_engine_version = "18"
  database_instance_class = "db.m7g.large"
  database_storage_gib    = 20
}

override_resource {
  override_during = plan
  target          = aws_route_table.application
  values          = { id = "rtb-00000000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_route_table.isolated
  values          = { id = "rtb-00000000000000002" }
}

override_resource {
  override_during = plan
  target          = aws_subnet.application["0"]
  values          = { id = "subnet-00000000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_subnet.application["1"]
  values          = { id = "subnet-00000000000000002" }
}
override_resource {
  override_during = plan
  target          = aws_subnet.database["0"]
  values          = { id = "subnet-00000000000000003" }
}
override_resource {
  override_during = plan
  target          = aws_subnet.database["1"]
  values          = { id = "subnet-00000000000000004" }
}

run "isolated_database_and_exact_client_paths" {
  command = plan
  assert {
    condition = (aws_route_table.application.id != aws_route_table.isolated.id &&
      alltrue([for a in aws_route_table_association.application : a.route_table_id == aws_route_table.application.id]) &&
      alltrue([for a in aws_route_table_association.database : a.route_table_id == aws_route_table.isolated.id]) &&
      output.network.application_route_table_id == aws_route_table.application.id && output.network.database_route_table_id == aws_route_table.isolated.id &&
      alltrue([for s in aws_subnet.application : s.tags.Tier == "application"]) && alltrue([for s in aws_subnet.database : s.tags.Tier == "database"]) &&
    alltrue([for name, g in aws_security_group.client : g.tags.Process == name]))
    error_message = "Application endpoint routing must remain separate from local-only DB routing, with stable scoped metadata outputs."
  }
  assert {
    condition     = length(aws_subnet.application) == 2 && length(aws_subnet.database) == 2 && alltrue([for s in concat(values(aws_subnet.application), values(aws_subnet.database)) : !s.map_public_ip_on_launch && s.vpc_id == aws_vpc.cell.id]) && length(aws_route_table.isolated.route) == 0 && length(aws_route_table_association.application) == 2 && length(aws_route_table_association.database) == 2
    error_message = "The foundation must stay private and locally routed, across four subnets/two AZs."
  }
  assert {
    condition     = toset([for s in aws_subnet.application : s.cidr_block]) == toset(["10.84.0.0/24", "10.84.1.0/24"]) && toset([for s in aws_subnet.database : s.cidr_block]) == toset(["10.84.16.0/24", "10.84.17.0/24"]) && length(aws_default_security_group.cell.ingress) == 0 && length(aws_default_security_group.cell.egress) == 0
    error_message = "Application/database ranges must be disjoint and the default group must grant nothing."
  }
  assert {
    condition     = length(aws_vpc_security_group_ingress_rule.database) == 3 && length(aws_vpc_security_group_egress_rule.database) == 3 && length(distinct([for group in aws_security_group.client : group.id])) == 3 && alltrue([for name, rule in aws_vpc_security_group_ingress_rule.database : rule.security_group_id == aws_security_group.database.id && rule.referenced_security_group_id == aws_security_group.client[name].id && rule.ip_protocol == "tcp" && rule.from_port == 5432 && rule.to_port == 5432 && rule.cidr_ipv4 == null && rule.cidr_ipv6 == null]) && alltrue([for name, rule in aws_vpc_security_group_egress_rule.database : rule.security_group_id == aws_security_group.client[name].id && rule.referenced_security_group_id == aws_security_group.database.id && rule.ip_protocol == "tcp" && rule.from_port == 5432 && rule.to_port == 5432 && rule.cidr_ipv4 == null && rule.cidr_ipv6 == null])
    error_message = "Only three distinct designated clients may reach the database on5432; no CIDR/wildcard path is allowed."
  }
  assert {
    condition     = !aws_db_instance.cell.publicly_accessible && aws_db_instance.cell.port == 5432 && aws_db_instance.cell.network_type == "IPV4" && aws_db_instance.cell.vpc_security_group_ids == toset([aws_security_group.database.id]) && aws_db_instance.cell.db_subnet_group_name == aws_db_subnet_group.cell.name && toset(aws_db_subnet_group.cell.subnet_ids) == toset([for s in aws_subnet.database : s.id])
    error_message = "PostgreSQL must use only the isolated database group/subnets."
  }
  assert {
    condition     = aws_db_instance.cell.storage_encrypted && aws_db_instance.cell.kms_key_id == aws_kms_key.database.arn && aws_kms_key.database.enable_key_rotation && aws_kms_key.database.deletion_window_in_days == 30 && aws_db_instance.cell.manage_master_user_password && aws_db_instance.cell.password == null
    error_message = "Storage must use the rotating cell key; bootstrap passwords must be managed by RDS, not Terraform."
  }
  assert {
    condition     = aws_db_instance.cell.backup_retention_period == 35 && aws_db_instance.cell.deletion_protection && !aws_db_instance.cell.skip_final_snapshot && !aws_db_instance.cell.delete_automated_backups && aws_db_instance.cell.copy_tags_to_snapshot && !aws_db_instance.cell.allow_major_version_upgrade && !aws_db_instance.cell.apply_immediately && !aws_db_instance.cell.multi_az
    error_message = "Backup/deletion/upgrade settings must remain protected; staging is not a multi-AZ/SLO claim."
  }
  assert {
    condition     = length([for p in aws_db_parameter_group.cell.parameter : p if p.name == "rds.force_ssl" && p.value == "1" && p.apply_method == "pending-reboot"]) == 1 && length([for p in aws_db_parameter_group.cell.parameter : p if p.name == "log_statement" && p.value == "none"]) == 1 && length([for p in aws_db_parameter_group.cell.parameter : p if p.name == "log_min_error_statement" && p.value == "panic"]) == 1 && length([for p in aws_db_parameter_group.cell.parameter : p if p.name == "log_error_verbosity" && p.value == "terse"]) == 1
    error_message = "TLS enforcement and restricted statement/error logging must not regress."
  }
  assert {
    condition     = alltrue([for sid in ["KeyControlPlaneAdministratorsOnly", "RejectNonRDSCryptography", "RejectArbitraryGrants"] : length([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == sid && s.Effect == "Deny"]) == 1]) && one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectNonRDSCryptography"]).Condition.StringNotEqualsIfExists["kms:ViaService"] == "rds.us-east-1.amazonaws.com" && one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectArbitraryGrants"]).Condition.BoolIfExists["kms:GrantIsForAWSResource"] == "false" && one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "KeyControlPlaneAdministratorsOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"] == ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"]
    error_message = "Key controls require explicit administrative/service/grant boundaries, with root/admin recovery still privileged."
  }
}
run "production_requires_multiaz_and_storage_floor" {
  command = plan
  variables {
    environment          = "production"
    database_storage_gib = 100
  }
  assert {
    condition     = aws_db_instance.cell.multi_az && aws_db_instance.cell.allocated_storage >= 100 && aws_db_instance.cell.backup_retention_period == 35
    error_message = "Production intent cannot disable multi-AZ or the storage floor; this is not actual availability evidence."
  }
}
run "az_input_order_does_not_replace_subnets" {
  command = plan
  variables { availability_zones = ["us-east-1b", "us-east-1a"] }
  assert {
    condition     = aws_subnet.application["0"].availability_zone == "us-east-1a" && aws_subnet.database["0"].availability_zone == "us-east-1a" && aws_subnet.application["0"].cidr_block == "10.84.0.0/24" && aws_subnet.database["0"].cidr_block == "10.84.16.0/24" && aws_subnet.application["1"].availability_zone == "us-east-1b" && aws_subnet.database["1"].availability_zone == "us-east-1b"
    error_message = "Reordering the same AZ inputs must retain identical AZ/CIDR slot mappings."
  }
}
run "automatic_minor_servicing_does_not_request_a_downgrade" {
  command = plan
  override_resource {
    override_during = plan
    target          = aws_db_instance.cell
    values          = { engine_version_actual = "18.5" }
  }
  assert {
    condition     = aws_db_instance.cell.engine_version == "18" && aws_db_instance.cell.auto_minor_version_upgrade && aws_db_instance.cell.engine_version_actual == "18.5" && !aws_db_instance.cell.allow_major_version_upgrade
    error_message = "Keep the requested major prefix stable when an observed minor advances; actual managed refresh still requires staging evidence."
  }
}
# Exact deny shapes plus narrow condition truth tables, not an IAM evaluator.
run "exact_key_denies_and_condition_cases" {
  command = plan
  assert {
    condition = jsonencode(one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "KeyControlPlaneAdministratorsOnly"])) == jsonencode({
      Sid       = "KeyControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", Resource = "*",
      NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:Describe*", "kms:Get*", "kms:List*"],
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"] } }
    })
    error_message = "The complete control-plane deny must have only its exact administrative exceptions and bounded data/service-grant exclusions."
  }
  assert {
    condition = jsonencode(one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectNonRDSCryptography"])) == jsonencode({
      Sid    = "RejectNonRDSCryptography", Effect = "Deny", Principal = "*", Resource = "*",
      Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"],
      Condition = {
        ArnNotEquals            = { "aws:PrincipalArn" = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"] },
        StringNotEqualsIfExists = { "kms:ViaService" = "rds.us-east-1.amazonaws.com" }
      }
    })
    error_message = "The complete crypto deny must combine exact principal exceptions and missing/wrong RDS service refusal."
  }
  assert {
    condition = jsonencode(one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectArbitraryGrants"])) == jsonencode({
      Sid = "RejectArbitraryGrants", Effect = "Deny", Principal = "*", Resource = "*", Action = "kms:CreateGrant",
      Condition = {
        ArnNotEquals = { "aws:PrincipalArn" = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"] },
        BoolIfExists = { "kms:GrantIsForAWSResource" = "false" }
      }
    })
    error_message = "The complete grant deny must reject absent/false resource-grant context except exact privileged administrators/root."
  }
  assert {
    condition = alltrue([for request in [
      { principal = "arn:aws:iam::111122223333:role/Ordinary", service = "", denied = true },
      { principal = "arn:aws:iam::111122223333:role/Ordinary", service = "ec2.us-east-1.amazonaws.com", denied = true },
      { principal = "arn:aws:iam::111122223333:role/Ordinary", service = "rds.us-west-2.amazonaws.com", denied = true },
      { principal = "arn:aws:iam::111122223333:role/Ordinary", service = "rds.us-east-1.amazonaws.com", denied = false },
      { principal = "arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", service = "", denied = false },
      { principal = "arn:aws:iam::111122223333:root", service = "", denied = false },
      { principal = "", service = "", denied = true }
    ] : (!contains(one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectNonRDSCryptography"]).Condition.ArnNotEquals["aws:PrincipalArn"], request.principal) && request.service != one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectNonRDSCryptography"]).Condition.StringNotEqualsIfExists["kms:ViaService"]) == request.denied])
    error_message = "The documented crypto-condition cases must reject direct/missing/wrong-region/non-RDS use while retaining explicit privileged exceptions."
  }
  assert {
    condition = alltrue([for request in [
      { principal = "arn:aws:iam::111122223333:role/Ordinary", flag = "", denied = true },
      { principal = "arn:aws:iam::111122223333:role/Ordinary", flag = "false", denied = true },
      { principal = "arn:aws:iam::111122223333:role/Ordinary", flag = "true", denied = false },
      { principal = "arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", flag = "false", denied = false },
      { principal = "arn:aws:iam::111122223333:root", flag = "", denied = false }
    ] : (!contains(one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectArbitraryGrants"]).Condition.ArnNotEquals["aws:PrincipalArn"], request.principal) && (request.flag == "" || request.flag == one([for s in jsondecode(aws_kms_key.database.policy).Statement : s if s.Sid == "RejectArbitraryGrants"]).Condition.BoolIfExists["kms:GrantIsForAWSResource"])) == request.denied])
    error_message = "The documented grant-condition cases must handle absent/false/true context and exact privileged exceptions."
  }
}
run "reject_exact_minor_with_automatic_servicing" {
  command = plan
  variables { database_engine_version = "18.4" }
  expect_failures = [var.database_engine_version]
}
run "reject_small_production_storage" {
  command = plan
  variables { environment = "production" }
  expect_failures = [var.database_storage_gib]
}
run "reject_foreign_key_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::999900001111:role/OtherAccount"] }
  expect_failures = [var.key_administrators]
}
run "reject_wildcard_key_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/*"] }
  expect_failures = [var.key_administrators]
}
run "reject_empty_key_administrators" {
  command = plan
  variables { key_administrators = [] }
  expect_failures = [var.key_administrators]
}
run "reject_duplicate_az" {
  command = plan
  variables { availability_zones = ["us-east-1a", "us-east-1a"] }
  expect_failures = [var.availability_zones]
}
run "reject_foreign_az" {
  command = plan
  variables { availability_zones = ["us-east-1a", "us-west-2b"] }
  expect_failures = [var.availability_zones]
}
run "reject_public_cidr" {
  command = plan
  variables { vpc_cidr = "203.0.0.0/16" }
  expect_failures = [var.vpc_cidr]
}
run "reject_wrong_postgres_major" {
  command = plan
  variables { database_engine_version = "17" }
  expect_failures = [var.database_engine_version]
}
run "reject_noncommercial_region" {
  command = plan
  variables {
    region             = "cn-north-1"
    availability_zones = ["cn-north-1a", "cn-north-1b"]
  }
  expect_failures = [var.region]
}
run "reject_implicit_development_cell" {
  command = plan
  variables { environment = "development" }
  expect_failures = [var.environment]
}
run "compute_database_graph_contract" {
  command = plan
  assert {
    condition = (toset([for name, r in aws_vpc_security_group_egress_rule.database : "${name}:database:${r.from_port}"]) ==
      toset([for e in jsondecode(file("../runtime-endpoints/tests/compute-network-graph.json")).edges : "${e.from}:${e.to}:${e.port}" if e.to == "database"]) &&
      length(aws_vpc_security_group_ingress_rule.database) == 3 && alltrue([for name, r in aws_vpc_security_group_ingress_rule.database :
      r.security_group_id == aws_security_group.database.id && r.referenced_security_group_id == aws_security_group.client[name].id && r.ip_protocol == "tcp" && r.from_port == 5432 && r.to_port == 5432 && r.cidr_ipv4 == null && r.cidr_ipv6 == null]) &&
      alltrue([for name, r in aws_vpc_security_group_egress_rule.database : r.security_group_id == aws_security_group.client[name].id && r.referenced_security_group_id == aws_security_group.database.id && r.ip_protocol == "tcp" && r.to_port == 5432 && r.cidr_ipv4 == null && r.cidr_ipv6 == null]) &&
    length(aws_default_security_group.cell.ingress) == 0 && length(aws_default_security_group.cell.egress) == 0)
    error_message = "The frozen consumer graph requires exactly reciprocal API/worker/deployment PostgreSQL paths and an empty default group. Mocks are declared contracts, not rule/attachment proof."
  }
}
