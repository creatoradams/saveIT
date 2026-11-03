locals {
  name             = "saveit-${var.cell}-${var.environment}"
  root             = "arn:aws:iam::${var.account_id}:root"
  admins           = concat(sort(tolist(var.key_administrators)), [local.root])
  workloads        = toset(["api", "web", "worker", "migration", "access"])
  images           = toset(["api", "web", "worker", "deployment"])
  exec_arns        = { for name in local.workloads : name => "arn:aws:iam::${var.account_id}:role/${local.name}-${name}-execution" }
  repository_names = { for name in local.images : name => "saveit/${var.cell}/${var.environment}/${name}" }
  repository_arns  = { for name, path in local.repository_names : name => "arn:aws:ecr:${var.region}:${var.account_id}:repository/${path}" }
  log_names        = { for name in local.workloads : name => "/saveit/${var.cell}/${var.environment}/${name}" }
  log_arns         = { for name, path in local.log_names : name => "arn:aws:logs:${var.region}:${var.account_id}:log-group:${path}" }
  readers          = { for name in local.images : name => name == "deployment" ? [local.exec_arns.migration, local.exec_arns.access] : [local.exec_arns[name]] }
  pull_actions     = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
  push_actions     = ["ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage"]
}

resource "aws_ecr_repository" "images" {
  for_each             = local.images
  name                 = local.repository_names[each.key]
  image_tag_mutability = "IMMUTABLE"
  force_delete         = false
  image_scanning_configuration { scan_on_push = true }
  encryption_configuration {
    encryption_type = "KMS"
    kms_key         = aws_kms_key.registry.arn
  }
  lifecycle { prevent_destroy = true }
}
resource "aws_ecr_repository_policy" "images" {
  for_each   = local.images
  repository = aws_ecr_repository.images[each.key].name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "RequireTLS", Effect = "Deny", Principal = "*", Action = "ecr:*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } },
      { Sid = "ExactImageReaders", Effect = "Deny", Principal = "*", Action = local.pull_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(local.readers[each.key], [var.publisher_role], local.admins) } } },
      { Sid = "ProtectedPublisherOnly", Effect = "Deny", Principal = "*", Action = local.push_actions, Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = var.publisher_role } } },
      { Sid = "PreserveImagesAndTags", Effect = "Deny", Principal = "*", Action = "ecr:BatchDeleteImage", Resource = "*" },
      { Sid = "RepositoryAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = concat(local.pull_actions, local.push_actions), Resource = "*", Condition = { ArnNotEquals = { "aws:PrincipalArn" = local.admins } } }
    ]
  })
  lifecycle {
    postcondition {
      condition     = length(self.policy) <= 10240
      error_message = "Repository policy exceeds the service policy limit; do not widen or truncate scope."
    }
  }
}

resource "aws_cloudwatch_log_group" "workloads" {
  for_each                    = local.workloads
  name                        = local.log_names[each.key]
  log_group_class             = "STANDARD"
  retention_in_days           = var.log_retention_days
  deletion_protection_enabled = true
  kms_key_id                  = aws_kms_key.logs[each.key].arn
  lifecycle { prevent_destroy = true }
}
