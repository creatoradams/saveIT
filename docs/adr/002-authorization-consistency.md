# ADR 002: current database authority and fail-closed access

Accepted for foundation, 2026-10-28.

PostgreSQL membership and organization grants are authoritative. Each request rechecks active membership and its current organization action. Viewer: read metadata/body/revisions. Editor: additionally create/revise and inspect audit. Unknown/inaccessible scopes return 404; known read-only actions return 403. No cached permissions, OpenFGA, search projection, or cross-account inheritance exists yet. All policy checks and data writes share one request transaction at READ COMMITTED.

Administrative revocation is currently a test/owner operation, not a product endpoint. A committed change takes effect for subsequent protected requests with the same cookie. A request authorized before concurrent revocation may complete; adding permission administration requires a revocation barrier/locking or permission-version protocol so success is not acknowledged until affected protected reads cannot disclose data. Do not claim the stronger concurrent guarantee today.

Before adding OpenFGA, caches, or search, define consistency tokens/version checks, deny precedence, inheritance, outage behavior, and revocation acknowledgments. Search ACLs are hints only; recheck current authority for results and side channels. If current authority is unavailable, fail closed. Jobs, files, exports, websockets, integrations, and AI require the same policy interface when added.
