mock_provider "aws" { override_during = plan }
variables {
  account_id       = jsondecode(file("tests/binding.json")).account_id
  region           = jsondecode(file("tests/binding.json")).region
  cell             = jsondecode(file("tests/binding.json")).cell
  environment      = jsondecode(file("tests/binding.json")).environment
  task_definitions = jsondecode(file("tests/binding.json")).task_definitions
  api_discovery    = jsondecode(file("tests/binding.json")).api_discovery
  relay_task       = jsondecode(file("tests/binding.json")).relay_task
  relay_discovery  = jsondecode(file("tests/binding.json")).relay_discovery
}
override_data {
  override_during = plan
  target          = data.aws_vpc.cell
  values = {
    "id" : "vpc-00000000000000001",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
    "owner_id" : "111122223333",
    "default" : false,
    "enable_dns_support" : true,
    "enable_dns_hostnames" : true,
    "cidr_block" : "10.84.0.0/16",
    "cidr_block_associations" : [
      {
        "association_id" : "vpc-cidr-assoc-00000000000000001",
        "cidr_block" : "10.84.0.0/16",
        "state" : "associated"
      }
    ],
    "ipv6_cidr_block_associations" : [],
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_subnet.cell["application-0"]
  values = {
    "id" : "subnet-00000000000000001",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
    "vpc_id" : "vpc-00000000000000001",
    "availability_zone" : "us-east-1a",
    "map_public_ip_on_launch" : false,
    "ipv6_cidr_block" : "",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Tier" : "application"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_subnet.cell["application-1"]
  values = {
    "id" : "subnet-00000000000000002",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002",
    "vpc_id" : "vpc-00000000000000001",
    "availability_zone" : "us-east-1b",
    "map_public_ip_on_launch" : false,
    "ipv6_cidr_block" : "",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Tier" : "application"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["api"]
  values = {
    "id" : "sg-00000000000000002",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "api"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["web"]
  values = {
    "id" : "sg-00000000000000005",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000005",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "web"
    }
  }
}
override_data {
  override_during = plan
  target          = data.aws_security_group.client["worker"]
  values = {
    "id" : "sg-00000000000000003",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000003",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "worker"
    }
  }
}
override_data {
  target = data.aws_security_group.client["relay"]
  values = {
    "id" : "sg-00000000000000006",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000006",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "relay"
    }
  }
}

override_data {
  target = data.aws_service_discovery_dns_namespace.cell
  values = {
    "id" : "ns-0000000000000001",
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
    "hosted_zone" : "Z0000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging"
    }
  }
}

override_data {
  target = data.aws_route53_zone.cell
  values = {
    "zone_id" : "Z0000000000000001"
  }
}

override_data {
  target = data.aws_service_discovery_service.registry["api"]
  values = {
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001",
    "dns_config" : [
      {
        "namespace_id" : "ns-0000000000000001",
        "routing_policy" : "MULTIVALUE",
        "dns_records" : [
          {
            "type" : "A",
            "ttl" : 10
          }
        ]
      }
    ],
    "health_check_config" : [],
    "health_check_custom_config" : [
      {
        "failure_threshold" : 1
      }
    ],
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "api"
    }
  }
}

override_data {
  target = data.aws_service_discovery_service.registry["relay"]
  values = {
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000002",
    "dns_config" : [
      {
        "namespace_id" : "ns-0000000000000001",
        "routing_policy" : "MULTIVALUE",
        "dns_records" : [
          {
            "type" : "A",
            "ttl" : 10
          }
        ]
      }
    ],
    "health_check_config" : [],
    "health_check_custom_config" : [
      {
        "failure_threshold" : 1
      }
    ],
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "relay"
    }
  }
}

override_resource {
  override_during = plan
  target          = aws_ecs_cluster.cell
  values          = { arn = "arn:aws:ecs:us-east-1:111122223333:cluster/saveit-pilot-staging", id = "arn:aws:ecs:us-east-1:111122223333:cluster/saveit-pilot-staging" }
}

