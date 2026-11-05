locals {
  name      = "saveit-${var.cell}-${var.environment}"
  workloads = toset(["api", "web", "worker"])
  api_host  = "api.${var.cell}.${var.environment}.saveit.internal"
  roles     = { for pair in setproduct(local.workloads, ["task", "execution"]) : "${pair[0]}-${pair[1]}" => "${local.name}-${pair[0]}-${pair[1]}" }
  trust = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "OnlyAccountRegionalECSTasks", Effect = "Allow", Action = "sts:AssumeRole", Principal = { Service = "ecs-tasks.amazonaws.com" },
      Condition = { StringEquals = { "aws:SourceAccount" = var.account_id }, ArnLike = { "aws:SourceArn" = "arn:aws:ecs:${var.region}:${var.account_id}:*" } }
    }]
  })
  environment = {
    api = {
      ASPNETCORE_ENVIRONMENT              = "Production", DOTNET_ENVIRONMENT = "Production", Deployment__ApiTls__Enabled = "true", DOTNET_EnableDiagnostics = "0"
      AllowedHosts                        = "${local.api_host};${trimprefix(var.runtime.public_origin, "https://")};127.0.0.1"
      DevelopmentIdentity__Enabled        = "false", Identity__Okta__Enabled = "true"
      Identity__Okta__Issuer              = var.runtime.okta_issuer, Identity__Okta__PublicOrigin = var.runtime.public_origin
      Identity__Okta__TenantId            = var.runtime.okta_tenant_id, Identity__Okta__ClientId = var.runtime.okta_client_id
      Identity__KeyDirectory              = "/var/saveit/keys", SAVEIT_HEALTH_PORT = "8080", SAVEIT_HEALTH_HOST = "127.0.0.1", TMPDIR = "/var/saveit/runtime"
      Deployment__Trust__DatabaseCaSha256 = var.public_trust.database_ca_sha256
    }
    web = {
      NODE_ENV             = "production", NEXT_TELEMETRY_DISABLED = "1", HOSTNAME = "0.0.0.0", PORT = "3000", TMPDIR = "/var/saveit/runtime"
      SAVEIT_API_ORIGIN    = "https://${local.api_host}:8443", SAVEIT_PUBLIC_ORIGIN = var.runtime.public_origin, SAVEIT_OKTA_ISSUER = var.runtime.okta_issuer
      SAVEIT_API_CA_SHA256 = var.public_trust.api_ca_sha256, NODE_EXTRA_CA_CERTS = "/opt/saveit/trust/api-ca.crt"
    }
    worker = merge({
      DOTNET_ENVIRONMENT                  = "Production", DOTNET_EnableDiagnostics = "0", SearchWorker__HealthPath = "/var/saveit/runtime/saveit-worker-health.json", TMPDIR = "/var/saveit/runtime"
      Deployment__Trust__DatabaseCaSha256 = var.public_trust.database_ca_sha256
    }, { for i, id in var.runtime.worker_tenants : "SearchWorker__Tenants__${i}" => id })
  }
  health = {
    api    = ["CMD", "dotnet", "SaveIT.Api.dll", "--healthcheck"]
    worker = ["CMD", "dotnet", "SaveIT.Workers.dll", "--healthcheck"]
    web    = ["CMD", "/usr/local/bin/node", "-e", "fetch('http://127.0.0.1:3000/health/ready',{signal:AbortSignal.timeout(3000)}).then(r=>process.exit(r.status===200?0:1)).catch(()=>process.exit(1))"]
  }
}

