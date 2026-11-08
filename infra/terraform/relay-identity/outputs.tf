output "roles" {
  description = "Distinct declared identities only. Null image binding explicitly denies all AWS actions; no role is migration/access/web authority."
  value       = { task_arn = aws_iam_role.task.arn, execution_arn = aws_iam_role.execution.arn }
}
output "configuration" {
  description = "Future whole-JSON SDK read target, not environment selectors, a payload, a populated version or a working loader."
  value = {
    arn    = aws_secretsmanager_secret.configuration.arn, key_arn = aws_kms_key.configuration.arn, version_id = var.configuration_version_id,
    fields = ["CertificateBase64", "CertificatePassword", "ApiCaSha256", "ClientLeafSha256", "ServerCertificateBase64", "ServerCertificatePassword", "ServerLeafSha256"]
  }
}
output "operational_log" {
  description = "Protected finite-retention log metadata; not ingestion/redaction/alert or immutable audit evidence."
  value       = { arn = aws_cloudwatch_log_group.relay.arn, name = aws_cloudwatch_log_group.relay.name, key_arn = aws_kms_key.logs.arn, retention_days = var.log_retention_days, execution_role_arn = aws_iam_role.execution.arn }
}
output "image_repository" {
  value       = var.image_repository
  description = "Exact declared store binding or null, never image promotion/actual pull evidence."
}
output "binding_ready" {
  value       = local.bound
  description = "Checked declared repository reader contract only. No endpoint, SDK loader, task, populated secret, IAM enforcement or launch readiness follows."
}
