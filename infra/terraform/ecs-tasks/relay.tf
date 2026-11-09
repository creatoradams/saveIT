# Optional isolated task metadata. No service, network attachment or launch authority.
variable "relay" {
  default = null
  type = object({
    roles            = object({ task_arn = string, execution_arn = string })
    configuration    = object({ arn = string, version_id = string, key_arn = string })
    operational_log  = object({ arn = string, name = string, key_arn = string, retention_days = number, execution_role_arn = string })
    image_repository = object({ arn = string, url = string, pull_execution_roles = list(string) })
    binding_ready    = bool
    network          = object({ vpc_id = string, application_subnet_ids = list(string), security_group_id = string })
    image            = string
    web_ca_sha256    = string
  })
  description = "Bound relay-identity outputs plus runtime-endpoints.relay_network, deployment registry manifest digest and reviewed web public CA-file hash. No private values or launch approval."
  validation {
    condition = var.relay == null ? true : try(
      var.relay.binding_ready && var.api_client_trust != null &&
      var.relay.roles.task_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-relay-task" &&
      var.relay.roles.execution_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-relay-execution" &&
      can(regex("^arn:aws:secretsmanager:${var.region}:${var.account_id}:secret:saveit/${var.cell}/${var.environment}/relay/config-[A-Za-z0-9]{6}$", var.relay.configuration.arn)) &&
      can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.relay.configuration.version_id)) &&
      var.relay.operational_log.arn == "arn:aws:logs:${var.region}:${var.account_id}:log-group:/saveit/${var.cell}/${var.environment}/relay" &&
      var.relay.operational_log.name == "/saveit/${var.cell}/${var.environment}/relay" &&
      var.relay.operational_log.execution_role_arn == var.relay.roles.execution_arn &&
      contains([30, 60, 90, 120, 150, 180, 365], var.relay.operational_log.retention_days) &&
      alltrue([for arn in [var.relay.configuration.key_arn, var.relay.operational_log.key_arn] : can(regex("^arn:aws:kms:${var.region}:${var.account_id}:key/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", arn))]) &&
      var.relay.configuration.key_arn != var.relay.operational_log.key_arn &&
      !contains(values(var.identity.operational_log_keys), var.relay.configuration.key_arn) &&
      !contains(values(var.identity.operational_log_keys), var.relay.operational_log.key_arn) &&
      var.relay.image_repository.arn == "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/deployment" &&
      var.relay.image_repository.url == "${var.account_id}.dkr.ecr.${var.region}.amazonaws.com/saveit/${var.cell}/${var.environment}/deployment" &&
      length(var.relay.image_repository.pull_execution_roles) == 3 &&
      toset(var.relay.image_repository.pull_execution_roles) == toset([for name in ["migration", "access", "relay"] : "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-${name}-execution"]) &&
      can(regex("^${var.account_id}\\.dkr\\.ecr\\.${var.region}\\.amazonaws\\.com/saveit/${var.cell}/${var.environment}/deployment@sha256:[0-9a-f]{64}$", var.relay.image)) &&
    can(regex("^[0-9a-f]{64}$", var.relay.web_ca_sha256)), false)
    error_message = "Require bound canonical relay roles/configuration/version/separate keys/own log/staged repository readers and manifest digest, public web CA hash and configured API client admission."
  }
  validation {
    condition = var.relay == null ? true : try(
      can(regex("^vpc-[0-9a-f]{17}$", var.relay.network.vpc_id)) &&
      length(var.relay.network.application_subnet_ids) == 2 && length(distinct(var.relay.network.application_subnet_ids)) == 2 &&
      alltrue([for id in var.relay.network.application_subnet_ids : can(regex("^subnet-[0-9a-f]{17}$", id))]) &&
    can(regex("^sg-[0-9a-f]{17}$", var.relay.network.security_group_id)), false)
    error_message = "Bind the separate runtime-endpoints relay network output with explicit VPC, two distinct application subnets and its own group."
  }
}

locals { relay_origin = "https://relay.${var.cell}.${var.environment}.saveit.internal:8443" }

