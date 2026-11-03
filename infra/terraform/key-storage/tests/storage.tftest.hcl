mock_provider "aws" { override_during = plan }

override_resource {
  override_during = plan
  target          = aws_kms_key.ring
  values          = { arn = "arn:aws:kms:us-east-1:111122223333:key/22222222-2222-2222-2222-222222222222", key_id = "22222222-2222-2222-2222-222222222222" }
}
override_resource {
  override_during = plan
  target          = aws_efs_file_system.ring
  # Provider returns an empty AZ for a regional filesystem; generic mocks invent
  # a random computed string unless this service value is made explicit.
  values = { id = "fs-00000000000000001", arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:file-system/fs-00000000000000001", availability_zone_name = "" }
}
override_resource {
  override_during = plan
  target          = aws_efs_access_point.api
  values          = { id = "fsap-00000000000000001", arn = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000001" }
}
override_resource {
  override_during = plan
  target          = aws_security_group.ring
  values          = { id = "sg-00000000000000009" }
}
override_data {
  target = data.aws_subnet.application["subnet-00000000000000001"]
  values = { id = "subnet-00000000000000001", vpc_id = "vpc-00000000000000001", availability_zone = "us-east-1a", map_public_ip_on_launch = false }
}
override_data {
  target = data.aws_subnet.application["subnet-00000000000000002"]
  values = { id = "subnet-00000000000000002", vpc_id = "vpc-00000000000000001", availability_zone = "us-east-1b", map_public_ip_on_launch = false }
}
override_data {
  target = data.aws_security_group.api
  values = { id = "sg-00000000000000002", vpc_id = "vpc-00000000000000001" }
}

variables {
  account_id         = "111122223333"
  region             = "us-east-1"
  cell               = "pilot"
  environment        = "staging"
  api_task_role      = "arn:aws:iam::111122223333:role/SaveITApiTask"
  key_administrators = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  network = {
    vpc_id                 = "vpc-00000000000000001"
    api_security_group_id  = "sg-00000000000000002"
    application_subnet_ids = ["subnet-00000000000000001", "subnet-00000000000000002"]
  }
}

run "encrypted_regional_ring_and_private_mount_targets" {
  command = plan
  assert {
    condition     = aws_efs_file_system.ring.encrypted && aws_efs_file_system.ring.kms_key_id == aws_kms_key.ring.arn && aws_kms_key.ring.enable_key_rotation && aws_kms_key.ring.deletion_window_in_days == 30 && aws_efs_file_system.ring.performance_mode == "generalPurpose" && aws_efs_file_system.ring.throughput_mode == "bursting" && aws_efs_file_system.ring.availability_zone_name == "" && one(aws_efs_backup_policy.ring.backup_policy).status == "ENABLED"
    error_message = "Use a protected rotating data key, regional filesystem and automatic backup enrollment."
  }
  assert {
    condition = length(aws_efs_mount_target.ring) == 2 && alltrue([for id, mount in aws_efs_mount_target.ring :
      mount.subnet_id == id && mount.file_system_id == aws_efs_file_system.ring.id && mount.security_groups == toset([aws_security_group.ring.id])
    ]) && aws_security_group.ring.vpc_id == var.network.vpc_id
    error_message = "Both private mount targets must use only the dedicated filesystem security group."
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.ring.security_group_id == aws_security_group.ring.id && aws_vpc_security_group_ingress_rule.ring.referenced_security_group_id == var.network.api_security_group_id && aws_vpc_security_group_egress_rule.api.security_group_id == var.network.api_security_group_id && aws_vpc_security_group_egress_rule.api.referenced_security_group_id == aws_security_group.ring.id && alltrue([for rule in [aws_vpc_security_group_ingress_rule.ring, aws_vpc_security_group_egress_rule.api] : rule.ip_protocol == "tcp" && rule.from_port == 2049 && rule.to_port == 2049 && rule.cidr_ipv4 == null && rule.cidr_ipv6 == null])
    error_message = "Only API-group-to-ring TCP 2049 traffic is declared, without CIDR or worker/web access."
  }
}

run "nonroot_directory_and_explicit_task_mount_requirements" {
  command = plan
  assert {
    condition     = one(aws_efs_access_point.api.posix_user).uid == 1654 && one(aws_efs_access_point.api.posix_user).gid == 1654 && one(aws_efs_access_point.api.root_directory).path == "/saveit-session-keys" && one(one(aws_efs_access_point.api.root_directory).creation_info).owner_uid == 1654 && one(one(aws_efs_access_point.api.root_directory).creation_info).owner_gid == 1654 && one(one(aws_efs_access_point.api.root_directory).creation_info).permissions == "0700"
    error_message = "Enforce the nonroot API identity and private dedicated directory."
  }
  assert {
    condition     = output.session_key_storage.task_volume.fileSystemId == aws_efs_file_system.ring.id && output.session_key_storage.task_volume.rootDirectory == "/" && output.session_key_storage.task_volume.transitEncryption == "ENABLED" && output.session_key_storage.task_volume.authorizationConfig.iam == "ENABLED" && output.session_key_storage.task_volume.authorizationConfig.accessPointId == aws_efs_access_point.api.id && output.session_key_storage.container_path == "/var/saveit/keys" && output.session_key_storage.container_uid == 1654
    error_message = "Future task wiring must enable IAM/TLS and the exact access point, not a raw root mount."
  }
}

run "exact_filesystem_denies_and_allow" {
  command = plan
  assert {
    condition = jsondecode(aws_efs_file_system_policy.ring.policy) == {
      Version = "2012-10-17"
      Statement = [
        { Sid = "DenyUnencryptedMounts", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn, Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
        { Sid = "DenyOtherClients", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = var.api_task_role } } },
        { Sid = "DenyOtherAccessPoints", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn, Condition = { StringNotEqualsIfExists = { "elasticfilesystem:AccessPointArn" = aws_efs_access_point.api.arn } } },
        { Sid = "DenyRootClients", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:ClientRootAccess", Resource = aws_efs_file_system.ring.arn },
        { Sid = "DenyOutsideMountTargets", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn, Condition = { BoolIfExists = { "elasticfilesystem:AccessedViaMountTarget" = "false" } } },
        { Sid = "APIProtectedRing", Effect = "Allow", Principal = { AWS = var.api_task_role }, Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = aws_efs_file_system.ring.arn, Condition = { Bool = { "aws:SecureTransport" = "true", "elasticfilesystem:AccessedViaMountTarget" = "true" }, StringEquals = { "elasticfilesystem:AccessPointArn" = aws_efs_access_point.api.arn } } }
      ]
    } && !aws_efs_file_system_policy.ring.bypass_policy_lockout_safety_check
    error_message = "Preserve every explicit deny and exact TLS/access-point API allow; identity policies cannot widen past these denies."
  }
}

run "client_context_predicates_fail_closed" {
  command = plan
  # Narrow allow-condition truth table, coupled with the exact denies above.
  # This deliberately is not an IAM simulator or evidence of AWS enforcement.
  assert {
    condition = alltrue([for context in [
      { role = var.api_task_role, point = aws_efs_access_point.api.arn, tls = "true", mount = "true", allowed = true },
      { role = null, point = aws_efs_access_point.api.arn, tls = "true", mount = "true", allowed = false },
      { role = "arn:aws:iam::111122223333:role/SaveITWorkerTask", point = aws_efs_access_point.api.arn, tls = "true", mount = "true", allowed = false },
      { role = var.api_task_role, point = null, tls = "true", mount = "true", allowed = false },
      { role = var.api_task_role, point = "arn:aws:elasticfilesystem:us-east-1:111122223333:access-point/fsap-00000000000000009", tls = "true", mount = "true", allowed = false },
      { role = var.api_task_role, point = aws_efs_access_point.api.arn, tls = null, mount = "true", allowed = false },
      { role = var.api_task_role, point = aws_efs_access_point.api.arn, tls = "false", mount = "true", allowed = false },
      { role = var.api_task_role, point = aws_efs_access_point.api.arn, tls = "true", mount = null, allowed = false },
      { role = var.api_task_role, point = aws_efs_access_point.api.arn, tls = "true", mount = "false", allowed = false }
      ] : (context.role == jsondecode(aws_efs_file_system_policy.ring.policy).Statement[5].Principal.AWS && context.point == jsondecode(aws_efs_file_system_policy.ring.policy).Statement[5].Condition.StringEquals["elasticfilesystem:AccessPointArn"] && context.tls == jsondecode(aws_efs_file_system_policy.ring.policy).Statement[5].Condition.Bool["aws:SecureTransport"] && context.mount == jsondecode(aws_efs_file_system_policy.ring.policy).Statement[5].Condition.Bool["elasticfilesystem:AccessedViaMountTarget"]) == context.allowed
    ])
    error_message = "Only the exact API/access-point/true-TLS/true-mount-target context meets the client allow predicates."
  }
}

run "kms_service_and_control_boundaries" {
  command = plan
  assert {
    condition = jsondecode(aws_kms_key.ring.policy).Statement[5] == {
      Sid       = "EFSInThisAccountAndRegion", Effect = "Allow", Principal = { AWS = "*" }, Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:DescribeKey"], Resource = "*",
      Condition = { StringEquals = { "kms:ViaService" = "elasticfilesystem.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } }
      } && jsondecode(aws_kms_key.ring.policy).Statement[3] == {
      Sid       = "RejectNonEFSDataUse", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::${var.account_id}:root"]) }, StringNotEqualsIfExists = { "kms:ViaService" = "elasticfilesystem.${var.region}.amazonaws.com" } }
      } && jsondecode(aws_kms_key.ring.policy).Statement[4] == {
      Sid       = "RejectArbitraryGrants", Effect = "Deny", Principal = "*", Action = "kms:CreateGrant", Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::${var.account_id}:root"]) }, BoolIfExists = { "kms:GrantIsForAWSResource" = "false" } }
    }
    error_message = "Preserve the exact same-account/regional EFS use, non-EFS data-use and arbitrary-grant deny predicates."
  }
  assert {
    condition = jsondecode(aws_kms_key.ring.policy).Statement[2] == {
      Sid       = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), ["arn:aws:iam::${var.account_id}:root"]) } }
    }
    error_message = "Runtime/data-service roles must not administer or delegate the key."
  }
}