run "four_parked_private_services" {
  command = plan
  assert {
    condition     = toset(keys(aws_ecs_service.application)) == toset(["api", "web", "worker", "relay"]) && output.parked_services.desired_count == 0 && alltrue([for name, service in aws_ecs_service.application : service.name == "saveit-pilot-staging-${name}" && service.cluster == "arn:aws:ecs:us-east-1:111122223333:cluster/saveit-pilot-staging" && service.desired_count == 0 && service.scheduling_strategy == "REPLICA" && service.launch_type == "FARGATE" && service.platform_version == "1.4.0" && !service.enable_execute_command && !service.force_delete && !service.force_new_deployment && service.wait_for_steady_state && service.deployment_minimum_healthy_percent == 100 && service.deployment_maximum_percent == 200 && one(service.deployment_controller).type == "ECS" && one(service.deployment_circuit_breaker).enable && one(service.deployment_circuit_breaker).rollback && length(service.capacity_provider_strategy) == 0 && length(service.load_balancer) == 0 && length(service.service_connect_configuration) == 0 && length(service.vpc_lattice_configurations) == 0 && length(service.volume_configuration) == 0])
    error_message = "Four explicit parked Fargate services must never request running tasks or extra runtime/ingress/exec/capacity attachments."
  }
  assert {
    condition     = alltrue([for name, service in aws_ecs_service.application : service.task_definition == merge(var.task_definitions, { relay = var.relay_task.task_definition_arn })[name] && one(service.network_configuration).subnets == toset(var.api_discovery.application_subnet_ids) && !one(service.network_configuration).assign_public_ip && one(service.network_configuration).security_groups == toset([name == "relay" ? var.relay_task.network.security_group_id : var.api_discovery.client_security_groups[name]]) && !contains(one(service.network_configuration).security_groups, var.api_discovery.client_security_groups.deployment)])
    error_message = "Each service binds its own exact revision, the same private subnet pair and exactly its own process SG, never deployment or an extra group."
  }
  assert {
    condition     = alltrue([for name, service in aws_ecs_service.application : length(service.service_registries) == (contains(["api", "relay"], name) ? 1 : 0)]) && one(aws_ecs_service.application["api"].service_registries).registry_arn == var.api_discovery.service_arn && one(aws_ecs_service.application["relay"].service_registries).registry_arn == var.relay_discovery.service_arn && alltrue([for name in ["api", "relay"] : one(aws_ecs_service.application[name].service_registries).container_name == null && one(aws_ecs_service.application[name].service_registries).container_port == null && one(aws_ecs_service.application[name].service_registries).port == null]) && length(aws_ecs_cluster.cell.service_connect_defaults) == 0 && one(aws_ecs_cluster.cell.setting).value == "disabled" && data.aws_route53_zone.cell.name == "pilot.staging.saveit.internal" && data.aws_route53_zone.cell.private_zone && data.aws_route53_zone.cell.vpc_id == var.api_discovery.vpc_id
    error_message = "Only API/relay bind the exact private A registries with no SRV ports; namespace-zone observation uses name/private/VPC filtering."
  }
}

