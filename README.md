# SaveIT

An IT documentation and operations platform. This repository implements a persistent **local foundation slice** and preserves the full enterprise scope in [the backlog](docs/backlog.md). It is not enterprise-ready or verified IT Glue parity.

Working flow: choose a synthetic development identity → select an authorized organization → create/list assets and link their dependencies → create a linked plain-text document → revise and compare it → restore an earlier revision with an audited reason → find it with permission-aware search. PostgreSQL retains data across restarts.

## Run locally

Install .NET SDK **10.0.401** (or a later 10.0.4xx patch allowed by `global.json`), **Node 24.x**, npm and Git. Use current servicing patches before release. No Docker, cloud account, API key or paid service is required. Initial dependency restore needs network access. From the repository root:

```sh
npm ci
dotnet restore SaveIT.slnx --locked-mode
npm run dev
```

Open **http://127.0.0.1:3000**. The script starts real PostgreSQL 18 on loopback port 55432, applies EF migrations and synthetic seeds through a separate owner connection, rebuilds local search projections, starts ASP.NET on 5080, Next.js on 3000, and the restricted search worker. Ctrl+C stops child services. First startup initializes ignored `.local/postgres`; later starts retain it. Do not run duplicate instances against the same ports/database directory.

| Synthetic profile | Tenant | Access |
| --- | --- | --- |
| Alice Chen | Northstar IT | Editor: Acme Manufacturing and Harbor Clinic |
| Sam Rivera | Northstar IT | Viewer: Acme Manufacturing only; no audit administration |
| Bob Morgan | Southbank IT | Editor: a separate Acme Manufacturing |

Both tenants deliberately contain a Core switch. The script explicitly opts into development identity; default configuration disables it. Enabling it outside Development throws. Production startup refuses to run until a real enterprise identity adapter is configured/reviewed. Use synthetic data only.

Optional shell variables: `SAVEIT_DB_PORT`, `SAVEIT_API_PORT`, `SAVEIT_WEB_PORT`. `.env.example` documents them; the launcher does not automatically read dotenv files. The API uses `ConnectionStrings__App` for a restricted runtime role; only the local migration process receives the separate owner connection. Never track real connection secrets.

The pinned embedded PostgreSQL wrapper is development tooling, not a production database deployment. Windows x64 is locally verified. Linux requires a regular user (not root) and PostgreSQL runtime libraries such as libicu; GitHub CI targets Ubuntu 24.04.

## Verify

With `npm run dev` running in another terminal:

```sh
dotnet test SaveIT.slnx --no-restore
npm run check:ignore
npm run typecheck
npm run build
npx playwright install chromium
npm run test:e2e
```

API tests create/drop an isolated randomly named test database, apply real migrations, and exercise the actual non-owner NOBYPASSRLS application role. They do not reset development data. Override `SAVEIT_TEST_ADMIN` only with an owner connection to an isolated synthetic test cluster. `npm run db` starts the database alone. On Windows, stop the API before rebuilding changed backend files if assembly file locking prevents compilation.

Playwright starts the complete stack automatically if the local web service is absent. It writes synthetic test records to the development database; use a separate checkout/ports for isolated browser data. Traces/reports/screenshots are ignored. Checks cover persistence, organization switching, roles, conflicts, keyboard navigation, responsive layout and axe accessibility; they do not establish complete WCAG compliance.

```sh
npm audit --audit-level=high
dotnet list SaveIT.slnx package --vulnerable --include-transitive --format json > .local/nuget-audit.json
node scripts/check-nuget-audit.mjs .local/nuget-audit.json
```

CI builds/tests both layers, checks contracts/ignore policy, audits dependencies and runs browser tests. See [actual progress and limitations](docs/progress.md).

## Contracts and migrations

With the local API running, export the API schema and generate TypeScript:

```sh
node scripts/contracts.mjs
npm run contracts
```

