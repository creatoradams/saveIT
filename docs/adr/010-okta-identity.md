# ADR 010: first enterprise identity connection uses Okta OIDC

Accepted for implementation, 2026-10-29. The user selected **Okta** and confirmed that no nonproduction AWS/integration account exists yet. No live Okta tenant, application or cloud resource has been connected. The adapter is implemented and tested with signed synthetic OIDC responses; live interoperability and provider policy remain unverified.

Use ASP.NET Core OpenIdConnect 10.0.14 with authorization code + PKCE, HTTPS discovery, signature/issuer/audience/lifetime validation, state, nonce and correlation cookies. Tokens are handled server-side and not saved in the session or browser storage. Login initiation is a CSRF-protected POST. The callback uses OIDC's state/nonce/correlation validation before issuing a protected two-hour HttpOnly Secure SameSite=Lax application cookie. Local development identity remains a separate, mutually exclusive opt-in.

The first connection maps exactly one configured issuer and server-selected tenant. After cryptographic validation, `(TenantId, ExternalIssuer, ExternalSubject)` must match a preprovisioned active membership under tenant RLS. Email matching, automatic enrollment, provider role claims and token-provided tenant IDs grant no access. The resulting cookie uses the persisted membership ID, canonical subject and display name. Every subsequent core request checks current membership/grants again. Removing an Okta assignment alone does not immediately revoke an existing SaveIT session; membership deactivation is required until lifecycle integration exists. Logout clears the application cookie, not the IdP session.

The optional RequiredAcr value is requested and must appear exactly in the validated token. Its meaning must be verified against the actual Okta authorization server and enforced app sign-in policy. An acr string alone does not establish phishing-resistant MFA. SAML, SCIM, multiple tenant connections, automated provisioning, group synchronization, break-glass and IdP lifecycle/session revocation remain planned.

## Nonproduction connection procedure

1. Obtain an authorized Okta tenant and create a confidential web application using the authorization code grant. Review entitlement, authorization-server issuer and app assignment policy. Configure the exact HTTPS callback `<SaveIT public origin>/api/v1/auth/callback`. Do not use the synthetic identity launcher for this deployment.
2. Choose the SaveIT tenant and provision memberships/grants through a separately controlled migration/admin process. Set ExternalIssuer to the exact normalized issuer and ExternalSubject to the verified immutable Okta subject. The API role intentionally cannot modify these fields. There is no public enrollment/admin UI or SCIM endpoint yet; do not enable automatic matching by email.
3. Supply the following server configuration through a secret manager or protected environment. The root `.env.example` contains empty sanitized placeholders and is not automatically loaded:

| Configuration | Meaning |
| --- | --- |
| DevelopmentIdentity__Enabled=false | Disable synthetic login |
| Identity__Okta__Enabled=true | Enable this connection |
| Identity__Okta__Issuer | Exact HTTPS issuer, no query, fragment or userinfo |
| Identity__Okta__ClientId | Confidential application ID |
| Identity__Okta__ClientSecret | Secret from protected configuration; never commit it |
| Identity__Okta__TenantId | Existing server-selected SaveIT tenant UUID |
| Identity__Okta__PublicOrigin | Exact HTTPS browser origin, root path only |
| Identity__Okta__RequiredAcr | Optional exact verified assurance value |
| Identity__KeyDirectory | Persisted Data Protection key directory outside the repository |
| Identity__KeyCertificatePath | Protected PFX path with unexpired private key |
| Identity__KeyCertificatePassword | Secret password from protected configuration |
| SAVEIT_OKTA_ISSUER | Same public issuer on the Next server, for CSP form navigation |

4. Configure the restricted API and search database connections separately. Outside Development, startup requires valid Okta settings and an explicit certificate-protected persisted key ring. Restrict key/certificate filesystem access, distribution, rotation and backup. These guards do not provision a shared key service or prove recovery.
5. Before exposure, operate a reviewed TLS edge, restrict upstream network access/Host routing, verify the fixed public callback through the Next proxy, add trusted-proxy/HSTS rules and harden CSP. The current local proxy and inline framework CSP are a development baseline. Configure redacted access logs; auth callback parameters/tokens and search queries must not enter logs.
6. Run actual Okta success/denial, wrong-issuer/audience, assignment/MFA/assurance, replay/nonce/state, key persistence/restart/rotation, membership deactivation and two-tenant browser tests in the isolated environment. Record evidence before enabling customer use. No such live test is claimed here.

Configuration fails closed for insecure issuer/origin, missing required values, conflicting development identity, unconfigured nondevelopment identity, missing key configuration or invalid protection certificate. Synthetic protocol tests use a local RSA-signed token endpoint and exercise PKCE, state, nonce, issuer, audience, signature, expiry, subject mapping, assurance, ignored provider tenant/roles, secure cookies and membership revocation. They do not substitute for a live provider test or a penetration test.

References reviewed for implementation: [Microsoft OIDC web authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0) and [Okta redirect web application guide](https://developer.okta.com/docs/guides/sign-into-web-app-redirect/asp-net-core-3/main/). Provider setup and licensing must be rechecked for the real account.