run "bound_parked_service_fixture" {
  command = plan
  variables {
    task_definitions = { for name in ["api", "web", "worker"] : name => one([for service in jsondecode(file("tests/compute-parked-observations.json")).services.services : service.taskDefinition if service.serviceName == "saveit-pilot-staging-${name}"]) }
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, {
      client_security_groups = { api = "sg-00000000000000001", web = "sg-00000000000000002", worker = "sg-00000000000000003", deployment = "sg-00000000000000004" }
    })
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { task_definition_arn = one([for service in jsondecode(file("tests/compute-parked-observations.json")).services.services : service.taskDefinition if service.serviceName == "saveit-pilot-staging-relay"]) })
  }
  override_data {
    target = data.aws_security_group.client["api"]
    values = { id = "sg-00000000000000001", arn = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000001", vpc_id = "vpc-00000000000000001", tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging", Process = "api" } }
  }
  override_data {
    target = data.aws_security_group.client["web"]
    values = { id = "sg-00000000000000002", arn = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002", vpc_id = "vpc-00000000000000001", tags = { Project = "SaveIT", Cell = "pilot", Environment = "staging", Process = "web" } }
  }
  assert {
    condition = (alltrue([for observed in jsondecode(file("tests/compute-parked-observations.json")).services.services : one([
      for name, service in aws_ecs_service.application : (
        service.cluster == observed.clusterArn && service.task_definition == observed.taskDefinition && service.desired_count == observed.desiredCount &&
        service.launch_type == observed.launchType && service.platform_version == observed.platformVersion && service.scheduling_strategy == observed.schedulingStrategy &&
        service.enable_execute_command == observed.enableExecuteCommand && service.enable_ecs_managed_tags == observed.enableECSManagedTags && service.propagate_tags == observed.propagateTags &&
        service.availability_zone_rebalancing == observed.availabilityZoneRebalancing &&
        one(service.deployment_controller).type == observed.deploymentController.type &&
        service.deployment_minimum_healthy_percent == observed.deploymentConfiguration.minimumHealthyPercent && service.deployment_maximum_percent == observed.deploymentConfiguration.maximumPercent &&
        one(service.deployment_circuit_breaker).enable == observed.deploymentConfiguration.deploymentCircuitBreaker.enable && one(service.deployment_circuit_breaker).rollback == observed.deploymentConfiguration.deploymentCircuitBreaker.rollback &&
        one(service.network_configuration).subnets == toset(observed.networkConfiguration.awsvpcConfiguration.subnets) &&
        one(service.network_configuration).security_groups == toset(observed.networkConfiguration.awsvpcConfiguration.securityGroups) &&
        one(service.network_configuration).assign_public_ip == (observed.networkConfiguration.awsvpcConfiguration.assignPublicIp == "ENABLED") &&
        toset([for registry in service.service_registries : registry.registry_arn]) == toset([for registry in observed.serviceRegistries : registry.registryArn]) &&
        alltrue([for registry in service.service_registries : registry.container_name == null && registry.container_port == null && registry.port == null]) &&
        service.tags == tomap({ Process = name }) &&
        length(service.load_balancer) == 0 && length(service.capacity_provider_strategy) == 0 && length(service.ordered_placement_strategy) == 0 && length(service.placement_constraints) == 0 &&
        length(service.service_connect_configuration) == 0 && length(service.vpc_lattice_configurations) == 0 && length(service.volume_configuration) == 0
      ) if service.name == observed.serviceName
      ])]) && aws_ecs_cluster.cell.arn == one(jsondecode(file("tests/compute-parked-observations.json")).cluster.clusters).clusterArn &&
      aws_ecs_cluster.cell.name == one(jsondecode(file("tests/compute-parked-observations.json")).cluster.clusters).clusterName &&
      one(aws_ecs_cluster.cell.setting).name == "containerInsights" && one(aws_ecs_cluster.cell.setting).value == "disabled" &&
    length(aws_ecs_cluster.cell.configuration) == 0 && length(aws_ecs_cluster.cell.service_connect_defaults) == 0)
    error_message = "Bind the exact producer-controlled parked service, network, registry and deployment settings to the independent acquisition fixture. Computed statuses/counts/roles/tags remain synthetic observations, not provider/IAM/fencing evidence."
  }
}

run "reject_latest_task_alias" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { api = "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-api" })
  }
  expect_failures = [var.task_definitions]
}

run "reject_zero_task_revision" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { api = replace(jsondecode(file("tests/binding.json")).task_definitions.api, ":7", ":0") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_noncanonical_task_revision" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { api = replace(jsondecode(file("tests/binding.json")).task_definitions.api, ":7", ":007") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_overflow_task_revision" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { api = replace(jsondecode(file("tests/binding.json")).task_definitions.api, ":7", ":2147483648") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_foreign_task_account" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { api = replace(jsondecode(file("tests/binding.json")).task_definitions.api, "111122223333", "999988887777") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_foreign_task_region" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { web = replace(jsondecode(file("tests/binding.json")).task_definitions.web, "us-east-1", "us-west-2") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_wrong_task_cell" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { worker = replace(jsondecode(file("tests/binding.json")).task_definitions.worker, "pilot", "other") })
  }
  expect_failures = [var.task_definitions]
}

run "reject_borrowed_task_family" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { worker = jsondecode(file("tests/binding.json")).task_definitions.api })
  }
  expect_failures = [var.task_definitions]
}

