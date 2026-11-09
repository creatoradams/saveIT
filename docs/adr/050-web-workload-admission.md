# ADR050: keyless web workload admission

Status: implemented source; managed service compatibility remains unverified. This is one part of the [configuration and compute section](../configuration-compute.md), not its completion.

## Decision

Retain verified native web/relay TLS and relay/API mTLS. An optional isolated Cognito **identity pool** issues a standard workload bearer token; customer Okta sign-in and live SQL tenant authority are unchanged. Web holds no PFX or configuration secret. No SaveIT role trusts this pool for STS exchange, and no identity role mapping is created.

The web server obtains credentials from the exact literal ECS relative metadata path, validates its canonical own task role and expiry, and uses pinned official Cognito SDK3.1164.0 SigV4 signing. One request uses one provider identifier and requests60s lifetime. The dedicated transport fixes endpoint/method/target, native TLS verification, response sizes and total deadline; no proxy, redirects, retries, alternate credentials or ambient SDK defaults/profile network fallback. Concurrent callers share one bounded issuance; only the token is cached, for at most20s. The proxy replaces browser-supplied workload headers; readiness uses the same provider. Tokens are not forwarded by relay to API and never become a user principal.

Relay loads one bounded public JWKS from its fixed root-owned image path, verifies the independently configured SHA256, and accepts only canonical RS512 JWTs with reviewed key IDs, exact issuer/pool/provider, authenticated AMR, bounded age, lifetime<=60s and zero clock skew. Duplicate/extra claims, unknown keys, token-supplied key URLs and public/unauthenticated flows fail closed. Current and next public keys can coexist within16 keys; unknown-key rotation requires a reviewed image/configuration update. No network JWKS discovery or automatic trust refresh exists.

The exact-pool IAM capability does **not** restrict requested token duration or developer identifiers. The client requests60s; the receiver independently rejects longer lifetimes. Public Cognito operations cannot be blocked by an IAM Deny alone. This is short-lived bearer admission over TLS, not proof of possession, per-task attestation, immediate revocation or protection against compromised authorized minting. Clock synchronization and actual returned claim/JWKS compatibility are managed gates.

## Infrastructure and launch boundary

Use existing roots: workload-identity creates a developer-only protected pool with null-bootstrap deny-all; runtime-endpoints creates the distinct web-only443 endpoint path and complete principal/action/pool/TLS policy; workload-identity rebinds the exact independently identified endpoint before enabling its six-statement allow/explicit-deny boundary. The endpoint lookup must remain plan-time independent of a newly created pool ARN; the separate owned-pool equality preconditions prevent cross-binding. Web remains denied secret/configuration and all other AWS actions. ECS tasks receive public binding metadata only. Null preserves the legacy fixture/bootstrap contract and must not be mistaken for authenticated intake.

ADR049 accepts an optional independently expected workloadAdmission object and requires the exact four corresponding observed relay environment entries. Observed environment cannot enable or remove the binding itself. This check still authenticates neither its supplied metadata nor launch approval. All four services remain fixed at desired0. Protected acquisition, signing/promotion, launch/change/drain/fencing and broader ingress/Okta egress remain unfinished engineering.

## Validation and limits

Tests include real RSA signatures and native bound TLS/mTLS relay requests, SDK-signed issuance/concurrency/cancellation/native HTTP limits, and fresh subprocess environment/profile poison with an actual unpinned SDK IMDS positive control. Mock plans check complete policies and staged actual module outputs; they are declared contracts, not AWS IAM/network evidence. The packaged fixture exercises the real Next process, SDK signing against a synthetic TLS provider, bound relay and existing mTLS API; its public JWKS alone enters deployment build context. See [progress](../progress.md) for executed results.

Actual AWS issuance/public-operation behavior, issuer/AMR/JWKS rotation, source endpoint and transitive IAM enforcement remain required before use. No credentials, apply, real AWS requests, registry publication or live Okta are involved in synthetic validation.
