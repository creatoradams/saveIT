# SaveIT infrastructure scaffold

This folder provisions no resources and configures no remote backend/provider. `terraform fmt -check` and `terraform init -backend=false` / `terraform validate` check the scaffold when Terraform >=1.9,<2 is installed.

Before an AWS implementation, resolve [ADR 004](../../docs/adr/004-deployment.md), cloud accounts, budget, regions/residency, networking and remote-state key/access controls. Then add separate core/API, worker, private data, storage/KMS, identity, monitoring and regional-cell modules. Commit `.terraform.lock.hcl` after provider selection. State, plans and local secret tfvars are ignored; sanitized `*.tfvars.example` files remain tracked.
