output "network" {
  description = "Unapplied private network identifiers for future reviewed compute wiring."
  value = {
    vpc_id                  = aws_vpc.cell.id
    application_subnet_ids  = [for subnet in aws_subnet.application : subnet.id]
    database_subnet_ids     = [for subnet in aws_subnet.database : subnet.id]
    client_security_groups  = { for name, group in aws_security_group.client : name => group.id }
    database_security_group = aws_security_group.database.id
  }
}
output "database" {
  description = "Endpoint/CA/key metadata only; credentials must never enter Terraform. No runtime or managed recovery is verified."
  value = {
    address               = aws_db_instance.cell.address
    port                  = aws_db_instance.cell.port
    database              = aws_db_instance.cell.db_name
    ca                    = aws_db_instance.cell.ca_cert_identifier
    storage_key           = aws_kms_key.database.arn
    actual_engine_version = aws_db_instance.cell.engine_version_actual
  }
}
