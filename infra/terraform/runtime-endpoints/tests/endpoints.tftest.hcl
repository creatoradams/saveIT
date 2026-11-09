mock_provider "aws" { override_during = plan }
run "shared_route_table" {
  command = plan
  variables { network = merge(var.network, { database_route_table_id = var.network.application_route_table_id }) }
  expect_failures = [var.network]
}
run "overlapping_subnets" {
  command = plan
  variables { network = merge(var.network, { database_subnet_ids = var.network.application_subnet_ids }) }
  expect_failures = [var.network]
}
run "database_group_as_client" {
  command = plan
  variables { network = merge(var.network, { client_security_groups = merge(var.network.client_security_groups, { api = var.network.database_security_group }) }) }
  expect_failures = [var.network]
}
run "shared_process_groups" {
  command = plan
  variables { network = merge(var.network, { client_security_groups = merge(var.network.client_security_groups, { worker = var.network.client_security_groups.api }) }) }
  expect_failures = [var.network]
}
run "malformed_route_id" {
  command = plan
  variables { network = merge(var.network, { application_route_table_id = "default" }) }
  expect_failures = [var.network]
}
run "foreign_execution_role" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { execution_role_arn = "arn:aws:iam::999900001111:role/saveit-pilot-staging-api-execution" }) }) }) }
  expect_failures = [var.identity]
}
run "task_role_instead_of_execution" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { web = merge(var.identity.workloads.web, { execution_role_arn = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task" }) }) }) }
  expect_failures = [var.identity]
}
run "foreign_repository" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { api = merge(var.identity.workloads.api, { repository_arn = var.identity.workloads.web.repository_arn }) }) }) }
  expect_failures = [var.identity]
}
run "foreign_log" {
  command = plan
  variables { identity = merge(var.identity, { workloads = merge(var.identity.workloads, { worker = merge(var.identity.workloads.worker, { log_group_arn = var.identity.workloads.api.log_group_arn }) }) }) }
  expect_failures = [var.identity]
}
run "foreign_configuration" {
  command = plan
  variables { identity = merge(var.identity, { configuration = merge(var.identity.configuration, { api = merge(var.identity.configuration.api, { arn = var.identity.configuration.worker.arn }) }) }) }
  expect_failures = [var.identity]
}
run "stage_instead_of_uuid" {
  command = plan
  variables { identity = merge(var.identity, { configuration = merge(var.identity.configuration, { api = merge(var.identity.configuration.api, { version_id = "AWSCURRENT" }) }) }) }
  expect_failures = [var.identity]
}
run "web_configuration" {
  command = plan
  variables { identity = merge(var.identity, { configuration = merge(var.identity.configuration, { web = var.identity.configuration.api }) }) }
  expect_failures = [var.identity]
}
run "foreign_vpc_account" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id"                   = "vpc-00000000000000001"
      "arn"                  = "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001"
      "owner_id"             = "999900001111"
      "default"              = false
      "enable_dns_support"   = true
      "enable_dns_hostnames" = true
      "cidr_block"           = "10.84.0.0/16"
      "cidr_block_associations" = [{
        "association_id" = "vpc-cidr-assoc-00000000000000001"
        "cidr_block"     = "10.84.0.0/16"
        "state"          = "associated"
      }]
      "ipv6_cidr_block_associations" = []
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
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
      "id"                   = "vpc-00000000000000001"
      "arn"                  = "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001"
      "owner_id"             = "111122223333"
      "default"              = false
      "enable_dns_support"   = false
      "enable_dns_hostnames" = true
      "cidr_block"           = "10.84.0.0/16"
      "cidr_block_associations" = [{
        "association_id" = "vpc-cidr-assoc-00000000000000001"
        "cidr_block"     = "10.84.0.0/16"
        "state"          = "associated"
      }]
      "ipv6_cidr_block_associations" = []
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}
run "extra_vpc_cidr" {
  command = plan
  override_data {
    target = data.aws_vpc.cell
    values = {
      "id"                   = "vpc-00000000000000001"
      "arn"                  = "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001"
      "owner_id"             = "111122223333"
      "default"              = false
      "enable_dns_support"   = true
      "enable_dns_hostnames" = true
      "cidr_block"           = "10.84.0.0/16"
      "cidr_block_associations" = [{
        "association_id" = "vpc-cidr-assoc-00000000000000001"
        "cidr_block"     = "10.84.0.0/16"
        "state"          = "associated"
        }, {
        "association_id" = "vpc-cidr-assoc-00000000000000002"
        "cidr_block"     = "10.85.0.0/16"
        "state"          = "associated"
      }]
      "ipv6_cidr_block_associations" = []
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
      }
    }
  }
  expect_failures = [data.aws_vpc.cell]
}
run "public_application_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id"                      = "subnet-00000000000000001"
      "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
      "vpc_id"                  = "vpc-00000000000000001"
      "availability_zone"       = "us-east-1a"
      "map_public_ip_on_launch" = true
      "ipv6_cidr_block"         = ""
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}
run "foreign_subnet_vpc" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id"                      = "subnet-00000000000000001"
      "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
      "vpc_id"                  = "vpc-00000000000000002"
      "availability_zone"       = "us-east-1a"
      "map_public_ip_on_launch" = false
      "ipv6_cidr_block"         = ""
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}
run "wrong_subnet_tier" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-0"]
    values = {
      "id"                      = "subnet-00000000000000001"
      "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
      "vpc_id"                  = "vpc-00000000000000001"
      "availability_zone"       = "us-east-1a"
      "map_public_ip_on_launch" = false
      "ipv6_cidr_block"         = ""
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "database"
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
      "id"                      = "subnet-00000000000000001"
      "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
      "vpc_id"                  = "vpc-00000000000000001"
      "availability_zone"       = "us-east-1a"
      "map_public_ip_on_launch" = false
      "ipv6_cidr_block"         = "2001:db8::/64"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
    }
  }
  expect_failures = [data.aws_subnet.cell["application-0"]]
}
run "same_application_az" {
  command = plan
  override_data {
    target = data.aws_subnet.cell["application-1"]
    values = {
      "id"                      = "subnet-00000000000000002"
      "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002"
      "vpc_id"                  = "vpc-00000000000000001"
      "availability_zone"       = "us-east-1a"
      "map_public_ip_on_launch" = false
      "ipv6_cidr_block"         = ""
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
    }
  }
  expect_failures = [aws_vpc_endpoint.interface]
}
run "wrong_process_group" {
  command = plan
  override_data {
    target = data.aws_security_group.client["worker"]
    values = {
      "id"     = "sg-00000000000000003"
      "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000003"
      "vpc_id" = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Process"     = "api"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["worker"]]
}
run "foreign_group_vpc" {
  command = plan
  override_data {
    target = data.aws_security_group.client["api"]
    values = {
      "id"     = "sg-00000000000000002"
      "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002"
      "vpc_id" = "vpc-00000000000000002"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Process"     = "api"
      }
    }
  }
  expect_failures = [data.aws_security_group.client["api"]]
}
run "route_association_to_database_subnet" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["application"]
    values = {
      "id"       = "rtb-00000000000000001"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
      "routes" = []
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000003"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000004"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["application"]]
}
run "main_route_table_association" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["application"]
    values = {
      "id"       = "rtb-00000000000000001"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
      "routes" = []
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000001"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = true
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000002"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["application"]]
}
run "foreign_route_table_owner" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["database"]
    values = {
      "id"       = "rtb-00000000000000002"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000002"
      "owner_id" = "999900001111"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "database"
      }
      "routes" = []
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000003"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000004"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["database"]]
}
run "reject_internet_default" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["application"]
    values = {
      "id"       = "rtb-00000000000000001"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
      "routes" = [{
        "cidr_block" = "0.0.0.0/0"
        "gateway_id" = "igw-00000000000000001"
      }]
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000001"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000002"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["application"]]
}
run "reject_nat_default" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["database"]
    values = {
      "id"       = "rtb-00000000000000002"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000002"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "database"
      }
      "routes" = [{
        "cidr_block"     = "0.0.0.0/0"
        "nat_gateway_id" = "nat-00000000000000001"
      }]
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000003"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000004"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["database"]]
}
run "reject_ipv6_default" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["application"]
    values = {
      "id"       = "rtb-00000000000000001"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
      "routes" = [{
        "ipv6_cidr_block"        = "::/0"
        "egress_only_gateway_id" = "eigw-00000000000000001"
      }]
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000001"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000002"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["application"]]
}
run "reject_peering_route" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["application"]
    values = {
      "id"       = "rtb-00000000000000001"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "application"
      }
      "routes" = [{
        "cidr_block"                = "10.85.0.0/16"
        "vpc_peering_connection_id" = "pcx-00000000000000001"
      }]
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000001"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000002"
        "route_table_id"             = "rtb-00000000000000001"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["application"]]
}
run "reject_reported_service_route" {
  command = plan
  override_data {
    target = data.aws_route_table.cell["database"]
    values = {
      "id"       = "rtb-00000000000000002"
      "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000002"
      "owner_id" = "111122223333"
      "vpc_id"   = "vpc-00000000000000001"
      "tags" = {
        "Project"     = "SaveIT"
        "Cell"        = "pilot"
        "Environment" = "staging"
        "Tier"        = "database"
      }
      "routes" = [{
        "destination_prefix_list_id" = "pl-00000001"
        "vpc_endpoint_id"            = "vpce-00000000000000001"
      }]
      "associations" = [{
        "subnet_id"                  = "subnet-00000000000000003"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000001"
        }, {
        "subnet_id"                  = "subnet-00000000000000004"
        "route_table_id"             = "rtb-00000000000000002"
        "main"                       = false
        "gateway_id"                 = ""
        "route_table_association_id" = "rtbassoc-00000000000000002"
      }]
    }
  }
  expect_failures = [data.aws_route_table.cell["database"]]
}