data "aws_ecr_repository" "relay" {
  count = var.relay == null ? 0 : 1
  name  = "saveit/${var.cell}/${var.environment}/deployment"
  lifecycle {
    postcondition {
      condition = try(self.arn == var.relay.image_repository.arn && self.repository_url == var.relay.image_repository.url &&
      self.image_tag_mutability == "IMMUTABLE" && one(self.encryption_configuration).encryption_type == "KMS", false)
      error_message = "Require the exact deployment immutable encrypted repository metadata; this is not protected promotion or actual pull evidence."
    }
  }
}
data "aws_cloudwatch_log_group" "relay" {
  count = var.relay == null ? 0 : 1
  name  = var.relay.operational_log.name
  lifecycle {
    postcondition {
      condition     = (self.arn == var.relay.operational_log.arn && self.kms_key_id == var.relay.operational_log.key_arn && self.retention_in_days == var.relay.operational_log.retention_days)
      error_message = "Require exact own encrypted finite-retention relay log metadata; not ingestion or immutable audit proof."
    }
  }
}
data "aws_security_group" "relay" {
  count = var.relay == null ? 0 : 1
  id    = var.relay.network.security_group_id
  lifecycle {
    postcondition {
      condition = (self.arn == "arn:aws:ec2:${var.region}:${var.account_id}:security-group/${var.relay.network.security_group_id}" && self.vpc_id == var.relay.network.vpc_id &&
      try(self.tags.Project, "") == "SaveIT" && try(self.tags.Cell, "") == var.cell && try(self.tags.Environment, "") == var.environment && try(self.tags.Process, "") == "relay")
      error_message = "Require the own same-account/region/cell/VPC relay group. This does not attach it or prove exclusive SG/rule enforcement."
    }
  }
}

resource "aws_ecs_task_definition" "relay" {
  count                    = var.relay == null ? 0 : 1
  family                   = "${local.name}-relay"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "512"
  memory                   = "1024"
  task_role_arn            = var.relay.roles.task_arn
  execution_role_arn       = var.relay.roles.execution_arn
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
  volume {
    name                = "runtime"
    configure_at_launch = false
  }
  container_definitions = jsonencode([{
    name             = "relay", image = var.relay.image, essential = true, user = "1654:1654", readonlyRootFilesystem = true
    privileged       = false, interactive = false, pseudoTerminal = false, stopTimeout = 30
    linuxParameters  = { capabilities = { drop = ["ALL"] } }
    entryPoint       = ["dotnet", "SaveIT.Deployment.dll"]
    command          = ["serve-api-relay-secrets", "https://${local.api_host}:8443", local.relay_origin, var.relay.configuration.arn, var.relay.configuration.version_id]
    environment      = [for name, value in { DOTNET_ENVIRONMENT = "Production", ASPNETCORE_ENVIRONMENT = "Production", DOTNET_EnableDiagnostics = "0", TMPDIR = "/var/saveit/runtime" } : { name = name, value = value }]
    secrets          = []
    mountPoints      = [{ sourceVolume = "runtime", containerPath = "/var/saveit/runtime", readOnly = false }]
    portMappings     = [{ containerPort = 8443, hostPort = 8443, protocol = "tcp" }]
    healthCheck      = { command = ["CMD", "dotnet", "SaveIT.Deployment.dll", "probe-api-relay"], interval = 10, timeout = 5, retries = 3, startPeriod = 30 }
    logConfiguration = { logDriver = "awslogs", options = { awslogs-group = var.relay.operational_log.name, awslogs-region = var.region, awslogs-stream-prefix = "relay", awslogs-create-group = "false", mode = "non-blocking", max-buffer-size = "4m" } }
  }])
  depends_on = [data.aws_iam_role.workload, data.aws_ecr_repository.relay, data.aws_cloudwatch_log_group.relay, data.aws_security_group.relay]
  lifecycle {
    postcondition {
      condition     = length(self.container_definitions) <= 24576
      error_message = "Keep relay ASCII container JSON within24KiB; no payload belongs here."
    }
  }
}

output "relay_task" {
  description = "Optional unapplied task and declared network/transport references. No service, SG attachment, running health, PKI or launch approval."
  value = var.relay == null ? null : {
    task_definition_arn = aws_ecs_task_definition.relay[0].arn
    network             = var.relay.network
    origin              = local.relay_origin
    api_origin          = "https://${local.api_host}:8443"
    port                = 8443
  }
}
