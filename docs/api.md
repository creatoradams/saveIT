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
| GET /organizations/{org}/assets/{asset}/documents?after={uuid} | Viewer/Editor | Linked document metadata; empty content field |
| POST /organizations/{org}/documents | Editor + CSRF | Validate scoped asset, initial revision/audit/outbox; 201 |
| GET /organizations/{org}/documents/{doc} | Viewer/Editor | Body with durable read audit |
| PUT /organizations/{org}/documents/{doc} | Editor + CSRF | Expected-version revision/audit/outbox; 409 on conflict |
| GET /organizations/{org}/documents/{doc}/revisions?before={integer} | Viewer/Editor | Latest-first body snapshots; audited read |
| GET /organizations/{org}/audit?before={long} | Editor | Latest-first organization audit |

GET `/health` is process liveness, not dependency readiness. `/openapi/v1.json` is available in the local Development host; production startup is currently disabled.

Mutations send JSON and `X-CSRF-TOKEN` from GET `/csrf`. Obtain a new token after identity changes. Cookies are HttpOnly; no browser localStorage tokens. API responses use `Cache-Control: no-store`. Validation/conflict/internal errors use Problem Details; bare 401/403/404 may have no body. Internal failures include correlation IDs, not SQL or documentation.

Create asset: `{ "name": "Edge firewall", "kind": "Network" }`. Create document: `{ "assetId": "<scoped UUID>", "title": "Failover", "content": "Synthetic plain text" }`. Revise: `{ "title": "Failover", "content": "Revised text", "expectedVersion": 1 }`.

Names/titles: max 160 characters; type: 80; content: 50,000; whitespace-only required values are invalid. Server generates stable UUIDs. Pages are `{ items, nextCursor }` with at most 50 entries. Assets/documents sort UUID ascending; revisions/audit sort numeric cursor descending. Pass returned cursor unchanged via the route's `after`/`before` parameter. No cross-scope total counts are exposed.

404 hides guessed/inaccessible organizations and mismatched targets; a forbidden action inside a readable organization returns 403; inactive membership returns 401. A 409 does not overwrite the document or create a successful event. Body/history reads fail if their audit insert fails. Denied/authentication/metadata-read auditing remains incomplete. No delete, export, share, search, vault or permission-administration endpoint exists.
