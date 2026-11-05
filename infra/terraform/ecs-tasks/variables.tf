variable "account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.account_id))
    error_message = "Supply an explicit twelve-digit account."
  }
}
variable "region" {
  type = string
  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[1-9]$", var.region)) && !startswith(var.region, "cn-")
    error_message = "Supply a reviewed commercial region."
  }
}
variable "cell" {
  type = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.cell))
    error_message = "Use a two-to-twelve character lowercase cell name."
  }
}
variable "environment" {
  type = string
  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "Choose staging or production."
  }
}
variable "identity" {
  description = "Workload-identity outputs; nonsecret selectors and declared readiness, never payloads or managed enforcement proof."
  nullable    = false
  type = object({
    session_key_binding_ready = bool
    log_key_binding_ready     = bool
    operational_log_keys      = map(string)
    session_key_binding       = object({ file_system_id = string, access_point_id = string })
    workloads = map(object({
      task_role_arn = string, execution_role_arn = string, repository_arn = string, log_group_arn = string
      secrets       = list(object({ name = string, valueFrom = string }))
    }))
  })
  validation {
    condition = try(var.identity.session_key_binding_ready && var.identity.log_key_binding_ready &&
      var.identity.session_key_binding.file_system_id == var.session_key_storage.file_system_id &&
      var.identity.session_key_binding.access_point_id == var.session_key_storage.access_point &&
      toset(keys(var.identity.operational_log_keys)) == toset(["api", "web", "worker", "migration", "access"]) &&
      length(distinct(values(var.identity.operational_log_keys))) == 5 && alltrue([for arn in values(var.identity.operational_log_keys) :
    can(regex("^arn:aws:kms:${var.region}:${var.account_id}:key/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", arn))]), false)
    error_message = "Bind both the session-key access point and five distinct operational log keys before defining encrypted compute."
  }
  validation {
    condition = alltrue([for name in ["api", "web", "worker"] : try(
      var.identity.workloads[name].task_role_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-${name}-task" &&
      var.identity.workloads[name].execution_role_arn == "arn:aws:iam::${var.account_id}:role/saveit-${var.cell}-${var.environment}-${name}-execution" &&
      var.identity.workloads[name].repository_arn == "arn:aws:ecr:${var.region}:${var.account_id}:repository/saveit/${var.cell}/${var.environment}/${name}" &&
    var.identity.workloads[name].log_group_arn == "arn:aws:logs:${var.region}:${var.account_id}:log-group:/saveit/${var.cell}/${var.environment}/${name}", false)])
    error_message = "Each application must bind its own exact same-account/region/cell roles, repository and log group."
  }
  validation {
    condition = try(length(var.identity.workloads.web.secrets) == 0 && alltrue([for name in ["api", "worker"] :
      length(var.identity.workloads[name].secrets) == length(distinct([for s in var.identity.workloads[name].secrets : s.name])) &&
      alltrue([for s in var.identity.workloads[name].secrets : can(regex("^arn:aws:secretsmanager:${var.region}:${var.account_id}:secret:saveit/${var.cell}/${var.environment}/${name}/config-[A-Za-z0-9]{6}:${s.name}::[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", s.valueFrom))]) &&
      length(distinct([for s in var.identity.workloads[name].secrets : join(":", concat(slice(split(":", s.valueFrom), 0, 7), [element(split(":", s.valueFrom), length(split(":", s.valueFrom)) - 1)]))])) == 1
    ]), false)
    error_message = "Web has no secrets. API/worker selectors must be unique fields of one own configuration ARN and one pinned canonical UUID, without stages or wildcard references."
  }
  validation {
    condition = try(
      toset([for s in var.identity.workloads.worker.secrets : s.name]) == toset(["ConnectionStrings__SearchWorker"]) &&
      anytrue([for count in range(5) : toset([for s in var.identity.workloads.api.secrets : s.name]) == toset(concat(
        ["ConnectionStrings__App", "Identity__Okta__ClientSecret", "Identity__KeyCertificateBase64", "Identity__KeyCertificatePassword", "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePassword"],
        flatten([for i in range(count) : ["Identity__PreviousKeyCertificates__${i}__Base64", "Identity__PreviousKeyCertificates__${i}__Password"]])
    ))]), false)
    error_message = "Require exact API session and transport certificate fields and zero-to-four contiguous retired session pairs, and only the worker connection field."
  }
}
variable "images" {
  type        = map(string)
  description = "Actual registry manifest digests after protected publication; synthetic test values are not publication/promotion evidence."
  validation {
    condition = toset(keys(var.images)) == toset(["api", "web", "worker"]) && alltrue([for name, image in var.images :
    can(regex("^${var.account_id}\\.dkr\\.ecr\\.${var.region}\\.amazonaws\\.com/saveit/${var.cell}/${var.environment}/${name}@sha256:[0-9a-f]{64}$", image))])
    error_message = "Supply three exact own-repository lowercase sha256 registry digest references; no mutable tags or image configuration IDs."
  }
}
variable "public_trust" {
  description = "Expected SHA256 of reviewed public CA release files baked into images. Hashes only, never PEM, private keys or secret values. Not signature/promotion or managed PKI evidence."
  nullable    = false
  type        = object({ api_ca_sha256 = string, database_ca_sha256 = string })
  validation {
    condition     = alltrue([for hash in [var.public_trust.api_ca_sha256, var.public_trust.database_ca_sha256] : can(regex("^[0-9a-f]{64}$", hash))])
    error_message = "Require canonical lowercase SHA256 metadata for both public CA release files."
  }
}
variable "session_key_storage" {
  nullable = false
  type = object({
    file_system_id = string, access_point = string, container_path = string, container_uid = number
    task_volume = object({
      fileSystemId        = string, rootDirectory = string, transitEncryption = string
      authorizationConfig = object({ accessPointId = string, iam = string })
    })
  })
  validation {
    condition = (can(regex("^fs-[0-9a-f]{17}$", var.session_key_storage.file_system_id)) &&
      can(regex("^fsap-[0-9a-f]{17}$", var.session_key_storage.access_point)) &&
      var.session_key_storage.container_path == "/var/saveit/keys" && var.session_key_storage.container_uid == 1654 &&
      var.session_key_storage.task_volume.fileSystemId == var.session_key_storage.file_system_id &&
      var.session_key_storage.task_volume.rootDirectory == "/" && var.session_key_storage.task_volume.transitEncryption == "ENABLED" &&
      var.session_key_storage.task_volume.authorizationConfig.accessPointId == var.session_key_storage.access_point &&
    var.session_key_storage.task_volume.authorizationConfig.iam == "ENABLED")
    error_message = "Require the exact nonroot IAM/TLS access-point volume contract from key-storage."
  }
}
variable "runtime" {
  description = "Nonsecret deployment metadata only. No arbitrary environment/command inputs. Networking and trusted ingress must be designed before launch."
  nullable    = false
  type        = object({ public_origin = string, okta_issuer = string, okta_client_id = string, okta_tenant_id = string, worker_tenants = list(string) })
  validation {
    condition = (length(var.runtime.public_origin) <= 253 && can(regex("^https://[a-z0-9]+([.-][a-z0-9]+)*\\.[a-z]{2,63}$", var.runtime.public_origin)) &&
      length(var.runtime.okta_issuer) <= 512 && can(regex("^https://[a-z0-9]+([.-][a-z0-9]+)*\\.[a-z]{2,63}(/oauth2/[A-Za-z0-9_-]+)?$", var.runtime.okta_issuer)) &&
      can(regex("^[A-Za-z0-9_-]{1,128}$", var.runtime.okta_client_id)) &&
      can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", var.runtime.okta_tenant_id)) && var.runtime.okta_tenant_id != "00000000-0000-0000-0000-000000000000" &&
      length(var.runtime.worker_tenants) >= 1 && length(var.runtime.worker_tenants) <= 100 && length(distinct(var.runtime.worker_tenants)) == length(var.runtime.worker_tenants) &&
    alltrue([for id in var.runtime.worker_tenants : can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id)) && id != "00000000-0000-0000-0000-000000000000"]))
    error_message = "Require bounded HTTPS DNS origins, client metadata and a unique nonempty canonical tenant allowlist; no URLs with credentials/query/fragment."
  }
}
