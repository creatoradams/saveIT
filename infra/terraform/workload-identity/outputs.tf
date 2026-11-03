output "workloads" {
  description = "Role/image/log/secret selector metadata only. No compute, secret population or injection has occurred."
  value = { for name in local.workloads : name => {
    task_role_arn      = aws_iam_role.task[name].arn
    execution_role_arn = aws_iam_role.execution[name].arn
    repository_arn     = local.repositories[name]
    log_group_arn      = local.log_groups[name]
    secrets = [for field in local.fields[name] : {
      name      = field
      valueFrom = "${try(aws_secretsmanager_secret.configuration[name].arn, "")}:${field}::${lookup(var.configuration_versions, name, "")}"
    }]
  } }
}
output "configuration" {
  description = "Protected population targets and pinned version metadata, never payloads. Custodian identity permissions/lifecycle remain separate."
  value = { for name in local.configured : name => {
    arn        = aws_secretsmanager_secret.configuration[name].arn
    key_arn    = aws_kms_key.configuration[name].arn
    version_id = lookup(var.configuration_versions, name, "")
    fields     = local.fields[name]
  } }
}
output "api_task_role_arn" {
  description = "Use for the separate key-storage definition after the identity bootstrap is actually applied."
  value       = aws_iam_role.task["api"].arn
}
output "session_key_binding_ready" {
  description = "Future compute must require true after metadata checks, plus its own IAM/TLS mount/runtime configuration guards. This is not mount evidence."
  value       = var.session_key_storage != null
  depends_on  = [data.aws_efs_file_system.ring, data.aws_efs_access_point.ring]
}
output "log_key_binding_ready" {
  description = "Declared five-key binding only, not managed key-policy, awslogs ingestion or runtime enforcement evidence. Future encrypted compute must require true."
  value       = var.operational_log_keys != null
}
