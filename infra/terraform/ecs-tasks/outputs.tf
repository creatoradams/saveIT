output "task_definitions" {
  description = "Unapplied application task-definition references only, not running services or promotion evidence."
  value       = { for name, definition in aws_ecs_task_definition.application : name => definition.arn }
}
output "api_transport" {
  description = "Declared web-origin/API-port/server-TLS metadata for private-discovery; not live trust or client identity evidence."
  value = {
    origin              = local.environment.web.SAVEIT_API_ORIGIN
    port                = one(one(jsondecode(aws_ecs_task_definition.application["api"].container_definitions)).portMappings).containerPort
    server_tls_declared = local.environment.api.Deployment__ApiTls__Enabled == "true"
  }
}
