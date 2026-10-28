# ADR 003: an explicit development identity adapter

Accepted for local development; production provider unresolved, 2026-10-28.

Use only three allowlisted synthetic profiles: Alice (two organization editor grants in Northstar), Sam (one read-only grant in Northstar), Bob (one editor grant in Southbank). Both tenants deliberately use Acme Manufacturing / Core switch. Profiles map server-side to persisted memberships. No arbitrary user/tenant headers or tokens are trusted.

The adapter requires `ASPNETCORE_ENVIRONMENT=Development` AND `DevelopmentIdentity__Enabled=true`. Default opt-in is false. Enabling it outside Development throws; all production startup currently fails because enterprise identity is unconfigured. Cookies use ASP.NET Data Protection, HttpOnly, Strict SameSite, fixed two-hour lifetime; antiforgery covers login/logout and every mutation. This is synthetic local simulation, not authentication suitable for production.

Select an enterprise broker/provider in a follow-up decision after verifying OIDC/SAML, Entra/Okta connections, SCIM, phishing-resistant MFA, enterprise entitlements, regional processing, licensing and break-glass requirements. Map verified subject/issuer to memberships; do not conflate AWS workload identity with customer identity. Production requires HTTPS, secure cookie policy, shared/protected key rings, session revocation, trusted origins/proxies, and tests. Preserve no bearer tokens in browser storage.
