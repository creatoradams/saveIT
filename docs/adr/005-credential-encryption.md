# ADR 005: defer vault implementation pending a reviewed key design

Design comparison recorded 2026-10-28; production choice unresolved.

| Design | Benefits | Costs and required controls |
| --- | --- | --- |
| Server-managed envelope encryption | Scoped unattended rotation/integrations, centralized recovery and controlled server operations | Server/key service compromise can expose values; separate KMS permissions, per-tenant data keys, reviewed encryption libraries, rotation, step-up reveal and complete audit are required |
| Client-side-only encryption | Server cannot directly decrypt content if key distribution is sound | Device enrollment, sharing/key distribution, recovery, compromised clients, and revocation limitations are difficult; unattended server AI/search/rotation over undecryptable ciphertext must not be promised |

Do not invent a cryptographic protocol. Obtain independent design review, define tenant/customer key ownership, device recovery, breach response, export and rotation reconciliation before production implementation. Future core objects hold opaque `CredentialReference` metadata only. Values/OTP seeds never enter logs, indexes, prompts, workflow histories, traces, or analytics; workflows resolve opaque references only in authorized execution contexts.

Offline vault access is a separately controlled, encrypted, time-limited feature; disconnected devices cannot immediately observe revocation. Rotation must recover from a remote change succeeding before local persistence and reconcile uncertain outcomes. No vault, fake reveal, production credential storage, secret sharing, OTP, offline access, or rotation is implemented by this slice.
