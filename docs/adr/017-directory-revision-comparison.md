# ADR 017: audited location/contact revision comparison

Accepted for the local core, 2026-10-30. Extends ADRs 014–016 without storage, grant, worker or dependency changes.

## Authority and historical sources

`GET /organizations/{org}/{locations|contacts}/{id}/compare` inherits the existing authenticated tenant transaction. Every request rechecks active membership and current organization Viewer/Editor grant, selects only the scoped current record's version, and reads at most two immutable snapshots of that same resource/record/organization under forced RLS. The server chooses the resource table; a location ID cannot select a contact snapshot. Archived current records remain readable. Missing historical versions, including pre-backfill gaps, are unavailable; current data never fills historical gaps.

The comparison projection deliberately excludes restore reasons. Summary context contains historical version/name/lifecycle/actor/time and numeric restored-from provenance. Every successful page commits `Location.Compared` or `Contact.Compared` with numeric source/target versions before result serialization. It writes no business row, snapshot, outbox event, search receipt or projection. Source/current/audit failures withhold all fields, counts and continuation; current content and reason column access are not required. Existing subsequent-request revocation semantics apply; this adds no in-flight revocation barrier.

## Exact bounded comparison

Locations compare name, address/details, notes and lifecycle in that order. Contacts compare name, role/details, email, phone, notes and lifecycle. Empty strings remain explicit values; lifecycle remains a JSON boolean. Strings compare exactly/ordinally, including whitespace, CRLF/LF and Unicode. Changed strings that become equal after normalizing CRLF/CR to LF get `lineEndingsOnly=true` for explanation; stored strings remain unchanged. No line-diff algorithm, email normalization, HTML parsing or merge is performed.

The maximum is four location or six contact rows. `changesOnly=true` filters the deterministic bounded set before four-row offset pages. Positive existing from/to versions may be equal or reversed. Offset defaults to zero and is valid for an empty result; negative or positive out-of-set offsets fail with 400. Missing versions are 404. Every new page independently repeats current authority/source checks and read audit. Recomputed work is bounded by existing field limits (notes 4,000), not a measured performance/SLO guarantee.

## Browser and restore separation

Directory history opens an explicit from/to form, show-unchanged control, historical context, literal side-by-side fields, line-ending notices and bounded navigation. Empty text and Active/Archived states are named in text. Mobile tables use the existing scrollable keyboard-focusable region and wrapping. Inputs/filter changes and every new/failed request clear previous comparison fields/counts/cursors; retry returns to the first page of the selected pair. Read denial clears the parent directory. Owned requests cancel on unmount and release only their own global busy state; close returns focus to the comparison opener. Directory navigation and other workspace actions remain locked during a comparison request.

Comparison's observed current version is informational and never changes the parent restore expected version, source selection or reviewed snapshot. Exact historical review remains a separate audited read. Entering review, restoring/reloading, parent history failure or changing directory context closes comparison. Current Editor/CSRF/reason/expected-version checks apply independently at restoration; a later edit still causes 409. Restoration appends exact history atomically as before.

## Evidence and remaining scope

PostgreSQL tests cover exact fields/empty values/lifecycle/line endings, reverse/same/restored equality, contact paging/filter bounds, current tenant/organization/resource/Viewer/membership/grant denial, missing history, real source/current/audit failures, reason-free/version-only column projections and stale restore conflict. Browser tests cover real review/restore/equality, retry/failure clearing, paging/filter/reset/global busy, Viewer/mobile/literal personal fields, denial clearing and informational-version isolation.

Local tests and separate agent reviews do not establish independent production security, full accessibility, measured load or vendor parity. Directory merges, richer schemas/bulk operations, organization history/lifecycle, deletion/retention recovery, backups, live Okta/cloud and the full enterprise roadmap remain unfinished.
