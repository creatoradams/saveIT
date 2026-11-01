output "backend" {
  description = "Identifiers only: use reviewed named keys and native lock files; never supply credentials in backend files."
  value = {
    bucket              = local.bucket_name
    region              = var.region
    allowed_account_ids = [var.account_id]
    encrypt             = true
    kms_key_id          = aws_kms_key.state.arn
    use_lockfile        = true
    key                 = "bootstrap/terraform.tfstate"
  }
}
