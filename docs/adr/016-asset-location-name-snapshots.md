# ADR 016: historical location-name snapshots for assets

Accepted for the local core, 2026-10-30. Extends ADRs 009, 013–015.

## Stored meaning and migration

`AssetRevisions.LocationName` is nullable plain text, bounded to 160 characters. It is a snapshot label, not an identifier or authority. The existing tenant/organization-qualified location FK remains authoritative for placement. A CHECK rejects non-null names without location IDs and empty/whitespace-only pins. Existing SELECT/INSERT-only history privileges, forced RLS and runtime role guards remain in place; no tenant table or worker grant is added.

The additive migration leaves every existing pin NULL. Current directory names and directory revisions cannot reconstruct the exact name observed by an older asset change, so no backfill is attempted. `LocationId=null` means unassigned; an ID with `LocationName=null` means historical name unavailable. Neither is encoded as a sentinel string. A real name can literally equal the UI's unavailable label without changing its stored meaning.

## Capture and exact recovery

Ordinary asset creation, metadata edits and custom-field/schema edits share one async snapshot helper. Placed assets read only one name through explicit tenant/organization/location predicates and EF tenant filters after the current live role check. Retained archived placements are included. Missing/hidden source or SELECT failure aborts the write; it does not create an unknown pin. Names reflect the scoped lookup statement under the existing READ COMMITTED transaction, not serialized ordering with a simultaneous directory rename. The captured label, asset version, history, audit, outbox and pending search receipt commit atomically.

Restoration is a distinct branch: it copies the source pin exactly, including a legacy NULL, while preserving current Editor/CSRF/expected-version/reason and active scoped placement checks. It does not look up a replacement name or rename/restore the location itself. The new asset revision can therefore show the restored historical label while current placement shows today's directory name. A subsequent ordinary edit captures the then-observed current name. Previous snapshots remain unchanged; actor/time and restore provenance describe the new action.

## Audited reads and comparison

Audited asset history adds nullable `locationName` without joining the current directory. Comparison reads the immutable pin only and adds a fifth metadata row, `locationName`, labelled "Snapshot location name". The location UUID remains a separate row so two equal names cannot hide a placement change. At most five metadata plus 40 union custom-field rows produce 45 rows, filtered before the existing 20-row pages. Every page retains current membership/organization checks and durable numeric-only audit before serialization. Directory SELECT outages do not prevent historical reads; historical source/audit failures withhold results.

Comparison sides add explicit `unavailable`. A location-name side is present when assigned, unavailable only when assigned with a NULL pin, and otherwise carries the literal saved name. Name/presence/availability differences count as changes. Two unknown names have matching recorded availability but do not establish that the original names were equal; the browser states this limitation. No restore reasons or historical names enter audit/outbox/search. The worker still cannot read any asset-history column and no new event or indexing behavior is introduced.

## Browser and evidence limits

History, restore confirmation and comparison render literal labels with explicit unavailable/unassigned wording and bounded wrapping. Current placement remains a separately authorized directory read. Failed history refresh clears prior snapshots, comparison and continuation state. Existing global busy/cancellation and separate restore authority remain.

Real PostgreSQL tests cover rename/archive/field edits, exact known/unknown restoration, predecessor-schema migration, bounds/FKs/RLS/append-only/worker column denial, current grant checks, source/audit failures, four atomic rollback boundaries, pagination and comparison availability. Browser tests exercise real renamed-label restore and mobile/axe rendering; legacy UI response injection is an explicit fixture, with actual legacy migration/restore separately covered in PostgreSQL. These checks and agent reviews do not establish production security, measured performance, complete accessibility or vendor parity. Directory comparison/merges, deletion recovery and backups remain separate unfinished work.