run "private_endpoints_and_exact_client_paths" {
  command = plan
  assert {
    condition = toset(keys(aws_vpc_endpoint.interface)) == toset(["ecr-api", "ecr-dkr", "logs", "configuration"]) && alltrue([for name, e in aws_vpc_endpoint.interface :
      e.vpc_id == var.network.vpc_id && e.service_name == "com.amazonaws.us-east-1.${local.interfaces[name].service}" && e.vpc_endpoint_type == "Interface" &&
      e.private_dns_enabled && e.ip_address_type == "ipv4" && e.subnet_ids == toset(var.network.application_subnet_ids) &&
      e.security_group_ids == toset([aws_security_group.endpoint[name].id]) && one(e.dns_options).dns_record_ip_type == "ipv4"
    ])
    error_message = "Exactly four private DNS-enabled IPv4 interface endpoints must use only the designated application subnets and own endpoint groups."
  }
  assert {
    condition = (aws_vpc_endpoint.layers.vpc_id == var.network.vpc_id && aws_vpc_endpoint.layers.service_name == "com.amazonaws.us-east-1.s3" &&
      aws_vpc_endpoint.layers.vpc_endpoint_type == "Gateway" && aws_vpc_endpoint.layers.ip_address_type == "ipv4" &&
      aws_vpc_endpoint.layers.route_table_ids == toset([var.network.application_route_table_id]) &&
      !contains(aws_vpc_endpoint.layers.route_table_ids, var.network.database_route_table_id) &&
    length(aws_vpc_endpoint.layers.subnet_ids) == 0 && length(aws_vpc_endpoint.layers.security_group_ids) == 0)
    error_message = "Regional layer routing must be associated only with the separate application table, never database subnets/tables."
  }
  assert {
    condition = (length(aws_security_group.endpoint) == 4 && aws_security_group.web.vpc_id == var.network.vpc_id &&
      length(aws_vpc_security_group_ingress_rule.interface) == 15 && length(aws_vpc_security_group_egress_rule.interface) == 15 &&
      !contains(keys(aws_vpc_security_group_ingress_rule.interface), "configuration-web") &&
      alltrue([for key, r in aws_vpc_security_group_ingress_rule.interface :
        r.security_group_id == aws_security_group.endpoint[local.interface_paths[key].endpoint].id &&
        r.referenced_security_group_id == local.clients[local.interface_paths[key].client] && r.ip_protocol == "tcp" && r.from_port == 443 && r.to_port == 443 && r.cidr_ipv4 == null && r.cidr_ipv6 == null
      ]) &&
      alltrue([for key, r in aws_vpc_security_group_egress_rule.interface :
        r.security_group_id == local.clients[local.interface_paths[key].client] &&
        r.referenced_security_group_id == aws_security_group.endpoint[local.interface_paths[key].endpoint].id && r.ip_protocol == "tcp" && r.from_port == 443 && r.to_port == 443 && r.cidr_ipv4 == null && r.cidr_ipv6 == null
      ]) &&
      length(aws_vpc_security_group_egress_rule.layers) == 4 && alltrue([for name, r in aws_vpc_security_group_egress_rule.layers :
        r.security_group_id == local.clients[name] && r.prefix_list_id == "pl-00000001" && r.ip_protocol == "tcp" && r.from_port == 443 && r.to_port == 443 && r.cidr_ipv4 == null && r.cidr_ipv6 == null && r.referenced_security_group_id == null
    ]))
    error_message = "Only exact standalone443 pairs and regional S3 prefix-list paths are declared; web has no configuration/database path."
  }
}
run "ecr_roles_pull_only_own_repositories" {
  command = plan
  assert {
    condition = (aws_vpc_endpoint.interface["ecr-api"].policy == local.ecr_policy && aws_vpc_endpoint.interface["ecr-api"].policy == aws_vpc_endpoint.interface["ecr-dkr"].policy &&
      length(jsondecode(aws_vpc_endpoint.interface["ecr-api"].policy).Statement) == 14 &&
      jsonencode(jsondecode(local.ecr_policy).Statement[1]) == jsonencode({ Sid = "ExecutionIdentitiesOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = sort([for w in var.identity.workloads : w.execution_role_arn]) } } }) &&
      jsonencode(jsondecode(local.ecr_policy).Statement[2]) == jsonencode({ Sid = "PullActionsOnly", Effect = "Deny", Principal = "*", NotAction = ["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = "*" }) &&
      jsonencode(jsondecode(local.ecr_policy).Statement[3]) == jsonencode({ Sid = "AuthenticateExecutions", Effect = "Allow", Principal = "*", Action = "ecr:GetAuthorizationToken", Resource = "*", Condition = { ArnEquals = { "aws:PrincipalArn" = sort([for w in var.identity.workloads : w.execution_role_arn]) } } }) &&
      alltrue([for name, w in var.identity.workloads :
        jsonencode(one([for s in jsondecode(local.ecr_policy).Statement : s if s.Sid == "Pull${title(name)}"])) == jsonencode({ Sid = "Pull${title(name)}", Effect = "Allow", Principal = "*", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = w.repository_arn, Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } }) &&
        jsonencode(one([for s in jsondecode(local.ecr_policy).Statement : s if s.Sid == "RejectOther${title(name)}Repositories"])) == jsonencode({ Sid = "RejectOther${title(name)}Repositories", Effect = "Deny", Principal = "*", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], NotResource = w.repository_arn, Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } })
    ]))
    error_message = "Exact execution principals, authentication/pull actions and per-role explicit repository denies must survive additional identity/session grants."
  }
}
run "logs_are_own_stream_write_only" {
  command = plan
  assert {
    condition = (length(jsondecode(local.logs_policy).Statement) == 13 &&
      jsonencode(jsondecode(local.logs_policy).Statement[1]) == jsonencode({ Sid = "ExecutionIdentitiesOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = sort([for w in var.identity.workloads : w.execution_role_arn]) } } }) &&
      jsonencode(jsondecode(local.logs_policy).Statement[2]) == jsonencode({ Sid = "WriteStreamsOnly", Effect = "Deny", Principal = "*", NotAction = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "*" }) &&
      alltrue([for name, w in var.identity.workloads :
        jsonencode(one([for s in jsondecode(local.logs_policy).Statement : s if s.Sid == "Write${title(name)}Streams"])) == jsonencode({ Sid = "Write${title(name)}Streams", Effect = "Allow", Principal = "*", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "${w.log_group_arn}:log-stream:*", Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } }) &&
        jsonencode(one([for s in jsondecode(local.logs_policy).Statement : s if s.Sid == "RejectOther${title(name)}Streams"])) == jsonencode({ Sid = "RejectOther${title(name)}Streams", Effect = "Deny", Principal = "*", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], NotResource = "${w.log_group_arn}:log-stream:*", Condition = { ArnEquals = { "aws:PrincipalArn" = w.execution_role_arn } } })
    ]))
    error_message = "No log-query/control/foreign-stream authority belongs in these endpoint filters."
  }
}
run "configuration_exact_role_arn_version_and_missing_context_denies" {
  command = plan
  assert {
    condition = (length(jsondecode(local.secrets_policy).Statement) == 15 &&
      jsonencode(jsondecode(local.secrets_policy).Statement[1]) == jsonencode({ Sid = "ConfiguredExecutionsOnly", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { ArnNotEqualsIfExists = { "aws:PrincipalArn" = sort([for name in keys(var.identity.configuration) : var.identity.workloads[name].execution_role_arn]) } } }) &&
      !contains(jsondecode(local.secrets_policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.identity.workloads.web.execution_role_arn) &&
      jsonencode(jsondecode(local.secrets_policy).Statement[2]) == jsonencode({ Sid = "ReadValueOnly", Effect = "Deny", Principal = "*", NotAction = "secretsmanager:GetSecretValue", Resource = "*" }) &&
      alltrue([for name, c in var.identity.configuration :
        jsonencode(one([for s in jsondecode(local.secrets_policy).Statement : s if s.Sid == "Read${title(name)}Version"])) == jsonencode({ Sid = "Read${title(name)}Version", Effect = "Allow", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = c.arn, Condition = { ArnEquals = { "aws:PrincipalArn" = var.identity.workloads[name].execution_role_arn }, StringEquals = { "secretsmanager:VersionId" = c.version_id } } }) &&
        jsonencode(one([for s in jsondecode(local.secrets_policy).Statement : s if s.Sid == "RejectOther${title(name)}Configurations"])) == jsonencode({ Sid = "RejectOther${title(name)}Configurations", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", NotResource = c.arn, Condition = { ArnEquals = { "aws:PrincipalArn" = var.identity.workloads[name].execution_role_arn } } }) &&
        jsonencode(one([for s in jsondecode(local.secrets_policy).Statement : s if s.Sid == "RejectOther${title(name)}Versions"])) == jsonencode({ Sid = "RejectOther${title(name)}Versions", Effect = "Deny", Principal = "*", Action = "secretsmanager:GetSecretValue", Resource = c.arn, Condition = { ArnEquals = { "aws:PrincipalArn" = var.identity.workloads[name].execution_role_arn }, StringNotEqualsIfExists = { "secretsmanager:VersionId" = c.version_id } } })
    ]))
    error_message = "Only four own execution identities may read their exact pinned JSON version; web, wrong/missing version, other secrets and write/control actions remain explicitly excluded."
  }
}
run "tls_layer_bucket_bounds_and_policy_sizes" {
  command = plan
  assert {
    condition = (aws_vpc_endpoint.layers.policy == local.s3_policy &&
      aws_vpc_endpoint.interface["logs"].policy == local.logs_policy &&
    aws_vpc_endpoint.interface["configuration"].policy == local.secrets_policy)
    error_message = "Attach the reviewed policies to the actual service endpoints without a default/full-access substitution."
  }
  assert {
    condition = alltrue([for policy in [local.ecr_policy, local.logs_policy, local.secrets_policy, local.s3_policy] :
      jsonencode(jsondecode(policy).Statement[0]) == jsonencode({ Sid = "RejectInsecureTransport", Effect = "Deny", Principal = "*", Action = "*", Resource = "*", Condition = { BoolIfExists = { "aws:SecureTransport" = "false" } } }) && length(policy) < 18000
      ]) && jsonencode(slice(jsondecode(local.s3_policy).Statement, 1, 4)) == jsonencode([
      { Sid = "LayerReadOnly", Effect = "Deny", Principal = "*", NotAction = "s3:GetObject", Resource = "*" },
      { Sid = "RegionalLayerObjectsOnly", Effect = "Deny", Principal = "*", Action = "s3:GetObject", NotResource = "arn:aws:s3:::prod-us-east-1-starport-layer-bucket/*" },
      { Sid = "ECRPresignedLayers", Effect = "Allow", Principal = "*", Action = "s3:GetObject", Resource = "arn:aws:s3:::prod-us-east-1-starport-layer-bucket/*" }
    ])
    error_message = "Require complete TLS/action/bucket denies while admitting the documented presigned ECR layer path; no tenant buckets, writes or role-only principal assumption."
  }
}
override_data {
  target = data.aws_vpc.cell
  values = {
    "id"                   = "vpc-00000000000000001"
    "arn"                  = "arn:aws:ec2:us-east-1:111122223333:vpc/vpc-00000000000000001"
    "owner_id"             = "111122223333"
    "default"              = false
    "enable_dns_support"   = true
    "enable_dns_hostnames" = true
    "cidr_block"           = "10.84.0.0/16"
    "cidr_block_associations" = [{
      "association_id" = "vpc-cidr-assoc-00000000000000001"
      "cidr_block"     = "10.84.0.0/16"
      "state"          = "associated"
    }]
    "ipv6_cidr_block_associations" = []
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["application-0"]
  values = {
    "id"                      = "subnet-00000000000000001"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000001"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1a"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["application-1"]
  values = {
    "id"                      = "subnet-00000000000000002"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000002"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1b"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["database-0"]
  values = {
    "id"                      = "subnet-00000000000000003"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000003"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1a"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
  }
}
override_data {
  target = data.aws_subnet.cell["database-1"]
  values = {
    "id"                      = "subnet-00000000000000004"
    "arn"                     = "arn:aws:ec2:us-east-1:111122223333:subnet/subnet-00000000000000004"
    "vpc_id"                  = "vpc-00000000000000001"
    "availability_zone"       = "us-east-1b"
    "map_public_ip_on_launch" = false
    "ipv6_cidr_block"         = ""
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
  }
}
override_data {
  target = data.aws_security_group.client["api"]
  values = {
    "id"     = "sg-00000000000000002"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000002"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "api"
    }
  }
}
override_data {
  target = data.aws_security_group.client["worker"]
  values = {
    "id"     = "sg-00000000000000003"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000003"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "worker"
    }
  }
}
override_data {
  target = data.aws_security_group.client["deployment"]
  values = {
    "id"     = "sg-00000000000000004"
    "arn"    = "arn:aws:ec2:us-east-1:111122223333:security-group/sg-00000000000000004"
    "vpc_id" = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Process"     = "deployment"
    }
  }
}
override_data {
  target = data.aws_prefix_list.s3
  values = {
    "id"          = "pl-00000001"
    "name"        = "com.amazonaws.us-east-1.s3"
    "cidr_blocks" = ["52.216.0.0/15"]
  }
}
override_data {
  target = data.aws_route_table.cell["application"]
  values = {
    "id"       = "rtb-00000000000000001"
    "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000001"
    "owner_id" = "111122223333"
    "vpc_id"   = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "application"
    }
    "routes" = []
    "associations" = [{
      "subnet_id"                  = "subnet-00000000000000001"
      "route_table_id"             = "rtb-00000000000000001"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000001"
      }, {
      "subnet_id"                  = "subnet-00000000000000002"
      "route_table_id"             = "rtb-00000000000000001"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000002"
    }]
  }
}
override_data {
  target = data.aws_route_table.cell["database"]
  values = {
    "id"       = "rtb-00000000000000002"
    "arn"      = "arn:aws:ec2:us-east-1:111122223333:route-table/rtb-00000000000000002"
    "owner_id" = "111122223333"
    "vpc_id"   = "vpc-00000000000000001"
    "tags" = {
      "Project"     = "SaveIT"
      "Cell"        = "pilot"
      "Environment" = "staging"
      "Tier"        = "database"
    }
    "routes" = []
    "associations" = [{
      "subnet_id"                  = "subnet-00000000000000003"
      "route_table_id"             = "rtb-00000000000000002"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000001"
      }, {
      "subnet_id"                  = "subnet-00000000000000004"
      "route_table_id"             = "rtb-00000000000000002"
      "main"                       = false
      "gateway_id"                 = ""
      "route_table_association_id" = "rtbassoc-00000000000000002"
    }]
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.web
  values = {
    "id" = "sg-00000000000000005"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["ecr-api"]
  values = {
    "id" = "sg-00000000000000010"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["ecr-dkr"]
  values = {
    "id" = "sg-00000000000000011"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["logs"]
  values = {
    "id" = "sg-00000000000000012"
  }
}
override_resource {
  override_during = plan
  target          = aws_security_group.endpoint["configuration"]
  values = {
    "id" = "sg-00000000000000013"
  }
}
variables {
  account_id  = "111122223333"
  region      = "us-east-1"
  cell        = "pilot"
  environment = "staging"
  network = {
    "vpc_id"                     = "vpc-00000000000000001"
    "application_subnet_ids"     = ["subnet-00000000000000001", "subnet-00000000000000002"]
    "database_subnet_ids"        = ["subnet-00000000000000003", "subnet-00000000000000004"]
    "application_route_table_id" = "rtb-00000000000000001"
    "database_route_table_id"    = "rtb-00000000000000002"
    "database_security_group"    = "sg-00000000000000001"
    "client_security_groups" = {
      "api"        = "sg-00000000000000002"
      "worker"     = "sg-00000000000000003"
      "deployment" = "sg-00000000000000004"
    }
  }
  identity = {
    "workloads" = {
      "api" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/api"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/api"
      }
      "web" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/web"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/web"
      }
      "worker" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-worker-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/worker"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/worker"
      }
      "migration" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/migration"
      }
      "access" = {
        "execution_role_arn" = "arn:aws:iam::111122223333:role/saveit-pilot-staging-access-execution"
        "repository_arn"     = "arn:aws:ecr:us-east-1:111122223333:repository/saveit/pilot/staging/deployment"
        "log_group_arn"      = "arn:aws:logs:us-east-1:111122223333:log-group:/saveit/pilot/staging/access"
      }
    }
    "configuration" = {
      "api" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00"
        "version_id" = "10000000-0000-0000-0000-000000000001"
      }
      "worker" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/worker/config-Abcd01"
        "version_id" = "10000000-0000-0000-0000-000000000002"
      }
      "migration" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/migration/config-Abcd02"
        "version_id" = "10000000-0000-0000-0000-000000000003"
      }
      "access" = {
        "arn"        = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/access/config-Abcd03"
        "version_id" = "10000000-0000-0000-0000-000000000004"
      }
    }
  }
}

# Frozen hashes were calculated from the committed pre-change policies using only synthetic variables.
run "null_relay_preserves_committed_policy_bytes" {
  command = plan

  assert {
    condition = jsonencode({ ecr = sha256(local.ecr_policy), logs = sha256(local.logs_policy), configuration = sha256(local.secrets_policy), s3 = sha256(local.s3_policy) }) == jsonencode({
      ecr           = "462b7aada1cf6284c5208e62806febeca928ea1617337e5348a007cf85afeaa0"
      logs          = "abc5c77e36a3baa132362f3583abe8e015c4435f93477a25838ff6772d6d4f36"
      configuration = "6be05267c381bc0e6543e6ddc746e80ef724d5212dd03b53f77082e9a66ee4f0"
      s3            = "0ce6c8d4624f3bb29f44262a9596e3acfd0c618b8758cbe637535b96de34c7aa"
    }) && output.relay_admission == null
    error_message = "Null retains all four exact policy bytes from committed8b92135 and no relay admission. This is a mock contract, not applied revocation."
  }
}

run "relay_policy_only_admission" {
  command = plan
  variables { relay = jsondecode(file("tests/relay-binding.json")) }
  assert {
    condition = alltrue([for pair in [
      { actual = local.ecr_policy, expected = "ecr" },
      { actual = local.logs_policy, expected = "logs" },
      { actual = local.secrets_policy, expected = "configuration" }
    ] : jsonencode(jsondecode(pair.actual)) == jsonencode(jsondecode(file("tests/relay-policies.json"))[pair.expected])])
    error_message = "Require every action/principal/resource/condition and explicit deny in the independently authored bound policy fixture."
  }
  assert {
    condition = (aws_vpc_endpoint.interface["ecr-api"].policy == local.ecr_policy && aws_vpc_endpoint.interface["ecr-dkr"].policy == local.ecr_policy &&
      aws_vpc_endpoint.interface["logs"].policy == local.logs_policy && aws_vpc_endpoint.interface["configuration"].policy == local.secrets_policy &&
      sha256(aws_vpc_endpoint.layers.policy) == "0ce6c8d4624f3bb29f44262a9596e3acfd0c618b8758cbe637535b96de34c7aa" &&
    alltrue([for policy in [local.ecr_policy, local.logs_policy, local.secrets_policy] : length(policy) < 18000]))
    error_message = "Attach the full reviewed filters to actual endpoints; preserve the presigned layer policy and size headroom."
  }
  assert {
    condition = (length(aws_vpc_endpoint.interface) == 4 && length(aws_security_group.endpoint) == 4 &&
      length(aws_vpc_security_group_ingress_rule.interface) == 15 && length(aws_vpc_security_group_egress_rule.interface) == 15 && length(aws_vpc_security_group_egress_rule.layers) == 4 &&
      jsonencode(output.runtime_network.client_security_groups) == jsonencode(merge(var.network.client_security_groups, { web = "sg-00000000000000005" })) &&
    jsonencode(output.relay_admission) == jsonencode({ roles = var.relay.roles, configuration = var.relay.configuration, repository_arn = var.relay.image_repository.arn, log_group_arn = var.relay.operational_log.arn }))
    error_message = "Relay admission is policy metadata only: existing resources, four-group consumer contract and packet paths must remain unchanged."
  }
}
run "relay_roles_are_not_interchangeable" {
  command = plan
  variables { relay = jsondecode(file("tests/relay-binding.json")) }
  assert {
    condition = (alltrue([for policy in [local.ecr_policy, local.logs_policy] :
      contains(jsondecode(policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.relay.roles.execution_arn) &&
      !contains(jsondecode(policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.relay.roles.task_arn)]) &&
      contains(jsondecode(local.secrets_policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.relay.roles.task_arn) &&
      !contains(jsondecode(local.secrets_policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.relay.roles.execution_arn) &&
    !contains(jsondecode(local.secrets_policy).Statement[1].Condition.ArnNotEqualsIfExists["aws:PrincipalArn"], var.identity.workloads.web.execution_role_arn))
    error_message = "Only relay execution joins pull/log principal lists; only relay task joins configuration readers; web still has no configuration authority."
  }
  assert {
    condition = alltrue([for context in [
      { pin = var.relay.configuration.version_id, allowed = true },
      { pin = null, allowed = false }, { pin = "AWSCURRENT", allowed = false },
      { pin = "10000000-0000-0000-0000-000000000008", allowed = false }
      ] : context.allowed == (
      context.pin == one([for s in jsondecode(local.secrets_policy).Statement : s if s.Sid == "ReadRelayVersion"]).Condition.StringEquals["secretsmanager:VersionId"] &&
      !(context.pin != one([for s in jsondecode(local.secrets_policy).Statement : s if s.Sid == "RejectOtherRelayVersions"]).Condition.StringNotEqualsIfExists["secretsmanager:VersionId"])
    )])
    error_message = "Narrow request-version condition predicates require the pin and explicitly deny wrong/missing/stage-only values; this is not an AWS IAM simulation."
  }
}

run "unbound_relay" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { binding_ready = false }) }
  expect_failures = [var.relay]
}

run "null_relay_repository" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { image_repository = null }) }
  expect_failures = [var.relay]
}