# These are metadata guards, not IAM simulation, drift-proof authorization or promotion.
data "aws_iam_role" "workload" {
  for_each = local.roles
  name     = each.value
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:iam::${var.account_id}:role/${each.value}" && self.path == "/" &&
        self.permissions_boundary == "arn:aws:iam::${var.account_id}:policy/${each.value}-boundary" &&
      try(jsondecode(self.assume_role_policy) == jsondecode(local.trust), false))
      error_message = "Require the exact same-account role, boundary reference and account/regional ECS-only trust contract. Policy content/transitive grants still require managed verification."
    }
  }
}
data "aws_ecr_repository" "image" {
  for_each = local.workloads
  name     = "saveit/${var.cell}/${var.environment}/${each.key}"
  lifecycle {
    postcondition {
      condition = (self.arn == var.identity.workloads[each.key].repository_arn &&
        startswith(var.images[each.key], "${self.repository_url}@sha256:") && self.image_tag_mutability == "IMMUTABLE" &&
      try(one(self.encryption_configuration).encryption_type, "") == "KMS")
      error_message = "Require the own immutable KMS-encrypted repository metadata; no tag or cross-repository reference."
    }
  }
}
data "aws_cloudwatch_log_group" "workload" {
  for_each = local.workloads
  name     = "/saveit/${var.cell}/${var.environment}/${each.key}"
  lifecycle {
    postcondition {
      condition = (self.arn == var.identity.workloads[each.key].log_group_arn &&
        self.kms_key_id == var.identity.operational_log_keys[each.key] &&
      contains([30, 60, 90, 120, 150, 180, 365], self.retention_in_days))
      error_message = "Require exact own encrypted operational logs with bounded finite retention. This is not an immutable tenant archive or ingestion proof."
    }
  }
}
data "aws_efs_file_system" "ring" {
  file_system_id = var.session_key_storage.file_system_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:elasticfilesystem:${var.region}:${var.account_id}:file-system/${var.session_key_storage.file_system_id}" && self.encrypted &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require the designated account/region/cell encrypted session-key filesystem."
    }
  }
}
data "aws_efs_access_point" "ring" {
  access_point_id = var.session_key_storage.access_point
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:elasticfilesystem:${var.region}:${var.account_id}:access-point/${var.session_key_storage.access_point}" && self.file_system_id == var.session_key_storage.file_system_id &&
        try(one(self.posix_user).uid, 0) == 1654 && try(one(self.posix_user).gid, 0) == 1654 &&
        try(one(self.root_directory).path, "") == "/saveit-session-keys" &&
        try(one(one(self.root_directory).creation_info).owner_uid, 0) == 1654 && try(one(one(self.root_directory).creation_info).owner_gid, 0) == 1654 &&
        contains(["0700", "700"], try(one(one(self.root_directory).creation_info).permissions, "")) &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment)
      error_message = "Require the exact nonroot private access point for this filesystem/cell."
    }
  }
}

resource "aws_ecs_task_definition" "application" {
  for_each                 = local.workloads
  family                   = "${local.name}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.key == "worker" ? "256" : "512"
  memory                   = "1024"
  task_role_arn            = var.identity.workloads[each.key].task_role_arn
  execution_role_arn       = var.identity.workloads[each.key].execution_role_arn
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
  volume {
    name                = "runtime"
    configure_at_launch = false
  }
  dynamic "volume" {
    for_each = each.key == "api" ? [var.session_key_storage] : []
    content {
      name                = "session-keys"
      configure_at_launch = false
      efs_volume_configuration {
        file_system_id     = volume.value.file_system_id
        root_directory     = "/"
        transit_encryption = "ENABLED"
        authorization_config {
          access_point_id = volume.value.access_point
          iam             = "ENABLED"
        }
      }
    }
  }
  container_definitions = jsonencode([{
    name            = each.key, image = var.images[each.key], essential = true, user = "1654:1654", readonlyRootFilesystem = true
    privileged      = false, interactive = false, pseudoTerminal = false, stopTimeout = 30
    linuxParameters = { capabilities = { drop = ["ALL"] } }
    environment     = [for name, value in local.environment[each.key] : { name = name, value = value }]
    secrets         = var.identity.workloads[each.key].secrets
    mountPoints = concat([{ sourceVolume = "runtime", containerPath = "/var/saveit/runtime", readOnly = false }], each.key == "api" ? [
      { sourceVolume = "session-keys", containerPath = "/var/saveit/keys", readOnly = false }
    ] : [])
    portMappings = each.key == "worker" ? [] : [{ containerPort = each.key == "api" ? 8443 : 3000, hostPort = each.key == "api" ? 8443 : 3000, protocol = "tcp" }]
    healthCheck  = { command = local.health[each.key], interval = 10, timeout = 5, retries = 3, startPeriod = 30 }
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group        = "/saveit/${var.cell}/${var.environment}/${each.key}", awslogs-region = var.region, awslogs-stream-prefix = each.key
        awslogs-create-group = "false", mode = "non-blocking", max-buffer-size = "4m"
      }
    }
  }])
  depends_on = [data.aws_iam_role.workload, data.aws_ecr_repository.image, data.aws_cloudwatch_log_group.workload, data.aws_efs_file_system.ring, data.aws_efs_access_point.ring]
  lifecycle {
    postcondition {
      condition     = length(self.container_definitions) <= 24576
      error_message = "Keep the ASCII container JSON within 24 KiB, leaving conservative headroom under ECS's 64 KiB task-definition limit."
    }
  }
}
