# SaveIT API v1

Source of truth: [OpenAPI](../contracts/openapi.json); generated TypeScript: `apps/web/lib/api.generated.ts`. Prefix `/api/v1`; browser traffic uses the same-origin Next proxy. No tenant header grants authority.

| Method and path | Required authority | Result |
| --- | --- | --- |
| GET /csrf | Anonymous or signed-in | Principal-bound antiforgery token and cookie |
| GET /dev/profiles | Explicit development identity opt-in | Synthetic profile metadata |
| POST /dev/login | Same opt-in + CSRF | Valid persisted membership → HttpOnly cookie |
| POST /logout | Signed-in + CSRF | Clear cookie |
| GET /session | Active membership | Actor and tenant metadata |
| GET /organizations | Active membership | Currently granted organizations and roles; max 100 |
| GET /organizations/{org}/assets?after={uuid} | Viewer/Editor | Scoped asset page |
| POST /organizations/{org}/assets | Editor + CSRF | Persist asset, audit/outbox; 201 |
| GET /organizations/{org}/assets/{asset} | Viewer/Editor | Scoped asset metadata |
| GET /organizations/{org}/assets/{asset}/relationships?after={uuid} | Viewer/Editor | Active incoming/outgoing links with current scoped endpoint labels; audited page, max 50 |
| POST /organizations/{org}/assets/{asset}/relationships | Editor + CSRF | Create directed link to a different asset in this organization, audit/outbox; 201 or duplicate 409 |
| POST /organizations/{org}/assets/{asset}/relationships/{relationship}/remove | Editor + CSRF | Expected-version soft removal, audit/outbox; 204 or conflict 409 |
| GET /organizations/{org}/assets/{asset}/documents?after={uuid} | Viewer/Editor | Linked document metadata; empty content field |
| POST /organizations/{org}/documents | Editor + CSRF | Validate scoped asset, initial revision/audit/outbox; 201 |
| GET /organizations/{org}/documents/{doc} | Viewer/Editor | Body with durable read audit |
| PUT /organizations/{org}/documents/{doc} | Editor + CSRF | Expected-version revision/audit/outbox; 409 on conflict |
| GET /organizations/{org}/documents/{doc}/revisions?before={integer} | Viewer/Editor | Latest-first body snapshots; audited read |
| GET /organizations/{org}/documents/{doc}/compare?fromVersion={int}&toVersion={int}&offset={int} | Viewer/Editor | Audited plain-text comparison; at most 200 rows |
| POST /organizations/{org}/documents/{doc}/restore | Editor + CSRF | Earlier snapshot as a new revision, audit/outbox; 409 on conflict |
| GET /organizations/{org}/audit?before={long} | Editor | Latest-first organization audit |
| GET /search?q={text}&organizationId={uuid}&type={asset or document}&after={cursor} | Active membership, per-result current Viewer/Editor grants | Ranked, audited asset/document previews; all optional filters except q |

GET `/health` is process liveness, not dependency readiness. `/openapi/v1.json` is available in the local Development host; production startup is currently disabled.

Mutations send JSON and `X-CSRF-TOKEN` from GET `/csrf`. Obtain a new token after identity changes. Cookies are HttpOnly; no browser localStorage tokens. API responses use `Cache-Control: no-store`. Validation/conflict/internal errors use Problem Details; bare 401/403/404 may have no body. Internal failures include correlation IDs, not SQL or documentation.

Create asset: `{ "name": "Edge firewall", "kind": "Network" }`. Create document: `{ "assetId": "<scoped UUID>", "title": "Failover", "content": "Synthetic plain text" }`. Revise: `{ "title": "Failover", "content": "Revised text", "expectedVersion": 1 }`.

Names/titles: max 160 characters; type: 80; content: 50,000; whitespace-only required values are invalid. Server generates stable UUIDs. Pages are `{ items, nextCursor }` with at most 50 entries. Assets/documents sort UUID ascending; revisions/audit sort numeric cursor descending. Pass returned cursor unchanged via the route's `after`/`before` parameter. No cross-scope total counts are exposed.