run "crossed_relay_roles" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { roles = { task_arn = jsondecode(file("tests/relay-binding.json")).roles.execution_arn, execution_arn = jsondecode(file("tests/relay-binding.json")).roles.task_arn } }) }
  expect_failures = [var.relay]
}

run "legacy_execution_as_relay" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { roles = merge(jsondecode(file("tests/relay-binding.json")).roles, { execution_arn = var.identity.workloads.migration.execution_role_arn }) }) }
  expect_failures = [var.relay]
}

run "assumed_session_as_task" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { roles = merge(jsondecode(file("tests/relay-binding.json")).roles, { task_arn = "arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-relay-task/session" }) }) }
  expect_failures = [var.relay]
}

run "foreign_task_account" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { roles = merge(jsondecode(file("tests/relay-binding.json")).roles, { task_arn = "arn:aws:iam::999900001111:role/saveit-pilot-staging-relay-task" }) }) }
  expect_failures = [var.relay]
}

run "foreign_configuration_region" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = merge(jsondecode(file("tests/relay-binding.json")).configuration, { arn = replace(jsondecode(file("tests/relay-binding.json")).configuration.arn, "us-east-1", "us-west-2") }) }) }
  expect_failures = [var.relay]
}

run "legacy_configuration_as_relay" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = var.identity.configuration.api }) }
  expect_failures = [var.relay]
}

