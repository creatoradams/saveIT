locals {
  name = "saveit-${var.cell}-${var.environment}-session-keys"
  root = "arn:aws:iam::${var.account_id}:root"
}

data "aws_subnet" "application" {
  for_each = var.network.application_subnet_ids
  id       = each.value
  lifecycle {
    postcondition {
      condition     = self.vpc_id == var.network.vpc_id && !self.map_public_ip_on_launch
      error_message = "Session keys require application subnets in the designated VPC with automatic public addresses disabled."
    }
  }
}
data "aws_security_group" "api" {
  id = var.network.api_security_group_id
  lifecycle {
    postcondition {
      condition     = self.vpc_id == var.network.vpc_id
      error_message = "The API security group must belong to the designated VPC."
    }
  }
}

resource "aws_kms_key" "ring" {
  description             = "${local.name} encrypted EFS file data; PFX encryption remains separate"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root }, Action = "kms:*", Resource = "*" },
      { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
      Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
      { Sid = "ControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) } } },
      { Sid = "RejectNonEFSDataUse", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) }, StringNotEqualsIfExists = { "kms:ViaService" = "elasticfilesystem.${var.region}.amazonaws.com" } } },
      { Sid = "RejectArbitraryGrants", Effect = "Deny", Principal = "*", Action = "kms:CreateGrant", Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) }, BoolIfExists = { "kms:GrantIsForAWSResource" = "false" } } },
      { Sid    = "EFSInThisAccountAndRegion", Effect = "Allow", Principal = { AWS = "*" },
        Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:DescribeKey"], Resource = "*",
      Condition = { StringEquals = { "kms:ViaService" = "elasticfilesystem.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } } }
    ]
  })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "ring" {
  name          = "alias/${local.name}"
  target_key_id = aws_kms_key.ring.key_id
}
resource "aws_efs_file_system" "ring" {
  creation_token   = local.name
  encrypted        = true
  kms_key_id       = aws_kms_key.ring.arn
  performance_mode = "generalPurpose"
  throughput_mode  = "bursting"
  tags             = { Name = local.name }
  lifecycle {
    prevent_destroy = true
    precondition {
      condition     = length(toset([for subnet in data.aws_subnet.application : subnet.availability_zone])) == 2
      error_message = "Session-key mount targets require two distinct availability zones."
    }
  }
}
resource "aws_efs_backup_policy" "ring" {
  file_system_id = aws_efs_file_system.ring.id
  backup_policy { status = "ENABLED" }
}
resource "aws_efs_access_point" "api" {
  file_system_id = aws_efs_file_system.ring.id
  posix_user {
    uid = 1654
    gid = 1654
  }
  root_directory {
    path = "/saveit-session-keys"
    creation_info {
      owner_uid   = 1654
      owner_gid   = 1654
      permissions = "0700"
    }
  }
  tags = { Name = local.name }
}
resource "aws_efs_file_system_policy" "ring" {
  file_system_id                     = aws_efs_file_system.ring.id
  bypass_policy_lockout_safety_check = false
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "DenyUnencryptedMounts", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn,
      Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
      { Sid = "DenyOtherClients", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn,
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = var.api_task_role } } },
      { Sid = "DenyOtherAccessPoints", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn,
      Condition = { StringNotEqualsIfExists = { "elasticfilesystem:AccessPointArn" = aws_efs_access_point.api.arn } } },
      { Sid = "DenyRootClients", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:ClientRootAccess", Resource = aws_efs_file_system.ring.arn },
      { Sid = "DenyOutsideMountTargets", Effect = "Deny", Principal = "*", Action = "elasticfilesystem:Client*", Resource = aws_efs_file_system.ring.arn,
      Condition = { BoolIfExists = { "elasticfilesystem:AccessedViaMountTarget" = "false" } } },
      { Sid = "APIProtectedRing", Effect = "Allow", Principal = { AWS = var.api_task_role }, Action = ["elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite"], Resource = aws_efs_file_system.ring.arn,
      Condition = { Bool = { "aws:SecureTransport" = "true", "elasticfilesystem:AccessedViaMountTarget" = "true" }, StringEquals = { "elasticfilesystem:AccessPointArn" = aws_efs_access_point.api.arn } } }
    ]
  })
}
resource "aws_security_group" "ring" {
  name_prefix = "${local.name}-"
  description = "Session-key NFS from the designated API security group only"
  vpc_id      = var.network.vpc_id
  # Standalone rules only; no all-destination egress or worker/web path.
}
resource "aws_vpc_security_group_ingress_rule" "ring" {
  security_group_id            = aws_security_group.ring.id
  referenced_security_group_id = data.aws_security_group.api.id
  ip_protocol                  = "tcp"
  from_port                    = 2049
  to_port                      = 2049
}
resource "aws_vpc_security_group_egress_rule" "api" {
  security_group_id            = data.aws_security_group.api.id
  referenced_security_group_id = aws_security_group.ring.id
  ip_protocol                  = "tcp"
  from_port                    = 2049
  to_port                      = 2049
}
resource "aws_efs_mount_target" "ring" {
  for_each        = var.network.application_subnet_ids
  file_system_id  = aws_efs_file_system.ring.id
  subnet_id       = each.value
  security_groups = [aws_security_group.ring.id]
  # Install client policy before any network-reachable target exists.
  depends_on = [aws_efs_file_system_policy.ring]
}
