# ADR 001: explicit tenant and organization boundaries

Accepted for foundation, 2026-10-28.

A tenant is a subscribing account; an organization is managed inside it. Every tenant-owned row has a non-null tenant key. Use composite tenant/organization foreign keys, EF scope filters/write guards, and forced PostgreSQL RLS. Resolve context from a protected identity cookie and live membership, then set transaction-local database context. Never accept an unverified request tenant ID as authority.

The runtime role is non-owner and NOBYPASSRLS, cannot manage grants, and cannot change historical revisions/audit. Startup rejects privileged roles. Migration/seed owners are separate and may bypass RLS. Tests use the actual application role and direct SQL, not SQLite or EF's in-memory provider.

RLS is defense in depth against omitted filters; the API process still controls the DB session variable. It is not a sandbox against arbitrary code execution/SQL injection in the trusted process. Per-organization policy remains server-side. Cross-account collaboration requires explicit grant models, auditing, and new tests; no implicit sharing is permitted. Regional tenant placement and dedicated enterprise deployment are planned, not implemented.
