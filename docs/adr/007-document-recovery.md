# ADR 007: bounded document comparison and append-only restoration

Accepted for the third local slice, 2026-10-28. This covers plain-text documents; asset rollback, deletion recovery and operational backup restoration remain unimplemented.

## Comparison and authority

Viewers and Editors can compare any two existing revisions of a document in a currently granted organization. The API checks live membership and organization scope, forced RLS and scoped document/revision predicates on every request, including comparison pages. Missing or inaccessible records return 404. Each successful comparison requires a durable `Document.Compared` audit with numeric source/target revisions before any historical text is serialized. Audit failure fails closed. Current in-flight revocation semantics remain those in ADR 002.

Comparison normalizes CRLF/CR to LF for display, reports title changes separately, and identifies line-ending-only changes. Snapshots retain their exact original text. Common prefixes/suffixes are preserved; a longest-common-subsequence diff is used only for a changed middle of at most 250,000 matrix cells. Larger replacements use faithful remove/add blocks rather than attempting a minimal diff. Inputs inherit the 50,000-character document limit. Output has at most 200 rows per request, with numeric offsets tied to the requested immutable version pair. The full bounded diff is recomputed per page; this is not a large-file streaming diff or a measured latency guarantee. General tenant quotas/rate limits remain pending.

## Restoration and provenance

Only a current Editor can restore. A CSRF-protected POST supplies `sourceVersion`, `expectedVersion` and a trimmed reason of 3–500 characters. The source must be an earlier revision of the same document; there is no import, arbitrary snapshot or cross-document restore. The current integer version is checked before changes and in EF's update predicate. Stale submissions and racing edit/restore operations return 409 without a successful restore event. Reload/review is required before retrying; an older comparison is never proof of current authority or version.

Restoration copies the exact historical title/content, increments the current version, and atomically appends a new snapshot, `Document.Restored` audit and schema-v1 outbox invalidation. Existing snapshots remain unchanged. New snapshot provenance includes `RestoredFromVersion` and `RestoreReason`; the tenant/document/version composite foreign key prevents cross-document references. A database check requires both provenance fields together and an earlier positive source revision, rejecting partial NULL values. The application role retains no UPDATE/DELETE rights on snapshots or audit.

Reasons are confidential document-history metadata: they appear only through the authorized, audited revision endpoint. Audit contains numeric source/new version identifiers, never document title/body or reason. Outbox contains only existing opaque invalidation metadata. Search recognizes restoration, rehydrates the latest source, and excludes outdated projections until indexing catches up; no historical search fallback is introduced. Restore of identical content still records the deliberate earlier-revision selection as a new revision.

## User flow and evidence

Revision history offers explicit version inputs and a plain-text, line-numbered comparison with change labels, 200-row pages and current-version checks. Editors review a native modal describing the new revision, preserved history and required reason. Cancel/Escape returns focus; submitting disables concurrent navigation/actions. A conflict keeps the entered reason visible and offers reload. Viewers see comparison without restore controls. The server remains authoritative regardless of UI state.

Real PostgreSQL tests cover exact content/history/provenance, role and cross-tenant/scope denial, grant/membership revocation after comparison, restore/restore and restore/edit races, injected audit/outbox failures, comparison audit failure, validation/CSRF, foreign keys/checks, bounded paging and search catch-up/replay. Algorithm tests reconstruct both normalized inputs, including repeated/empty lines and worst-case inputs. Chromium exercises persistence/audit, actual worker indexing, viewer access, conflict review/retry, literal HTML, pagination, keyboard focus, responsive layout and automated axe checks. These establish local behavior, not complete WCAG review, production disaster recovery or enterprise readiness.
