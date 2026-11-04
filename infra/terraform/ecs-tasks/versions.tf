terraform {
  required_version = ">= 1.13.5, < 2.0.0"
  required_providers {
    aws = { source = "hashicorp/aws", version = "6.71.0" }
  }
}
provider "aws" {
  region              = var.region
  allowed_account_ids = [var.account_id]
  default_tags {
    tags = { Project = "SaveIT", Cell = var.cell, Environment = var.environment, ManagedBy = "Terraform" }
  }
}
