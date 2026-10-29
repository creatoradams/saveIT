# ADR 008: scoped directed asset relationships

Accepted for the fourth local slice, 2026-10-28. Relationships describe inventory; they do not grant access or execute dependency workflows.

## Scope and model

Implement two directed kinds: `DependsOn` (inverse label: Required by) and `Contains` (inverse: Part of). Both endpoints must be different assets in the same managed organization and tenant. Cross-organization/account links, custom relationship types, topology traversal, cycle enforcement and relationship-based authorization remain planned. Cycles are allowed in this descriptive model; it is not a dependency scheduler or strict containment tree.

`AssetRelationship` owns an opaque UUID, tenant/organization, source/target asset UUIDs, kind, version, creation actor/time and nullable removal actor/time. Composite foreign keys qualify **both** endpoints by tenant and organization. Forced RLS denies application reads/writes without transaction-local tenant context. The application role can SELECT/INSERT and update only version/removal columns; it cannot rewrite endpoint/type/creation metadata or DELETE rows. The search role has no relationship-table access. Positive lifecycle/type/self-link checks and a unique active source/target/kind index apply in PostgreSQL as well as API validation.

Viewers can list incoming/outgoing active links. Editors can create or remove links after live membership/grant checks. Every page joins both current asset rows before labels/cursor are returned; list reads require durable audit. Authorization is by current organization role, not an edge. Unknown/inaccessible scopes/endpoints return 404; Viewer mutation is 403. ADR 002's existing in-flight revocation limitation still applies.

## Mutation and concurrency

Creation emits `AssetRelationship.Created` audit/outbox metadata in the same transaction as the link. A racing active duplicate yields one success and one 409 via the unique index; opposite directions/different kinds are distinct. Removal requires `expectedVersion`, changes version 1 to 2 and records removal actor/time. The optimistic update predicate makes simultaneous removals yield one success and one 409. The old row and both assets/documents remain. Recreating a removed link creates a different UUID, preserving the prior lifecycle; no undelete/update endpoint is exposed.

Every successful removal atomically writes `AssetRelationship.Removed` audit/outbox metadata. Audit/outbox targets are opaque relationship IDs, without endpoint names or document content. Reads target the selected asset with `Asset.RelationshipsRead` audit and no outbox. Failed audit/outbox persistence rolls back the entire mutation; failed list audit returns no link metadata. Retained rows are not an immutable operational archive or a complete asset-history product.

Search-v1 explicitly acknowledges schema-v1 Created/Removed relationship events without projection changes: edge metadata is not searchable text. Its transactional receipt denotes this consumer's no-op handling only. Unknown types or unsupported schemas still dead-letter, and global DeliveredAt stays untouched. Worker startup checks column-level as well as table-level core write grants. This does not implement generic external event delivery or relationship search.

## Bounds and user flow

Active links use UUID keyset pages of at most 50, including both directions. Cursors are opaque UUID positions; authority/scope is rechecked on every page. Candidate selection uses the existing 50-row scoped asset endpoint with explicit load-more controls, omitting self. No cross-scope counts, names or suggestions are returned. Large-account quotas, indexing/load measurements, graph traversal and a virtualized picker remain future work.

The workspace supports typed link creation, incoming/outgoing labels, current authorized linked-asset navigation, read-only controls, loading/empty/error/retry states and duplicate/removal conflict recovery. A native confirmation explains that removing a link preserves both assets/documents and audit. Organization/asset switches cancel requests and reset component state. Plain React text protects labels containing HTML. Browser navigation rechecks current asset access even when the target was outside the currently loaded inventory page.

Real PostgreSQL tests cover persistence/direction, Viewer/tenant/org denial, both endpoint FKs/RLS, creation/removal races, audit/outbox rollback, current-cookie revocation, 50-row paging/read-audit failure, malformed requests/CSRF, worker no-op/replay/schema handling and forbidden column grants. Browser checks cover actual persistence, navigation, organization changes, read-only access, duplicate/removal conflicts, outage/retry presentation, escaped labels, native modal keyboard behavior, mobile layout and axe checks. These are local acceptance evidence, not independent security/WCAG assurance or enterprise parity.
