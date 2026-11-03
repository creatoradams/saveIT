output "repositories" {
  description = "Private store references only; no published/promoted image or registry digest evidence."
  value = { for name, repository in aws_ecr_repository.images : name => {
    arn                  = repository.arn
    url                  = repository.repository_url
    pull_execution_roles = local.readers[name]
  } }
}
output "log_groups" {
  description = "Operational store metadata, not ingestion/redaction/query/alert or audit-archive evidence."
  value = { for name, group in aws_cloudwatch_log_group.workloads : name => {
    arn                = group.arn
    name               = group.name
    key_arn            = group.kms_key_id
    retention_days     = group.retention_in_days
    execution_role_arn = local.exec_arns[name]
  } }
}
