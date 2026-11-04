output "task_definitions" {
  description = "Unapplied application task-definition references only, not running services or promotion evidence."
  value       = { for name, definition in aws_ecs_task_definition.application : name => definition.arn }
}
