mock_provider "aws" {
  override_during = plan
  mock_resource "aws_kms_key" {
    defaults = { arn = "arn:aws:kms:us-east-1:111122223333:key/11111111-1111-1111-1111-111111111111" }
  }
}

variables {
  account_id         = "111122223333"
  region             = "us-east-1"
  key_administrators = ["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator"]
  state_operators    = ["arn:aws:iam::111122223333:role/SaveITStateOperator"]
}

run "private_encrypted_versioned_state" {
  command = plan
  assert {
    condition     = aws_s3_bucket_public_access_block.state.block_public_acls && aws_s3_bucket_public_access_block.state.block_public_policy && aws_s3_bucket_public_access_block.state.ignore_public_acls && aws_s3_bucket_public_access_block.state.restrict_public_buckets && !aws_s3_bucket.state.force_destroy
    error_message = "State must remain private and must not permit force-destroy."
  }
  assert {
    condition     = aws_s3_bucket_ownership_controls.state.rule[0].object_ownership == "BucketOwnerEnforced" && aws_s3_bucket_versioning.state.versioning_configuration[0].status == "Enabled" && aws_kms_key.state.enable_key_rotation && aws_kms_key.state.deletion_window_in_days == 30
    error_message = "Owner enforcement, retained versions and rotating recoverable key configuration are required."
  }
  assert {
    condition     = one(aws_s3_bucket_server_side_encryption_configuration.state.rule).apply_server_side_encryption_by_default[0].sse_algorithm == "aws:kms" && one(aws_s3_bucket_server_side_encryption_configuration.state.rule).apply_server_side_encryption_by_default[0].kms_master_key_id == aws_kms_key.state.arn && output.backend.use_lockfile && output.backend.encrypt
    error_message = "Backend defaults must use this KMS key and native lock files."
  }
  assert {
    condition     = length([for statement in jsondecode(aws_s3_bucket_policy.state.policy).Statement : statement if statement.Sid == "ReadWriteState" && !contains(statement.Action, "s3:DeleteObject") && length(statement.Resource) == 2]) == 1 && length([for statement in jsondecode(aws_s3_bucket_policy.state.policy).Statement : statement if statement.Sid == "ManageLocks" && contains(statement.Action, "s3:DeleteObject") && alltrue([for arn in statement.Resource : endswith(arn, ".tflock")])]) == 1
    error_message = "Normal state writers may delete only lock files, never the state key."
  }
  assert {
    condition     = alltrue([for sid in ["RequireTLS", "ApprovedPrincipalsOnly", "RejectConflictingAlgorithm", "RejectConflictingKey", "RejectCustomerKeys"] : length([for statement in jsondecode(aws_s3_bucket_policy.state.policy).Statement : statement if statement.Sid == sid && statement.Effect == "Deny"]) == 1]) && length([for statement in jsondecode(aws_kms_key.state.policy).Statement : statement if statement.Sid == "StateThroughS3" && statement.Condition.StringEquals["kms:ViaService"] == "s3.us-east-1.amazonaws.com" && statement.Condition.StringEquals["kms:CallerAccount"] == "111122223333"]) == 1
    error_message = "State access must fail closed on transport/principal/encryption conflicts and constrain service key use."
  }
}

run "explicit_access_boundaries" {
  command = plan
  assert {
    condition     = one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RequireTLS"]).Condition.Bool["aws:SecureTransport"] == "false" && toset(one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "ApprovedPrincipalsOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:role/SaveITStateOperator", "arn:aws:iam::111122223333:root"])
    error_message = "Transport denial and the exact administrator/operator/root allowlist must be enforced."
  }
  assert {
    condition     = tolist(one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "ProtectStateVersions"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == tolist(["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"]) && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RejectConflictingKey"]).Condition.StringNotEquals["s3:x-amz-server-side-encryption-aws-kms-key-id"] == aws_kms_key.state.arn && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RejectCustomerKeys"]).Condition.Null["s3:x-amz-server-side-encryption-customer-algorithm"] == "false"
    error_message = "State history deletion and encryption choices require explicit policy enforcement."
  }
  assert {
    condition     = one([for s in jsondecode(aws_kms_key.state.policy).Statement : s if s.Sid == "RejectDirectOperatorCryptography"]).Condition.StringNotEqualsIfExists["kms:ViaService"] == "s3.us-east-1.amazonaws.com" && tolist(one([for s in jsondecode(aws_kms_key.state.policy).Statement : s if s.Sid == "RejectOtherBucketCryptography"]).Condition.StringNotLikeIfExists["kms:EncryptionContext:aws:s3:arn"]) == tolist(["arn:aws:s3:::saveit-tfstate-111122223333-us-east-1", "arn:aws:s3:::saveit-tfstate-111122223333-us-east-1/*"])
    error_message = "Broader IAM grants must not bypass the ordinary operator S3/bucket key-use boundary."
  }
}