run "reject_operator_service" {
  command = plan
  variables {
    task_definitions = merge(jsondecode(file("tests/binding.json")).task_definitions, { access = "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-access:1" })
  }
  expect_failures = [var.task_definitions]
}

run "reject_borrowed_relay_task" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { task_definition_arn = jsondecode(file("tests/binding.json")).task_definitions.api })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_latest_alias" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { task_definition_arn = replace(jsondecode(file("tests/binding.json")).relay_task.task_definition_arn, ":10", "") })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_revision_overflow" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { task_definition_arn = replace(jsondecode(file("tests/binding.json")).relay_task.task_definition_arn, ":10", ":2147483648") })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_target_confusion" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { origin = jsondecode(file("tests/binding.json")).api_discovery.origin })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_plaintext_port" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { port = 8080 })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_other_vpc" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { vpc_id = "vpc-00000000000000002" }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_other_subnet_set" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { application_subnet_ids = ["subnet-00000000000000001", "subnet-00000000000000003"] }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_duplicate_subnets" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { application_subnet_ids = ["subnet-00000000000000001", "subnet-00000000000000001"] }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_borrowed_api_group" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { security_group_id = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.api }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_borrowed_web_group" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { security_group_id = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.web }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_borrowed_worker_group" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { security_group_id = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.worker }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_relay_borrowed_deployment_group" {
  command = plan
  variables {
    relay_task = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { security_group_id = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.deployment }) })
  }
  expect_failures = [var.relay_task]
}

run "reject_foreign_namespace" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { namespace_arn = replace(jsondecode(file("tests/binding.json")).api_discovery.namespace_arn, "111122223333", "999988887777") })
  }
  expect_failures = [var.api_discovery]
}

run "reject_foreign_api_registry" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { service_arn = replace(jsondecode(file("tests/binding.json")).api_discovery.service_arn, "us-east-1", "us-west-2") })
  }
  expect_failures = [var.api_discovery]
}

run "reject_wrong_api_origin" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { origin = jsondecode(file("tests/binding.json")).relay_task.origin })
  }
  expect_failures = [var.api_discovery]
}

run "reject_api_extra_subnet" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { application_subnet_ids = concat(jsondecode(file("tests/binding.json")).api_discovery.application_subnet_ids, ["subnet-00000000000000003"]) })
  }
  expect_failures = [var.api_discovery]
}

run "reject_api_duplicate_groups" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { client_security_groups = merge(jsondecode(file("tests/binding.json")).api_discovery.client_security_groups, { web = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.api }) })
  }
  expect_failures = [var.api_discovery]
}

run "reject_api_missing_worker_group" {
  command = plan
  variables {
    api_discovery = merge(jsondecode(file("tests/binding.json")).api_discovery, { client_security_groups = { for name, id in jsondecode(file("tests/binding.json")).api_discovery.client_security_groups : name => id if name != "worker" } })
  }
  expect_failures = [var.api_discovery]
}

run "reject_shared_relay_registry" {
  command = plan
  variables {
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { service_arn = jsondecode(file("tests/binding.json")).api_discovery.service_arn })
  }
  expect_failures = [var.relay_discovery]
}

run "reject_relay_other_namespace" {
  command = plan
  variables {
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { namespace_id = "ns-0000000000000002" })
  }
  expect_failures = [var.relay_discovery]
}

run "reject_relay_registry_target_confusion" {
  command = plan
  variables {
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { api_origin = jsondecode(file("tests/binding.json")).relay_task.origin })
  }
  expect_failures = [var.relay_discovery]
}

run "reject_relay_registry_other_group" {
  command = plan
  variables {
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { security_group_id = jsondecode(file("tests/binding.json")).api_discovery.client_security_groups.web })
  }
  expect_failures = [var.relay_discovery]
}

run "reject_relay_registry_other_subnets" {
  command = plan
  variables {
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { application_subnet_ids = ["subnet-00000000000000001", "subnet-00000000000000003"] })
  }
  expect_failures = [var.relay_discovery]
}

