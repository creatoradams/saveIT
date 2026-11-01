locals {
  bucket_name = "saveit-tfstate-${var.account_id}-${var.region}"
  bucket_arn  = "arn:aws:s3:::${local.bucket_name}"
  root_arn    = "arn:aws:iam::${var.account_id}:root"
  principals  = sort(distinct(concat(tolist(var.key_administrators), tolist(var.state_operators))))
  state_arns  = ["${local.bucket_arn}/bootstrap/terraform.tfstate", "${local.bucket_arn}/cells/*/terraform.tfstate"]
  lock_arns   = [for arn in local.state_arns : "${arn}.tflock"]
}

resource "aws_kms_key" "state" {
  description             = "SaveIT Terraform state"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "AccountRecoveryDelegation", Effect = "Allow", Principal = { AWS = local.root_arn }, Action = "kms:*", Resource = "*" },
      { Sid = "KeyAdministration", Effect = "Allow", Principal = { AWS = sort(tolist(var.key_administrators)) },
      Action = ["kms:Create*", "kms:Describe*", "kms:Enable*", "kms:List*", "kms:Put*", "kms:Update*", "kms:Revoke*", "kms:Disable*", "kms:Get*", "kms:Delete*", "kms:ScheduleKeyDeletion", "kms:CancelKeyDeletion"], Resource = "*" },
      { Sid = "KeyControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root_arn]) } } },
      { Sid    = "StateThroughS3", Effect = "Allow", Principal = { AWS = local.principals },
        Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"], Resource = "*",
        Condition = {
          StringEquals = { "kms:ViaService" = "s3.${var.region}.amazonaws.com", "kms:CallerAccount" = var.account_id }
          StringLike   = { "kms:EncryptionContext:aws:s3:arn" = [local.bucket_arn, "${local.bucket_arn}/*"] }
      } },
      { Sid = "RejectDirectOperatorCryptography", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root_arn]) }, StringNotEqualsIfExists = { "kms:ViaService" = "s3.${var.region}.amazonaws.com" } } },
      { Sid = "RejectOtherBucketCryptography", Effect = "Deny", Principal = "*", Action = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*"], Resource = "*",
      Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root_arn]) }, StringNotLikeIfExists = { "kms:EncryptionContext:aws:s3:arn" = [local.bucket_arn, "${local.bucket_arn}/*"] } } }
    ]
  })
  lifecycle { prevent_destroy = true }
}

resource "aws_s3_bucket" "state" {
  bucket        = local.bucket_name
  force_destroy = false
  lifecycle { prevent_destroy = true }
}

resource "aws_s3_bucket_ownership_controls" "state" {
  bucket = aws_s3_bucket.state.id
  rule { object_ownership = "BucketOwnerEnforced" }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration { status = "Enabled" }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    bucket_key_enabled = true
    apply_server_side_encryption_by_default {
      sse_algorithm     = "aws:kms"
      kms_master_key_id = aws_kms_key.state.arn
    }
  }
}

resource "aws_s3_bucket_policy" "state" {
  bucket = aws_s3_bucket.state.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Sid = "RequireTLS", Effect = "Deny", Principal = "*", Action = "s3:*", Resource = [local.bucket_arn, "${local.bucket_arn}/*"], Condition = { Bool = { "aws:SecureTransport" = "false" } } },
      { Sid = "ApprovedPrincipalsOnly", Effect = "Deny", Principal = "*", Action = "s3:*", Resource = [local.bucket_arn, "${local.bucket_arn}/*"], Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(local.principals, [local.root_arn]) } } },
      { Sid = "BucketControlPlaneAdministratorsOnly", Effect = "Deny", Principal = "*", NotAction = ["s3:Get*", "s3:List*"], Resource = local.bucket_arn, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root_arn]) } } },
      { Sid = "ListState", Effect = "Allow", Principal = { AWS = local.principals }, Action = ["s3:ListBucket", "s3:ListBucketVersions", "s3:GetBucketLocation"], Resource = local.bucket_arn },
      { Sid = "ReadWriteState", Effect = "Allow", Principal = { AWS = local.principals }, Action = ["s3:GetObject", "s3:GetObjectVersion", "s3:PutObject"], Resource = local.state_arns },
      { Sid = "ProtectStateVersions", Effect = "Deny", Principal = "*", Action = ["s3:DeleteObject", "s3:DeleteObjectVersion"], Resource = local.state_arns, Condition = { ArnNotEquals = { "aws:PrincipalArn" = concat(sort(tolist(var.key_administrators)), [local.root_arn]) } } },
      { Sid = "ManageLocks", Effect = "Allow", Principal = { AWS = local.principals }, Action = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"], Resource = local.lock_arns },
      { Sid = "RejectConflictingAlgorithm", Effect = "Deny", Principal = "*", Action = "s3:PutObject", Resource = "${local.bucket_arn}/*",
      Condition = { StringNotEquals = { "s3:x-amz-server-side-encryption" = "aws:kms" }, Null = { "s3:x-amz-server-side-encryption" = "false" } } },
      { Sid = "RejectConflictingKey", Effect = "Deny", Principal = "*", Action = "s3:PutObject", Resource = "${local.bucket_arn}/*",
      Condition = { StringNotEquals = { "s3:x-amz-server-side-encryption-aws-kms-key-id" = aws_kms_key.state.arn }, Null = { "s3:x-amz-server-side-encryption-aws-kms-key-id" = "false" } } },
      { Sid = "RequireKeyWithExplicitKMS", Effect = "Deny", Principal = "*", Action = "s3:PutObject", Resource = "${local.bucket_arn}/*",
      Condition = { StringEquals = { "s3:x-amz-server-side-encryption" = "aws:kms" }, Null = { "s3:x-amz-server-side-encryption-aws-kms-key-id" = "true" } } },
      { Sid = "RejectCustomerKeys", Effect = "Deny", Principal = "*", Action = "s3:PutObject", Resource = "${local.bucket_arn}/*", Condition = { Null = { "s3:x-amz-server-side-encryption-customer-algorithm" = "false" } } }
    ]
  })
  depends_on = [aws_s3_bucket_public_access_block.state]
}
