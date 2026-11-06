mock_provider "aws" { override_during = plan }
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
  override_during = plan
  target          = data.aws_security_group.client["deployment"]
  values = {
    "id" : "sg-00000000000000004",
    "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000004",
    "vpc_id" : "vpc-00000000000000001",
    "tags" : {
      "Project" : "SaveIT",
      "Cell" : "pilot",
      "Environment" : "staging",
      "Process" : "deployment"
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
override_resource {
  override_during = plan
  target          = aws_service_discovery_private_dns_namespace.cell
  values = {
    "id" : "ns-0000000000000001",
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
    "hosted_zone" : "Z0000000000000001"
  }
}
override_resource {
  override_during = plan
  target          = aws_service_discovery_service.api
  values = {
    "id" : "srv-0000000000000001",
    "arn" : "arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001"
  }
}
variables {
  account_id  = "111122223333"
  region      = "us-east-1"
  cell        = "pilot"
  environment = "staging"
  network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000002"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000005"
    }
  }
  api_transport = {
    "origin" : "https://api.pilot.staging.saveit.internal:8443",
    "port" : 8443,
    "server_tls_declared" : true
  }
}

run "fixed_private_dns_and_custom_health" {
  command = plan
  assert {
    condition     = aws_service_discovery_private_dns_namespace.cell.name == "pilot.staging.saveit.internal" && aws_service_discovery_private_dns_namespace.cell.vpc == var.network.vpc_id && aws_service_discovery_service.api.name == "api" && !aws_service_discovery_service.api.force_destroy && one(aws_service_discovery_service.api.dns_config).namespace_id == aws_service_discovery_private_dns_namespace.cell.id && one(aws_service_discovery_service.api.dns_config).routing_policy == "MULTIVALUE" && one(one(aws_service_discovery_service.api.dns_config).dns_records).type == "A" && one(one(aws_service_discovery_service.api.dns_config).dns_records).ttl == 10 && length(aws_service_discovery_service.api.health_check_custom_config) == 1 && one(aws_service_discovery_service.api.health_check_custom_config).failure_threshold == 1 && length(aws_service_discovery_service.api.health_check_config) == 0
    error_message = "Require private API IPv4 DNS, bounded TTL and ECS custom health, without public health probing or forced instance deletion."
  }
}
run "exact_web_api_tls_rules" {
  command = plan
  assert {
    condition     = aws_vpc_security_group_ingress_rule.api_https.security_group_id == var.network.client_security_groups.api && aws_vpc_security_group_ingress_rule.api_https.referenced_security_group_id == var.network.client_security_groups.web && aws_vpc_security_group_egress_rule.api_https.security_group_id == var.network.client_security_groups.web && aws_vpc_security_group_egress_rule.api_https.referenced_security_group_id == var.network.client_security_groups.api && alltrue([for r in [aws_vpc_security_group_ingress_rule.api_https, aws_vpc_security_group_egress_rule.api_https] : r.ip_protocol == "tcp" && r.from_port == 8443 && r.to_port == 8443 && r.cidr_ipv4 == null && r.cidr_ipv6 == null && r.prefix_list_id == null])
    error_message = "Only the exact web/API pair receives TCP8443; no CIDR/prefix-list/plaintext/general egress."
  }
}
run "output_is_scoped_registry_metadata" {
  command = plan
  assert {
    condition     = output.api_discovery.hostname == "api.pilot.staging.saveit.internal" && output.api_discovery.origin == var.api_transport.origin && output.api_discovery.port == 8443 && output.api_discovery.namespace_id == aws_service_discovery_private_dns_namespace.cell.id && output.api_discovery.service_arn == aws_service_discovery_service.api.arn && output.api_discovery.vpc_id == var.network.vpc_id && output.api_discovery.application_subnet_ids == var.network.application_subnet_ids && output.api_discovery.client_security_groups == var.network.client_security_groups
    error_message = "Return only the declared registry and network references, not launch or health readiness."
  }
}
run "shared_process_groups" {
  command = plan
  variables { network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000002"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000002"
    }
  } }
  expect_failures = [var.network]
}
run "missing_web_group" {
  command = plan
  variables { network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000002"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004"
    }
  } }
  expect_failures = [var.network]
}
run "extra_process_group" {
  command = plan
  variables { network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000002"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000005",
      "extra" : "sg-00000000000000006"
    }
  } }
  expect_failures = [var.network]
}
run "one_application_subnet" {
  command = plan
  variables { network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000005"
    }
  } }
  expect_failures = [var.network]
}
run "overlapping_subnets" {
  command = plan
  variables { network = {
    "vpc_id" : "vpc-00000000000000001",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000001"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000005"
    }
  } }
  expect_failures = [var.network]
}
run "malformed_vpc" {
  command = plan
  variables { network = {
    "vpc_id" : "default",
    "application_subnet_ids" : [
      "subnet-00000000000000001",
      "subnet-00000000000000002"
    ],
    "client_security_groups" : {
      "api" : "sg-00000000000000002",
      "worker" : "sg-00000000000000003",
      "deployment" : "sg-00000000000000004",
      "web" : "sg-00000000000000005"
    }
  } }
  expect_failures = [var.network]
}
run "plaintext" {
  command = plan
  variables { api_transport = {
    "origin" : "http://api.pilot.staging.saveit.internal:8443",
    "port" : 8443,
    "server_tls_declared" : true
  } }
  expect_failures = [var.api_transport]
}
run "wrong_cell" {
  command = plan
  variables { api_transport = {
    "origin" : "https://api.other.staging.saveit.internal:8443",
    "port" : 8443,
    "server_tls_declared" : true
  } }
  expect_failures = [var.api_transport]
}
run "wrong_port" {
  command = plan
  variables { api_transport = {
    "origin" : "https://api.pilot.staging.saveit.internal:8443",
    "port" : 8080,
    "server_tls_declared" : true
  } }
  expect_failures = [var.api_transport]
}
run "tls_disabled" {
  command = plan
  variables { api_transport = {
    "origin" : "https://api.pilot.staging.saveit.internal:8443",
    "port" : 8443,
    "server_tls_declared" : false
  } }
  expect_failures = [var.api_transport]
}
run "foreign_vpc_account" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "999900001111",
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
run "foreign_vpc_arn" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-west-2:111122223333:vpc/vpc-00000000000000001",
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
  expect_failures = [data.aws_vpc.cell]
}
run "default_vpc" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_vpc.cell
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
    override_during = plan
    target          = data.aws_vpc.cell
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
run "dns_hostnames_disabled" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_vpc.cell
    values = {
      "id" : "vpc-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001",
      "owner_id" : "111122223333",
      "default" : false,
      "enable_dns_support" : true,
      "enable_dns_hostnames" : false,
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
run "foreign_vpc_cell" {
  command = plan
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
        "Cell" : "other",
        "Environment" : "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}
run "extra_vpc_cidr" {
  command = plan
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
        },
        {
          "association_id" : "vpc-cidr-assoc-00000000000000002",
          "cidr_block" : "10.85.0.0/16",
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
run "public_subnet" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_subnet.cell["application-0"]
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
run "foreign_subnet_vpc" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_subnet.cell["application-0"]
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
run "foreign_subnet_account" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_subnet.cell["application-0"]
    values = {
      "id" : "subnet-00000000000000001",
      "arn" : "arn:aws:ec2:us-east-1:999900001111:subnet/subnet-00000000000000001",
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
    override_during = plan
    target          = data.aws_subnet.cell["application-0"]
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
run "database_subnet_tier" {
  command = plan
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
        "Tier" : "database"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}
run "foreign_subnet_cell" {
  command = plan
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
        "Cell" : "other",
        "Environment" : "staging",
        "Tier" : "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}
run "same_application_az" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_subnet.cell["application-1"]
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
  expect_failures = [aws_service_discovery_private_dns_namespace.cell]
}
run "wrong_web_process" {
  command = plan
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
        "Process" : "worker"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["web"]]
}
run "foreign_api_account" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_security_group.client["api"]
    values = {
      "id" : "sg-00000000000000002",
      "arn" : "arn:aws:ec2:us-east-1:999900001111:security-group/sg-00000000000000002",
      "vpc_id" : "vpc-00000000000000001",
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
run "foreign_web_vpc" {
  command = plan
  override_data {
    override_during = plan
    target          = data.aws_security_group.client["web"]
    values = {
      "id" : "sg-00000000000000005",
      "arn" : "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000005",
      "vpc_id" : "vpc-00000000000000002",
      "tags" : {
        "Project" : "SaveIT",
        "Cell" : "pilot",
        "Environment" : "staging",
        "Process" : "web"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["web"]]
}
run "foreign_worker_environment" {
  command = plan
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
        "Environment" : "production",
        "Process" : "worker"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["worker"]]
}
