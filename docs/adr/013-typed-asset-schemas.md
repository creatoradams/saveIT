# ADR 013: pinned, versioned asset templates

Accepted for the local core, 2026-10-29. Enterprise scope and unimplemented capabilities remain in the backlog and parity inventory.

## Decision

An organization owns an `AssetType` head and append-only `AssetTypeRevision` definitions. Each publication, including archival/reactivation, increments the optimistic version and appends a snapshot in the same transaction as audit/outbox and the search receipt. Existing assets are never rewritten by publication. The API role has SELECT/INSERT plus lifecycle/name/version column UPDATE on heads; revisions have SELECT/INSERT only. Neither table permits application deletion or tenant/organization reassignment. Both have forced RLS and qualified organization FKs.

Assets and asset snapshots store a nullable paired type ID/version plus `FieldsJson`. A composite tenant/organization/type/version FK pins the exact immutable definition. Untyped assets require an empty object. Existing metadata edits preserve the pin/values; explicit field edits require full validated values and the current asset version. Restoring an asset appends a new exact snapshot, including its historical pin/values and source/reason provenance. It never applies current defaults or upgrades schemas. Restore reasons and field/schema content remain outside audit/outbox metadata.

Archived templates remain readable and valid for an existing identical pin, including field edits and restoration retaining that pin. Creating/reassigning/upgrading an asset, or restoring a different historical pin, requires an active template. Reactivate before that historical rebinding. This is a request-time lifecycle check, not a synchronous in-flight revocation barrier.

## Format v1

Definitions contain 1–20 fields. Keys match `[a-z][a-z0-9_]{0,31}` and are unique. Each field has a plain label (80 characters), exact lowercase type and boolean `required`; only `options` and `defaultValue` are optional properties. Supported types: text (500 characters), number (±1,000,000,000 with at most three fractional decimals), boolean, valid ISO calendar date (`0001-01-01` through `9999-12-31`), and choice (1–20 unique nonblank strings, 80 characters each). Text excludes controls except CR/LF/tab. Required text must be nonblank. Optional values are omitted; explicit null/nested values and unknown keys fail. Boolean false is a valid supplied value. A null/absent default means no default; other defaults must satisfy their field type. There are no user regexes, references, nesting, computed values or executable content.

Both raw API JSON and canonical JSONB are limited to 16 KiB. API numbers additionally have bounded lexical length/exponents to reject normalization/decimal-underflow errors. API JSON is checked for duplicate properties and malformed Unicode before PostgreSQL normalization. UTF-8 is required by startup guards and explicitly provisioned for local/test databases. The existing synthetic Windows database was transferred with exact table snapshot verification and retained as a local backup; this is not an enterprise backup/restore drill.

The authoritative functions `valid_asset_*_v1` are immutable, fixed-path, SECURITY INVOKER functions. Definition CHECKs and invoker asset/history triggers enforce the same type/value rules on direct SQL writes. SQL NULL never satisfies a validator/CHECK. Functions expose no table authority and PUBLIC execution is revoked. A future format must use new functions and an explicit format/migration design; never reinterpret existing v1 definitions by replacing its validators.

See PostgreSQL's primary documentation on [JSON/JSONB semantics](https://www.postgresql.org/docs/18/datatype-json.html), [JSON functions](https://www.postgresql.org/docs/18/functions-json.html) and [trigger functions](https://www.postgresql.org/docs/18/plpgsql-trigger.html). The application validates original JSON because JSONB normalization can discard representation information.

## Reads, browser and search

Lists expose metadata/reference IDs only. Audited exact-definition and asset-field endpoints return confidential schema/default/value content after live membership/grant checks. Head and definition-history pages are bounded to 50; history lists contain metadata, and exact definitions require another audited request. Asset history includes bounded values for restoration. Audits must commit before content is serialized.

The browser supplies native typed controls, reviewable defaults only for new creation, explicit schema-change review retaining matching keys, validation/conflict/reload states and literal text rendering. It does not infer authorization. Removing unmatched keys requires explicit review; new required values must be supplied. UI assignment uses the selected template's latest published version. Historical explicit assignment and clearing a binding are API capabilities; no bulk migration UI exists.

Custom values/defaults/definitions are excluded from search, list/relationship responses and events. Worker SELECT on assets is restricted to view-required core columns. The worker has no type-table or asset-history reads; startup rejects direct, PUBLIC or reachable-role content grants, as well as existing core-write/administration authority checks. Schema-v1 type events are explicitly acknowledged as metadata-only; unsupported events/schemas still dead-letter. Relationship creation projects only endpoint metadata.

Kestrel caps every request body at 1 MiB. The Next proxy returns 413 for oversized declared Content-Length before forwarding; streaming bodies are independently bounded by Kestrel. A proxy transport reset may surface as a generic failure for a streaming oversized body. Per-tenant quotas, edge limits and production proxy configuration remain release work.

## Limits

No schema migration jobs, bulk previews, nested schemas, secrets/vault, configuration discovery, field-based search/facets, cross-organization types, template deletion or vendor parity is implemented. Bounded local functionality and automated reviews do not establish production load/security readiness.