run "reject_cross_account_role" {
  command = plan
  variables { api_task_role = "arn:aws:iam::999900001111:role/SaveITApiTask" }
  expect_failures = [var.api_task_role]
}
run "reject_role_wildcard" {
  command = plan
  variables { api_task_role = "arn:aws:iam::111122223333:role/SaveIT*" }
  expect_failures = [var.api_task_role]
}
run "reject_api_as_key_administrator" {
  command = plan
  variables { key_administrators = ["arn:aws:iam::111122223333:role/SaveITApiTask"] }
  expect_failures = [var.key_administrators]
}
run "reject_single_subnet" {
  command = plan
  variables {
    network = { vpc_id = "vpc-00000000000000001", api_security_group_id = "sg-00000000000000002", application_subnet_ids = ["subnet-00000000000000001"] }
  }
  expect_failures = [var.network]
}
run "reject_foreign_subnet_vpc" {
  command = plan
  override_data {
    target = data.aws_subnet.application["subnet-00000000000000001"]
    values = { id = "subnet-00000000000000001", vpc_id = "vpc-00000000000000009", availability_zone = "us-east-1a", map_public_ip_on_launch = false }
  }
  expect_failures = [data.aws_subnet.application["subnet-00000000000000001"]]
}
run "reject_public_address_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.application["subnet-00000000000000001"]
    values = { id = "subnet-00000000000000001", vpc_id = "vpc-00000000000000001", availability_zone = "us-east-1a", map_public_ip_on_launch = true }
  }
  expect_failures = [data.aws_subnet.application["subnet-00000000000000001"]]
}
run "reject_same_zone_mount_targets" {
  command = plan
  override_data {
    target = data.aws_subnet.application["subnet-00000000000000002"]
    values = { id = "subnet-00000000000000002", vpc_id = "vpc-00000000000000001", availability_zone = "us-east-1a", map_public_ip_on_launch = false }
  }
  expect_failures = [aws_efs_file_system.ring]
}
run "reject_foreign_api_group" {
  command = plan
  override_data {
    target = data.aws_security_group.api
    values = { id = "sg-00000000000000002", vpc_id = "vpc-00000000000000009" }
  }
  expect_failures = [data.aws_security_group.api]
}
