output "session_key_storage" {
  description = "Metadata for future task wiring, not a mounted or recovered key ring. Never contains a key/password/value."
  value = {
    file_system_id = aws_efs_file_system.ring.id
    access_point   = aws_efs_access_point.api.id
    storage_key    = aws_kms_key.ring.arn
    task_volume = {
      fileSystemId      = aws_efs_file_system.ring.id
      rootDirectory     = "/"
      transitEncryption = "ENABLED"
      authorizationConfig = {
        accessPointId = aws_efs_access_point.api.id
        iam           = "ENABLED"
      }
    }
    container_path = "/var/saveit/keys"
    container_uid  = 1654
  }
}