run "selector_configuration" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = merge(jsondecode(file("tests/relay-binding.json")).configuration, { arn = "${jsondecode(file("tests/relay-binding.json")).configuration.arn}:CertificateBase64::${jsondecode(file("tests/relay-binding.json")).configuration.version_id}" }) }) }
  expect_failures = [var.relay]
}

run "stage_relay_version" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = merge(jsondecode(file("tests/relay-binding.json")).configuration, { version_id = "AWSCURRENT" }) }) }
  expect_failures = [var.relay]
}

run "uppercase_relay_version" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = merge(jsondecode(file("tests/relay-binding.json")).configuration, { version_id = "10000000-0000-0000-0000-00000000000A" }) }) }
  expect_failures = [var.relay]
}

run "missing_relay_version" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { configuration = merge(jsondecode(file("tests/relay-binding.json")).configuration, { version_id = null }) }) }
  expect_failures = [var.relay]
}

run "foreign_relay_repository" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { image_repository = merge(jsondecode(file("tests/relay-binding.json")).image_repository, { arn = var.identity.workloads.api.repository_arn }) }) }
  expect_failures = [var.relay]
}

run "wrong_registry_url" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { image_repository = merge(jsondecode(file("tests/relay-binding.json")).image_repository, { url = replace(jsondecode(file("tests/relay-binding.json")).image_repository.url, "111122223333", "999900001111") }) }) }
  expect_failures = [var.relay]
}

