locals {
  name  = "saveit-${var.cell}-${var.environment}"
  zones = { for index, az in sort(var.availability_zones) : tostring(index) => az }
  root  = "arn:aws:iam::${var.account_id}:root"
}

# Entirely isolated foundation. Internet ingress/egress, endpoints and compute are
# deliberately separate unfinished work, rather than implicit default routes.
resource "aws_vpc" "cell" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = local.name }
  lifecycle { prevent_destroy = true }
}
resource "aws_default_security_group" "cell" {
  vpc_id = aws_vpc.cell.id
  # Empty rules remove the VPC default group's allow-all/self defaults.
  ingress = []
  egress  = []
}
resource "aws_subnet" "application" {
  for_each                = local.zones
  vpc_id                  = aws_vpc.cell.id
  availability_zone       = each.value
  cidr_block              = cidrsubnet(var.vpc_cidr, 8, tonumber(each.key))
  map_public_ip_on_launch = false
  tags                    = { Name = "${local.name}-application-${each.key}" }
}
resource "aws_subnet" "database" {
  for_each                = local.zones
  vpc_id                  = aws_vpc.cell.id
  availability_zone       = each.value
  cidr_block              = cidrsubnet(var.vpc_cidr, 8, 16 + tonumber(each.key))
  map_public_ip_on_launch = false
  tags                    = { Name = "${local.name}-database-${each.key}" }
}
resource "aws_route_table" "isolated" {
  vpc_id = aws_vpc.cell.id
  route  = []
  tags   = { Name = "${local.name}-isolated" }
}
resource "aws_route_table_association" "application" {
  for_each       = local.zones
  subnet_id      = aws_subnet.application[each.key].id
  route_table_id = aws_route_table.isolated.id
}
resource "aws_route_table_association" "database" {
  for_each       = local.zones
  subnet_id      = aws_subnet.database[each.key].id
  route_table_id = aws_route_table.isolated.id
}
resource "aws_security_group" "database" {
  name_prefix = "${local.name}-database-"
  description = "Private PostgreSQL from the three designated process security groups only"
  vpc_id      = aws_vpc.cell.id
}
resource "aws_security_group" "client" {
  for_each    = toset(["api", "worker", "deployment"])
  name_prefix = "${local.name}-${each.key}-"
  description = "Future ${each.key} process; no inbound or internet authority"
  vpc_id      = aws_vpc.cell.id
  # Only standalone rules below; provider removes AWS default egress at creation.
  # Account-side drift/extra rules must be monitored during actual operations.
}
resource "aws_vpc_security_group_ingress_rule" "database" {
  for_each                     = aws_security_group.client
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = each.value.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  description                  = "${each.key} to private PostgreSQL"
}
resource "aws_vpc_security_group_egress_rule" "database" {
  for_each                     = aws_security_group.client
  security_group_id            = each.value.id
  referenced_security_group_id = aws_security_group.database.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  description                  = "Only private database access; runtime endpoint egress remains separate"
}

resource "aws_kms_key" "database" {
  description             = "${local.name} PostgreSQL storage/backups"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root }, Action = "kms:*", Resource = "*" },
      { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
      Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
      { Sid = "KeyControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:CreateGrant", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) } } },
      { Sid = "RejectNonRDSCryptography", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) }, StringNotEqualsIfExists = { "kms:ViaService" = "rds.${var.region}.amazonaws.com" } } },
      { Sid = "RejectArbitraryGrants", Effect = "Deny", Principal = "*", Action = "kms:CreateGrant", Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root]) }, BoolIfExists = { "kms:GrantIsForAWSResource" = "false" } } },
      { Sid    = "RDSUseInThisAccount", Effect = "Allow", Principal = { AWS = local.root },
        Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = "*",
      Condition = { StringEquals = { "kms:ViaService" = "rds.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id } } },
      { Sid = "ServiceResourceGrants", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
      Action = ["kms:CreateGrant", "kms:ListGrants", "kms:RevokeGrant"], Resource = "*", Condition = { Bool = { "kms:GrantIsForAWSResource" = "true" } } }
    ]
  })
  lifecycle { prevent_destroy = true }
}
resource "aws_kms_alias" "database" {
  name          = "alias/${local.name}-database"
  target_key_id = aws_kms_key.database.key_id
}
resource "aws_db_subnet_group" "cell" {
  name       = local.name
  subnet_ids = [for subnet in aws_subnet.database : subnet.id]
}
resource "aws_db_parameter_group" "cell" {
  name_prefix = "${local.name}-"
  family      = "postgres18"
  parameter {
    name         = "rds.force_ssl"
    value        = "1"
    apply_method = "pending-reboot"
  }
  # Do not enable general statement logging of confidential documentation.
  parameter {
    name  = "log_statement"
    value = "none"
  }
  parameter {
    name  = "log_min_error_statement"
    value = "panic"
  }
  parameter {
    name  = "log_error_verbosity"
    value = "terse"
  }
  lifecycle { create_before_destroy = true }
}
resource "aws_db_instance" "cell" {
  identifier                  = local.name
  db_name                     = "saveit"
  engine                      = "postgres"
  engine_version              = var.database_engine_version
  instance_class              = var.database_instance_class
  allocated_storage           = var.database_storage_gib
  storage_type                = "gp3"
  storage_encrypted           = true
  kms_key_id                  = aws_kms_key.database.arn
  username                    = "saveit_bootstrap"
  manage_master_user_password = true
  # AWS manages this separate bootstrap secret with its Secrets Manager key.
  # No secret version/value/password resource, input, data lookup or output.
  db_subnet_group_name        = aws_db_subnet_group.cell.name
  vpc_security_group_ids      = [aws_security_group.database.id]
  parameter_group_name        = aws_db_parameter_group.cell.name
  publicly_accessible         = false
  port                        = 5432
  network_type                = "IPV4"
  multi_az                    = var.environment == "production"
  backup_retention_period     = 35
  backup_window               = "03:00-04:00"
  maintenance_window          = "sun:05:00-sun:06:00"
  copy_tags_to_snapshot       = true
  delete_automated_backups    = false
  deletion_protection         = true
  skip_final_snapshot         = false
  final_snapshot_identifier   = "${local.name}-final"
  auto_minor_version_upgrade  = true
  allow_major_version_upgrade = false
  apply_immediately           = false
  lifecycle { prevent_destroy = true }
}
