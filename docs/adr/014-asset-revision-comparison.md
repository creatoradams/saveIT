# ADR 014: audited, bounded asset revision comparison

Accepted for the local core, 2026-10-29. This extends ADRs 007, 009 and 013 without changing storage, grants, search or restoration authority.

## Historical sources and authority

Current Viewers and Editors can compare any two existing revisions of an asset in their currently granted organization, including the same revision or reverse order. Every request/page uses the signed session, live membership/organization grant, transaction-local tenant context, tenant filters and forced RLS. At most two scoped asset snapshots and two exact pinned schema revisions are read. The current asset lookup selects only its version. Historical schema names, labels, rules/defaults come from immutable pinned revisions; current template heads cannot reinterpret them. Archived templates remain readable. Missing/inaccessible asset/revision IDs return 404. Missing pinned schema or source/database/audit failure withholds all comparison fields, counts and pagination; there is no fallback to current definitions.

Every successful page writes `Asset.Compared` with numeric source/target versions before transaction commit/result serialization. No field values, schema content, restore reasons or labels enter audit metadata. Comparison produces no outbox event and writes no business row, snapshot or projection. Restore reasons are not selected or returned. Existing subsequent-request revocation semantics apply; there is no synchronous in-flight revocation barrier.

## Comparison semantics and bounds

Four fixed metadata rows compare name, free-form asset type, location UUID and template ID/version. Location names are not historical data in the current model, so comparison never substitutes current directory names. Version numbers, actors/timestamps and restored-from versions appear as snapshot context rather than changes to business fields.

Custom fields align by ordinal key across the two historical schemas, not by array position. Each side distinguishes field-not-defined, defined-but-unset and an explicitly supplied scalar. False, zero and empty optional text are present values. No defaults are applied. Strings, including whitespace/line endings, compare exactly/ordinally; validated numbers and numeric defaults compare as decimal values regardless of JSON formatting. Label/type/required/default/ordered-choice changes are definition changes even when the stored value is unchanged. Absent and null defaults mean the same thing; absent/null non-choice options are equivalent. Choice order is meaningful; reordering fields alone creates no field-level change. Template pin changes remain a metadata change. Matching keys across different templates do not establish semantic identity; both historical definitions are shown.

The v1 schema/value limits yield at most 40 custom-field union rows plus four metadata rows. Each response returns at most 20 rows. The default `changesOnly=true` filter is applied to the deterministic complete bounded row set before offset pagination; `offset=0` is valid for an empty comparison. A positive offset outside the selected set or negative/invalid revision/offset input fails. Each new page rechecks current authority and audits independently. Work is bounded recomputation, not a large-file diff or a measured performance guarantee.

## Browser and restore separation

Asset history opens explicit revision inputs, a changes-only/all-rows filter, snapshot/schema context, per-side labels/rules/values, and bounded navigation. Values are literal React text. The mobile table scrolls within a keyboard-focusable region; unset/empty/added/removed/definition/value states are labelled in text. Input/filter changes and every failed/new request clear the previous result, counts and page controls. Abort and shared busy state protect navigation/concurrent operations. Retry starts at the first page of the selected pair.

Comparison has no restore control or authority. Restore remains the existing current-Editor, CSRF-protected, expected-version, reasoned confirmation flow. A comparison's observed current version is informational; intervening writes still produce 409. Refreshing asset details reloads history before retry. Restoration still appends an exact snapshot and atomically commits revision/audit/outbox; comparison neither changes old snapshots nor migrates fields.

## Remaining scope

Directory revision history, historical location-name pins, row-level field history/merges, rich document comparison, bulk schema migration, deletion/retention recovery and enterprise backups remain unfinished. No new dependency, migration, DB authority or external service is required. Automated PostgreSQL/browser tests and separate read-only agent reviews establish local behavior only, not independent production security, accessibility or load assurance.