run "reserve_control_plane_and_explicit_kms_selection" {
  command = plan
  assert {
    condition     = one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "BucketControlPlaneAdministratorsOnly"]).Effect == "Deny" && toset(one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "BucketControlPlaneAdministratorsOnly"]).NotAction) == toset(["s3:Get*", "s3:List*"]) && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "BucketControlPlaneAdministratorsOnly"]).Resource == "arn:aws:s3:::saveit-tfstate-111122223333-us-east-1" && toset(one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "BucketControlPlaneAdministratorsOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(concat(tolist(var.key_administrators), ["arn:aws:iam::111122223333:root"]))
    error_message = "Bucket policy/lifecycle/versioning/encryption mutation must be administrator-only."
  }
  assert {
    condition     = one([for s in jsondecode(aws_kms_key.state.policy).Statement : s if s.Sid == "KeyControlPlaneAdministratorsOnly"]).Effect == "Deny" && toset(one([for s in jsondecode(aws_kms_key.state.policy).Statement : s if s.Sid == "KeyControlPlaneAdministratorsOnly"]).NotAction) == toset(["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*", "kms:Get*", "kms:List*"]) && toset(one([for s in jsondecode(aws_kms_key.state.policy).Statement : s if s.Sid == "KeyControlPlaneAdministratorsOnly"]).Condition.ArnNotEquals["aws:PrincipalArn"]) == toset(["arn:aws:iam::111122223333:role/SaveITInfrastructureAdministrator", "arn:aws:iam::111122223333:root"])
    error_message = "Broader ordinary-role IAM grants must not permit key-policy/grant/destructive administration."
  }
  assert {
    condition     = one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RequireKeyWithExplicitKMS"]).Effect == "Deny" && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RequireKeyWithExplicitKMS"]).Condition.StringEquals["s3:x-amz-server-side-encryption"] == "aws:kms" && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RequireKeyWithExplicitKMS"]).Condition.Null["s3:x-amz-server-side-encryption-aws-kms-key-id"] == "true" && one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RejectConflictingKey"]).Condition.Null["s3:x-amz-server-side-encryption-aws-kms-key-id"] == "false"
    error_message = "Explicit KMS without this key must fail while fully absent headers retain safe bucket defaults."
  }
}

# A narrow truth table for the documented header predicates, not an AWS IAM simulator.
# The adjacent assertions check the production statements' Deny/Null/operator shapes.
run "documented_encryption_header_cases" {
  command = plan
  assert {
    condition = alltrue([for request in [
      { algorithm = "", key = "", customer = false, denied = false },
      { algorithm = "aws:kms", key = aws_kms_key.state.arn, customer = false, denied = false },
      { algorithm = "aws:kms", key = "wrong-key", customer = false, denied = true },
      { algorithm = "aws:kms", key = "", customer = false, denied = true },
      { algorithm = "AES256", key = "", customer = false, denied = true },
      { algorithm = "", key = "", customer = true, denied = true }
      ] : (
      (request.algorithm != "" && request.algorithm != one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RejectConflictingAlgorithm"]).Condition.StringNotEquals["s3:x-amz-server-side-encryption"]) ||
      (request.key != "" && request.key != one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RejectConflictingKey"]).Condition.StringNotEquals["s3:x-amz-server-side-encryption-aws-kms-key-id"]) ||
      (request.algorithm == one([for s in jsondecode(aws_s3_bucket_policy.state.policy).Statement : s if s.Sid == "RequireKeyWithExplicitKMS"]).Condition.StringEquals["s3:x-amz-server-side-encryption"] && request.key == "") ||
      request.customer
    ) == request.denied])
    error_message = "Absent headers/exact key must retain defaults; conflicting or unspecified explicit KMS and customer keys must be denied."
  }
}

run "reject_foreign_operator" {
  command = plan
  variables { state_operators = ["arn:aws:iam::999900001111:role/ForeignOperator"] }
  expect_failures = [var.state_operators]
}

run "reject_wildcard_operator" {
  command = plan
  variables { state_operators = ["arn:aws:iam::111122223333:role/*"] }
  expect_failures = [var.state_operators]
}

run "require_key_administrator" {
  command = plan
  variables { key_administrators = [] }
  expect_failures = [var.key_administrators]
}

run "reject_noncommercial_partition" {
  command = plan
  variables { region = "cn-north-1" }
  expect_failures = [var.region]
}

run "reject_unbounded_operator_list" {
  command = plan
  variables { state_operators = [for index in range(17) : "arn:aws:iam::111122223333:role/Operator${index}"] }
  expect_failures = [var.state_operators]
}