run "missing_relay_store_reader" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { image_repository = merge(jsondecode(file("tests/relay-binding.json")).image_repository, { pull_execution_roles = [var.identity.workloads.migration.execution_role_arn, var.identity.workloads.access.execution_role_arn] }) }) }
  expect_failures = [var.relay]
}

run "duplicate_store_reader" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { image_repository = merge(jsondecode(file("tests/relay-binding.json")).image_repository, { pull_execution_roles = [var.identity.workloads.migration.execution_role_arn, var.identity.workloads.access.execution_role_arn, var.identity.workloads.access.execution_role_arn] }) }) }
  expect_failures = [var.relay]
}

run "legacy_log_as_relay" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { operational_log = merge(jsondecode(file("tests/relay-binding.json")).operational_log, { arn = var.identity.workloads.migration.log_group_arn }) }) }
  expect_failures = [var.relay]
}

run "task_as_log_execution" {
  command = plan
  variables { relay = merge(jsondecode(file("tests/relay-binding.json")), { operational_log = merge(jsondecode(file("tests/relay-binding.json")).operational_log, { execution_role_arn = jsondecode(file("tests/relay-binding.json")).roles.task_arn }) }) }
  expect_failures = [var.relay]
}

run "foreign_relay_namespace" {
  command = plan
  variables { relay = jsondecode(replace(jsonencode(jsondecode(file("tests/relay-binding.json"))), "pilot", "other")) }
  expect_failures = [var.relay]
}