run "foreign_vpc_owner" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "999988887777",
      "default" : false,
      "enable_dns_support" : true,
      "enable_dns_hostnames" : true,
      "cidr_block" : "10.84.0.0/16",
      "cidr_block_associations" : [
        {
          "association_id" : "vpc-cidr-assoc-00000000000000001",
          "cidr_block" : "10.84.0.0/16",
          "state" : "associated"
        }
      ],
      "ipv6_cidr_block_associations" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}

run "default_vpc" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "111122223333",
      "default" : true,
      "enable_dns_support" : true,
      "enable_dns_hostnames" : true,
      "cidr_block" : "10.84.0.0/16",
      "cidr_block_associations" : [
        {
          "association_id" : "vpc-cidr-assoc-00000000000000001",
          "cidr_block" : "10.84.0.0/16",
          "state" : "associated"
        }
      ],
      "ipv6_cidr_block_associations" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}

run "dns_disabled" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "111122223333",
      "default" : false,
      "enable_dns_support" : false,
      "enable_dns_hostnames" : true,
      "cidr_block" : "10.84.0.0/16",
      "cidr_block_associations" : [
        {
          "association_id" : "vpc-cidr-assoc-00000000000000001",
          "cidr_block" : "10.84.0.0/16",
          "state" : "associated"
        }
      ],
      "ipv6_cidr_block_associations" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}

run "second_vpc_range" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "111122223333",
      "default" : false,
      "enable_dns_support" : true,
      "enable_dns_hostnames" : true,
      "cidr_block" : "10.84.0.0/16",
      "cidr_block_associations" : [
        {
          "cidr_block" : "10.84.0.0/16"
        },
        {
          "cidr_block" : "10.85.0.0/16"
        }
      ],
      "ipv6_cidr_block_associations" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}

run "wrong_vpc_cell" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "111122223333",
      "default" : false,
      "enable_dns_support" : true,
      "enable_dns_hostnames" : true,
      "cidr_block" : "10.84.0.0/16",
      "cidr_block_associations" : [
        {
          "association_id" : "vpc-cidr-assoc-00000000000000001",
          "cidr_block" : "10.84.0.0/16",
          "state" : "associated"
        }
      ],
      "ipv6_cidr_block_associations" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "other",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}

run "public_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
      "vpc_id" : "vpc-00000000000000001",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : true,
      "ipv6_cidr_block" : "",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}

run "database_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
      "vpc_id" : "vpc-00000000000000001",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : false,
      "ipv6_cidr_block" : "",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "database"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}

run "wrong_subnet_vpc" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
      "vpc_id" : "vpc-00000000000000002",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : false,
      "ipv6_cidr_block" : "",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}

run "foreign_subnet_region" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-west-2:111122223333:subnet/subnet-00000000000000001",
      "vpc_id" : "vpc-00000000000000001",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : false,
      "ipv6_cidr_block" : "",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}

run "ipv6_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001",
      "vpc_id" : "vpc-00000000000000001",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : false,
      "ipv6_cidr_block" : "2001:db8::/64",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}

