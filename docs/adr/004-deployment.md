# ADR 004: local-first modular core, regional AWS cells later

Accepted direction; cloud configuration pending, 2026-10-28.

Start with a modular ASP.NET core and a separately runnable worker scaffold, Next.js UI, PostgreSQL, versioned REST/OpenAPI, and Terraform structure. Run the development DB with pinned `embedded-postgres` binaries, scram authentication, loopback bind, synthetic credentials and persistent ignored `.local/postgres`. It is a real PostgreSQL process. CI may use a PostgreSQL service for API tests; browser tests run the complete local path.

AWS is the default roadmap: ECS/Fargate, private managed PostgreSQL/Redis/OpenSearch, S3/KMS, least-privilege workload identities, regional tenant placement and dedicated enterprise options. No cloud resources, purchases, paid identity/AI providers or public application deployment occur in this slice. The Terraform scaffold provisions nothing. Worker host delivery logic is explicitly absent.

Regional residency, retention, budget, self-hosting/licensing, regulated-data scope and support obligations remain unresolved. Avoid active-active global writes initially. Cloud release requires reviewed expand/migrate/contract migrations, synthetic staging, private network/egress controls, telemetry redaction, patched runtimes, backup/restore and key-recovery exercises, independent security reviews, and measured SLO/RPO/RTO evidence.
