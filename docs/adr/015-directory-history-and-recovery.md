# ADR 015: scoped location/contact history and recovery

Accepted for the local core, 2026-10-29. Extends ADR 009; preserves document/asset recovery, typed schemas, comparison, relationships and search authority.

## Immutable snapshots and migration

`LocationRevisions` and `ContactRevisions` retain the exact bounded plain-text fields and archived state for each successful create/edit/archive/reactivate/restore. Their `(TenantId, OrganizationId, RecordId, Version)` primary keys, matching current-record FKs and fully qualified source self-FKs prevent cross-tenant, organization, record or resource-type references. Positive versions and NULL-safe paired source/reason checks enforce provenance. The API has SELECT/INSERT only; existing current records and all old snapshots remain retained. Both tables use forced RLS and EF tenant/write guards, bringing the tenant-table total to 19.

Migration records each existing head's actual current version, fields, lifecycle and update timestamp, attributed to `migration`. It does not invent version 1 or reconstruct earlier changes. A backfilled version greater than 1 can therefore have no earlier available source. Missing historical versions return 404. The migration is tested against a real database at the preceding schema, including version-7 and version-9 heads.

## Current authority, bounded reads and atomic writes

Every history page and exact snapshot read requires the current active signed-session membership and organization Viewer/Editor grant, including for archived records. Archival is a list filter, not an access grant. The existing transaction-local PostgreSQL context, tenant filters and forced RLS apply. History selects metadata only (version/name/lifecycle/actor/time/numeric source), orders newest first and fetches at most 51 rows to return 50 with a version cursor. Notes, email, phone, details and reasons require a separate exact snapshot read. Positive cursor/version validation and scoped predicates precede projection. Read audit commits before serialization; source or audit failure yields no historical fields or continuation.

Editor restoration also requires CSRF, the expected current version, an existing earlier source, version capacity and a reason of 3–500 trimmed characters. It keeps the identity/tenant/organization fixed and copies the exact source fields **and archived state** into a newly numbered snapshot, with the new actor/time and numeric source/reason provenance. Prior snapshots remain unchanged. An archived restoration may disappear from the active list; history remains inspectable. Asset identities, placement FKs, documents and relationships are not rewritten. Restoring an active location can make it available for future placement; restoring archive state does not remove existing placements.

The current record update, appended revision, numeric-only successful audit, metadata-only outbox event and pending search receipt commit together. History/reason content never enters audit/outbox. Expected-version updates reject restore/restore and restore/edit races with one winner. Injected history/audit/outbox/queue failures must roll back every related write. These guarantees cover application transactions, not synchronous in-flight revocation or an immutable operational archive.

## Worker and browser boundaries

Worker startup covers ownership/reachable privileges on the two new tables, rejects core writes including column grants and rejects directory current/history reads. Direct/PUBLIC/NOINHERIT grant regressions preserve least privilege. The exact schema-v1 `Location.Restored` and `Contact.Restored` events are acknowledged without indexing directory fields; unsupported types/schemas still dead-letter. Receipts remain separate from global event delivery. There is no new connector, notification or external side effect.

The browser offers explicit history paging and audited snapshot review with literal text, lifecycle labels and native reasoned confirmation. Viewer history has no restoration controls. A restore conflict retains the reason; even a failed current reload preserves it and prevents submission. Successful reload rechecks both current and historical sources and requires renewed review/confirmation. Historical read/page failure clears snapshots, rows and cursors; access denial clears the directory view. Requests cancel on unmount, independent list reads keep their own controllers, and directory/recovery controls honor global workspace busy state. Confirmation names the archived-state consequences and retains normal Escape/focus behavior.

## Remaining scope

Historical location-name pins for asset snapshots, directory revision comparison/merges, richer person/address schemas, bulk operations, deletion/retention recovery, backup restoration and enterprise readiness remain unimplemented. This adds no dependency or external account. Local PostgreSQL/browser/review evidence does not establish independent penetration, complete accessibility, measured performance or production operational assurance.