Create relationship: `{ "targetAssetId": "<scoped asset UUID>", "kind": "DependsOn" }`. Kinds are exactly `DependsOn` and `Contains`; source is the route's asset. Self/empty targets and unsupported kinds return 400; missing/cross-organization/cross-tenant endpoints return 404. Response includes opaque link ID, organization, current `source`/`target` AssetDto metadata, kind, version 1 and creation actor/time. The inverse labels are Required by / Part of. List includes both directions, excludes removed rows, sorts link UUID ascending and returns at most 50 plus nullable `nextCursor`. Every page rechecks current access and joins both scoped assets before labels or cursors; failed read audit returns no metadata. Malformed query/JSON binding returns 400.

Remove relationship: `{ "expectedVersion": 1 }`. The route asset must be one of the endpoints. Current Editor authority and CSRF are required again; stale/already removed links return 409 without a successful event. Success records version 2/removal actor/time, preserving the row and both assets/documents. The active unique index permits recreating a removed link with a new ID. Endpoint/type/creation columns cannot be updated by the app role and physical delete is denied. Read audit action is `Asset.RelationshipsRead`; change actions are `AssetRelationship.Created`/`AssetRelationship.Removed`, with opaque target IDs and no endpoint labels. Search-v1 acknowledges known schema-v1 link events without projection changes. Cross-organization links, graph traversal, cycle enforcement, relation edits/undelete and relationship-derived permissions remain absent; see ADR 008.

Compare requires two positive existing version numbers of the same scoped document. Response includes revision summaries, observed `currentVersion`, title/content/line-ending flags, added/removed counts, `simplified`, `totalRows`, `offset`, at most 200 `lines` and nullable `nextOffset`. Each line has kind (`unchanged`, `removed`, `added`), nullable 1-based old/new numbers and literal text. Pass `nextOffset` with the same version pair; default offset is 0. Negative/out-of-range offsets return 400; missing revisions return 404. Line endings are normalized for display only. Large changes use bounded replacement blocks and may not be a minimal diff. All comparison pages require successful historical-read audit before response serialization.

Restore: `{ "sourceVersion": 1, "expectedVersion": 2, "reason": "Reinstate approved procedure" }`. Source must precede the current revision; current/future source is 400, missing source is 404, stale expected version is 409. Reasons are required, trimmed, 3–500 characters (raw maximum 500); control characters other than CR/LF/tab are rejected. Success returns the current `DocumentDto` with version 3 and the exact title/content from revision 1. Existing history is immutable. The new revision's `restoredFromVersion`/`restoreReason` are returned by the audited history endpoint; ordinary revisions have null provenance. `Document.Compared`/`Document.Restored` audits expose numeric `sourceVersion`/`targetVersion`, never text or reasons. A restore appends revision/audit/outbox atomically and emits a search invalidation; stale projections stay excluded until indexing catches up. See ADR 007 for bounds and concurrency semantics.

Search requires 2–120 characters, at most 12 complete-word terms and no control characters; query terms are ANDed. It returns `{ items, nextCursor }` with at most **25** results per page, ranked by per-record match and stable type/UUID tie-breakers. Cursor is bound to the query, principal, organization and record-type filters; pass it unchanged and restart pagination when changing filters. An omitted organization searches all currently authorized organizations. Inaccessible explicit scope returns 404. All titles, organization labels and 240-character plain-text previews are rehydrated from current core rows after exact projection/source/version and live membership/grant checks. No total count/facets/autocomplete or file/vault search exists. New writes are eventually indexed; stale revisions are excluded in the meantime.

Search writes read-audit events before serializing results/cursor; audit failure returns 500 without previews. A missing/unavailable projection/source returns 503 without fallback or cached results. Invalid query/filter/cursor returns 400. Search terms and snippets are not recorded in audit/outbox/log messages. Configure any future reverse-proxy access logging to redact query strings.

404 hides guessed/inaccessible organizations and mismatched targets; a forbidden action inside a readable organization returns 403; inactive membership returns 401. A 409 does not overwrite the document or create a successful event. Body/history reads fail if their audit insert fails. Denied/authentication/metadata-read auditing remains incomplete. No delete, export, share, vault or permission-administration endpoint exists.
