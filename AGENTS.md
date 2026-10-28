# SaveIT repository guidance

Read `docs/project-brief.md`, `docs/architecture.md`, `docs/progress.md` and relevant ADRs before architectural changes or completion claims. Preserve all enterprise scope in `docs/backlog.md` and `docs/feature-parity.md`; unimplemented capabilities stay visibly unimplemented. Use original SaveIT branding and synthetic data.

Never derive tenant authority from request headers/payloads alone. Preserve live membership/organization checks, transaction-local PostgreSQL context, forced RLS, tenant/organization FKs and the non-owner runtime-role guard. Do not replace real PostgreSQL integration tests with an in-memory provider. Business writes, revisions where applicable, audit and outbox must commit atomically. Development identity stays opt-in and impossible in Production.

Run appropriate .NET tests, `npm run typecheck`, `npm run build`, `npm run check:ignore` and relevant Playwright tests. Regenerate OpenAPI/TypeScript when contracts change. Keep lockfiles, migrations, `.terraform.lock.hcl` when providers exist and sanitized environment examples tracked. Never commit real environment files, state/plans, secrets, test evidence or local DB files.

Workers and Terraform are scaffolds. Do not imply pending event delivery, connectors, enterprise identity, vault/AI, cloud deployment, measured SLOs, compliance or verified vendor parity works until implemented and evidenced. Record limitations and progress accurately.