Commit `contracts/openapi.json` and `apps/web/lib/api.generated.ts`. The frontend imports generated response types; domain/policy logic remains in C#. See [API conventions](docs/api.md). OpenAPI is available at http://127.0.0.1:5080/openapi/v1.json.

```sh
dotnet tool restore
dotnet ef migrations add MeaningfulName --project services/api
```

Review migrations, tenant constraints and RLS before applying. Local `--migrate` requires explicit development identity and a separate owner connection. Production migrations must use reviewed expand/migrate/contract procedures and isolated restore evidence.

## Repository and remaining scope

`apps/web` contains Next.js/TypeScript; `services/api` owns EF migrations, core records, policy, audit/outbox; `services/search` implements derived full-text projections/querying; `services/workers` runs the actual search consumer. `infra/terraform` remains a non-provisioning scaffold. `contracts`, `tests` and `docs` hold shared schemas, PostgreSQL/Playwright checks, original brief, architecture, threats, eight ADRs, parity inventory, roadmap and progress.

Tenant authority comes from a protected session and live membership. Server policies, tenant-qualified relationships and forced PostgreSQL RLS apply from the first slice. Successful writes and audit/outbox rows commit atomically; revisions are append-only to the application role. Search consumes immutable outbox invalidations using separate transactional completion receipts, bounded retries, dead letters and replay. Generic external event delivery is not implemented.

Search supports complete-word matching in asset names/types and document titles/content, organization/type filters, ranked cursor pages and plain-text previews. PostgreSQL full-text projections are rebuildable; OpenSearch remains planned. Every result is checked against current membership/grants and exact current source version/content before previews or pagination are returned. A stale revision is excluded until indexing catches up. No files, vault values, autocomplete, facets or total count are indexed/exposed. Search fails closed on projection/source/audit failure.

Open a document and choose **View revisions**, then **Compare revisions** or select a historical revision's comparison button. Viewers can inspect the line-numbered, paginated plain-text comparison. Editors can restore an earlier revision after reviewing the confirmation and entering a reason. Restoring revision 1 when the current version is 2 creates revision 3, preserving revisions 1 and 2. A concurrent edit stops the restore and offers reload/review. Source revision and reason appear in audited history; audit events contain numeric version identifiers without content/reasons. Search catches up through the real worker. Large comparisons show bounded replacement blocks; see [ADR 007](docs/adr/007-document-recovery.md).

Asset details include **Relationships**. Editors can link a different asset in the same organization using **Depends on** or **Contains**. Both ends show the appropriate direction and open the currently authorized linked asset. Viewers can follow links. Confirmed removal preserves both assets/documents and retains the link record/audit; duplicates and simultaneous removals return a recoverable conflict. Lists and target selection load in bounded pages. Relationships never grant permissions or enter full-text search. See [ADR 008](docs/adr/008-asset-relationships.md).

With the local database running and worker project built:

```sh
npm run search:rebuild
npm run search:once
node scripts/search-worker.mjs --replay-event <event-uuid> --once
```

These commands use a separate synthetic `saveit_search_worker` account and the two configured development tenants. Rebuild does not modify core records. The background worker polls every second; writes may take a moment to appear. See [ADR 006](docs/adr/006-search-and-delivery.md) for ordering, retry/dead-letter semantics, tenant boundaries and recovery instructions. `SearchDeliveries` tracks this consumer; global `OutboxEvents.DeliveredAt` is intentionally untouched. Production needs externally managed worker credentials, explicit tenant scheduling and operator controls.

**Next slice:** organization-scoped locations linked to assets, with current permissions, tenant-qualified relationships, version conflicts and atomic audit/outbox. Enterprise SSO/MFA/SCIM, advanced grants, organization management, richer asset schemas/relationships and asset/deletion recovery, private attachments/rich editing, OpenSearch, external event delivery, integrations/workflows, vault/OTP/rotation, portals/discovery/mobile/extensions/offline, Office editing, AI, cloud operations and independent enterprise assurance remain on the roadmap. No public application deployment is configured.