run "foreign_relay_environment" {
  command = plan
  variables { relay = jsondecode(replace(jsonencode(jsondecode(file("tests/relay-binding.json"))), "staging", "production")) }
  expect_failures = [var.relay]
}

run "null_after_bound_retains_legacy_filters" {
  command = plan
  variables { relay = null }

  assert {
    condition = jsonencode({ ecr = sha256(local.ecr_policy), logs = sha256(local.logs_policy), configuration = sha256(local.secrets_policy), s3 = sha256(local.s3_policy) }) == jsonencode({
      ecr           = "462b7aada1cf6284c5208e62806febeca928ea1617337e5348a007cf85afeaa0"
      logs          = "abc5c77e36a3baa132362f3583abe8e015c4435f93477a25838ff6772d6d4f36"
      configuration = "6be05267c381bc0e6543e6ddc746e80ef724d5212dd03b53f77082e9a66ee4f0"
      s3            = "0ce6c8d4624f3bb29f44262a9596e3acfd0c618b8758cbe637535b96de34c7aa"
    }) && output.relay_admission == null
    error_message = "Null retains all four exact policy bytes from committed8b92135 and no relay admission. This is a mock contract, not applied revocation."
  }
}


run "network_bootstrap_creates_no_relay_group" {
  command = plan
  assert {
    condition     = length(aws_security_group.relay) == 0 && output.relay_network == null && length(local.endpoint_clients) == 4 && length(aws_vpc_security_group_ingress_rule.interface) == 15
    error_message = "Initial mode preserves the legacy resource graph and has no relay network binding."
  }
}
run "network_cannot_skip_relay_admission" {
  command = plan
  variables { relay_network_enabled = true }
  expect_failures = [var.relay_network_enabled]
}
run "admission_alone_does_not_opt_into_relay_network" {
  command = plan
  variables { relay = jsondecode(file("tests/relay-binding.json")) }
  assert {
    condition     = length(aws_security_group.relay) == 0 && output.relay_network == null && length(local.endpoint_clients) == 4
    error_message = "The existing policy-only contract remains policy-only unless network preparation is explicitly selected."
  }
}
run "separate_relay_endpoint_and_layer_network" {
  command = plan
  variables {
    relay                 = jsondecode(file("tests/relay-binding.json"))
    relay_network_enabled = true
  }

  override_resource {
    override_during = plan
    target          = aws_security_group.relay[0]
    values          = { id = "sg-00000000000000006" }
  }

  assert {
    condition = (length(aws_security_group.relay) == 1 && aws_security_group.relay[0].vpc_id == var.network.vpc_id && aws_security_group.relay[0].tags.Process == "relay" &&
      aws_security_group.relay[0].name_prefix == "saveit-pilot-staging-relay-" && length(aws_security_group.relay[0].ingress) == 0 && length(aws_security_group.relay[0].egress) == 0 &&
      length(aws_vpc_security_group_ingress_rule.interface) == 19 && length(aws_vpc_security_group_egress_rule.interface) == 19 && length(aws_vpc_security_group_egress_rule.layers) == 5 &&
      alltrue([for endpoint in ["ecr-api", "ecr-dkr", "logs", "configuration"] :
        aws_vpc_security_group_ingress_rule.interface[format("%s-relay", endpoint)].security_group_id == aws_security_group.endpoint[endpoint].id &&
        aws_vpc_security_group_ingress_rule.interface[format("%s-relay", endpoint)].referenced_security_group_id == "sg-00000000000000006" &&
        aws_vpc_security_group_egress_rule.interface[format("%s-relay", endpoint)].security_group_id == "sg-00000000000000006" &&
        aws_vpc_security_group_egress_rule.interface[format("%s-relay", endpoint)].referenced_security_group_id == aws_security_group.endpoint[endpoint].id
    ]))
    error_message = "Own relay group gets exactly four endpoint pairs, never legacy/DB/NFS group reuse or inline rules."
  }
  assert {
    condition = (alltrue([for r in concat(values(aws_vpc_security_group_ingress_rule.interface), values(aws_vpc_security_group_egress_rule.interface)) :
      r.ip_protocol == "tcp" && r.from_port == 443 && r.to_port == 443 && r.cidr_ipv4 == null && r.cidr_ipv6 == null && r.prefix_list_id == null]) &&
      aws_vpc_security_group_egress_rule.layers["relay"].security_group_id == "sg-00000000000000006" &&
      aws_vpc_security_group_egress_rule.layers["relay"].prefix_list_id == data.aws_prefix_list.s3.id &&
      aws_vpc_security_group_egress_rule.layers["relay"].ip_protocol == "tcp" && aws_vpc_security_group_egress_rule.layers["relay"].from_port == 443 && aws_vpc_security_group_egress_rule.layers["relay"].to_port == 443 &&
      aws_vpc_security_group_egress_rule.layers["relay"].cidr_ipv4 == null && aws_vpc_security_group_egress_rule.layers["relay"].cidr_ipv6 == null &&
    sha256(aws_vpc_endpoint.layers.policy) == "0ce6c8d4624f3bb29f44262a9596e3acfd0c618b8758cbe637535b96de34c7aa")
    error_message = "Keep only referenced endpoint443 and regional layer prefix443; no public/default/CIDR/plaintext paths or S3 policy widening."
  }
  assert {
    condition = (jsonencode(output.relay_network) == jsonencode({ vpc_id = var.network.vpc_id, application_subnet_ids = var.network.application_subnet_ids, security_group_id = "sg-00000000000000006" }) &&
      jsonencode(output.runtime_network.client_security_groups) == jsonencode(merge(var.network.client_security_groups, { web = "sg-00000000000000005" })) &&
      alltrue([for pair in [{ actual = local.ecr_policy, expected = "ecr" }, { actual = local.logs_policy, expected = "logs" }, { actual = local.secrets_policy, expected = "configuration" }] :
    jsonencode(jsondecode(pair.actual)) == jsonencode(jsondecode(file("tests/relay-policies.json"))[pair.expected])]))
    error_message = "Return the separate group contract while retaining all49 reviewed policy statements and the legacy four-group output."
  }
}
