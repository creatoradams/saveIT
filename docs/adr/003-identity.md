# ADR 003: an explicit development identity adapter

Accepted for local development, 2026-10-28. Enterprise connection decision added in [ADR 010](010-okta-identity.md), 2026-10-29: the user selected Okta; the adapter is implemented but live provider setup remains unverified.

Use only three allowlisted synthetic profiles: Alice (two organization editor grants in Northstar), Sam (one read-only grant in Northstar), Bob (one editor grant in Southbank). Both tenants deliberately use Acme Manufacturing / Core switch. Profiles map server-side to persisted memberships. No arbitrary user/tenant headers or tokens are trusted.

The adapter requires `ASPNETCORE_ENVIRONMENT=Development` AND `DevelopmentIdentity__Enabled=true`. Default opt-in is false. Enabling it outside Development throws; it cannot coexist with Okta. Nondevelopment startup requires configured Okta and an explicit protected persisted key ring. Development cookies use ASP.NET Data Protection, HttpOnly, Strict SameSite, fixed two-hour lifetime; antiforgery covers login/logout and every mutation. This is synthetic local simulation, not authentication suitable for production.

The first Okta connection maps verified subject/issuer to a preprovisioned membership in a server-selected tenant. Do not conflate AWS workload identity with customer identity. SAML, SCIM, phishing-resistant MFA policy verification, enterprise entitlements, regional processing, licensing, break-glass and broader lifecycle/session revocation remain release work. Production requires operated HTTPS, secure cookie/key lifecycle, trusted origins/proxies and live tests. No bearer tokens belong in browser storage.