run "borrowed_web_group" {
  command = plan
  override_data {
    target = data.aws_security_group.client["web"]
    values = {
      "id" : "sg-00000000000000005",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000005",
      "vpc_id" : "vpc-00000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "deployment"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["web"]]
}

run "borrowed_relay_group" {
  command = plan
  override_data {
    target = data.aws_security_group.client["relay"]
    values = {
      "id" : "sg-00000000000000006",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000006",
      "vpc_id" : "vpc-00000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "web"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["relay"]]
}

run "foreign_worker_group" {
  command = plan
  override_data {
    target = data.aws_security_group.client["worker"]
    values = {
      "id" : "sg-00000000000000003",
      "arn" : "arn:aws:ec2:us-east-1:999988887777:security-group/sg-00000000000000003",
      "vpc_id" : "vpc-00000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "worker"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["worker"]]
}

run "wrong_group_vpc" {
  command = plan
  override_data {
    target = data.aws_security_group.client["api"]
    values = {
      "id" : "sg-00000000000000002",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002",
      "vpc_id" : "vpc-00000000000000002",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["api"]]
}

run "wrong_namespace_id" {
  command = plan
  override_data {
    target = data.aws_service_discovery_dns_namespace.cell
    values = {
      "id" : "ns-0000000000000002",
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
      "hosted_zone" : "Z0000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_dns_namespace.cell]
}

run "foreign_namespace_metadata" {
  command = plan
  override_data {
    target = data.aws_service_discovery_dns_namespace.cell
    values = {
      "id" : "ns-0000000000000001",
      "arn" : "arn:aws:servicediscovery:us-east-1:999988887777:namespace/ns-0000000000000001",
      "hosted_zone" : "Z0000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_dns_namespace.cell]
}

run "wrong_namespace_tags" {
  command = plan
  override_data {
    target = data.aws_service_discovery_dns_namespace.cell
    values = {
      "id" : "ns-0000000000000001",
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
      "hosted_zone" : "Z0000000000000001",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "other",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_dns_namespace.cell]
}

run "wrong_hosted_zone" {
  command = plan
  override_data {
    target = data.aws_route53_zone.cell
    values = {
      "zone_id" : "Z0000000000000002"
    }
  }
  expect_failures = [data.aws_route53_zone.cell]
}

run "missing_hosted_zone" {
  command = plan
  override_data {
    target = data.aws_route53_zone.cell
    values = {
      "zone_id" : ""
    }
  }
  expect_failures = [data.aws_route53_zone.cell]
}

run "wrong_api_registry_arn" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["api"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000003",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["api"]]
}

run "wrong_registry_record_type" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["relay"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000002",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "SRV",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "relay"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["relay"]]
}

run "wrong_registry_ttl" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["relay"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000002",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 60
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "relay"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["relay"]]
}

run "wrong_registry_namespace" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["api"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000002",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["api"]]
}

run "weighted_registry" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["api"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "WEIGHTED",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["api"]]
}

run "extra_registry_record" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["api"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            },
            {
              "type" : "AAAA",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["api"]]
}

run "missing_custom_health" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["relay"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000002",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "relay"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["relay"]]
}

run "wrong_custom_health" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["relay"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000002",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 2
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "relay"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["relay"]]
}

run "public_health_probe" {
  command = plan
  override_data {
    target = data.aws_service_discovery_service.registry["api"]
    values = {
      "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001",
      "dns_config" : [
        {
          "namespace_id" : "ns-0000000000000001",
          "routing_policy" : "MULTIVALUE",
          "dns_records" : [
            {
              "type" : "A",
              "ttl" : 10
            }
          ]
        }
      ],
      "health_check_config" : [
        {
          "type" : "HTTPS",
          "resource_path" : "/health/ready",
          "failure_threshold" : 1
        }
      ],
      "health_check_custom_config" : [
        {
          "failure_threshold" : 1
        }
      ],
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "api"
      }
    }
  }
  expect_failures = [data.aws_service_discovery_service.registry["api"]]
}

run "same_az_subnets" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-1"]
    values = {
      "id" : "subnet-00000000000000002",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002",
      "vpc_id" : "vpc-00000000000000001",
      "availability_zone" : "us-east-1a",
      "map_public_ip_on_launch" : false,
      "ipv6_cidr_block" : "",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [aws_ecs_cluster.cell]
}

run "subnet_order_is_set_equivalent" {
  command = plan
  variables {
    relay_task      = merge(jsondecode(file("tests/binding.json")).relay_task, { network = merge(jsondecode(file("tests/binding.json")).relay_task.network, { application_subnet_ids = reverse(jsondecode(file("tests/binding.json")).relay_task.network.application_subnet_ids) }) })
    relay_discovery = merge(jsondecode(file("tests/binding.json")).relay_discovery, { application_subnet_ids = reverse(jsondecode(file("tests/binding.json")).relay_discovery.application_subnet_ids) })
  }
  assert {
    condition     = alltrue([for service in aws_ecs_service.application : one(service.network_configuration).subnets == toset(var.api_discovery.application_subnet_ids) && service.desired_count == 0])
    error_message = "Equivalent subnet ordering must not change parked private placement."
  }
}
