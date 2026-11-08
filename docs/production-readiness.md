# SaveIT production-readiness reassessment

**SaveIT is a tested local preview, not a production release.** Credential-free engineering remains before account setup becomes the next dependency. The [current progress/evidence](progress.md) is authoritative; historical checkpoints are preserved in [delivery history](progress-history.md). The full [enterprise backlog](backlog.md) and all 146 [inventory entries](feature-parity.md) remain open according to their individual status.

## What is ready to exercise locally

The core documentation/inventory workflow persists in real PostgreSQL with live membership/organization checks, 28 forced-RLS tables, scoped foreign keys and atomic business/history/audit/outbox/queue writes. Search is a restricted derived PostgreSQL projection that rechecks current authority/source. Browser flows include conflicts, history/comparison/restoration, scoped folders and reviewed templates.

Production-mode containers, seed-free deployment/provisioning, isolated access operations, bounded configuration/health tools, the relay mTLS path and a synthetic logical database/key recovery drill have CI evidence. The Okta adapter is implemented and tested with a synthetic external identity provider. Cloud definitions are mock-tested and unapplied. [Progress](progress.md) gives exact source/run links and current lower security findings; passing tests do not establish production safety or capacity.

## Remaining gates

| Priority | Work that can proceed without credentials | Evidence requiring authorized external setup |
| --- | --- | --- |
| 1 — Configuration and compute | Verify the [ADR045 endpoint filters](adr/045-relay-endpoint-policy-admission.md), then dedicated relay network/discovery/task/service bindings; authenticated intake and ingress/restricted egress | Actual role isolation, ECR pull, protected secret delivery, DNS/packet paths, managed CA/TLS and PKI issuance/rotation/revocation |
| 2 — Protected release and operations | Signing/provenance and immutable promotion/rollback; restricted scheduler/PassRole/operator paths; health/drain and old-instance fencing/runbooks | Approved registry publication, rollback, operator credential lifecycle and recovery/fencing exercises |
| 3 — Identity and onboarding | Finish operating procedures and broader connection/lifecycle design | Real Okta issuer/app, assignment/MFA/assurance, subject mapping, existing-cookie offboarding and session/key behavior |
| 4 — Telemetry and resilience | Redacted collection/alerts, API/tenant quotas, denied/auth/admin auditing, protected audit archive/retention and worker operations | Managed database/config/key backup, independent-target/offsite/PITR restore, multi-instance key access and measured recovery |
| 5 — Release assurance | Remediate tracked findings; representative scenarios, capacity harnesses, accessibility/incident/support procedures and patch ownership | Staging measurements, independent security/accessibility assessment and explicit pilot approval |

The target remains AWS with Okta; the user has not yet supplied an AWS staging account or real Okta configuration. Accounts, region/residency, budget, licensing, regulated-data scope and support ownership require decisions before provisioning. Never send secrets in chat or put them in Terraform, CLI arguments, Git or test evidence.

## Specific unresolved boundaries

- The separate relay identity root reserves exact whole-secret ARN/UUID/via-Secrets-Manager/account/context authority for the implemented ADR044 task loader. Execution owns exact image/own-log authority. Null bootstrap denies all AWS actions; bound metadata is not launch approval. ADR045 optionally admits the distinct callers in endpoint filters, with no new relay network path or IAM grant; default-null endpoints retain only legacy callers.
- **Pre-apply compatibility gate:** direct regional Logs customer kms:CallerAccount availability is unverified. The retained restrictive key policy may deny real service calls when context is absent. Obtain managed context/DescribeKey/PutLogEvents and wrong-scope denial evidence, or separately review an account-bound replacement before apply. Do not infer compatibility from fabricated mock contexts.
- All infrastructure roots are unapplied. Mock plans and declared cross-root contracts do not prove actual/transitive IAM, SG/DNS drift, mounts, injection, ingestion or service behavior. Trusted administrators/custodians can change policies; custodian restrictions are not exclusive payload-writer proof.
- Relay intake validates server TLS and does not authenticate web workload identity. User/tenant authority still comes from protected sessions and live database checks. API response completion cannot recall downstream buffers; lost protocol state requires operated fencing, which remains unfinished.
- The recovery drill retains the same cluster/roles/CA. It does not establish managed/PITR/offsite/KMS recovery, achieved RPO/RTO or asset/deletion recovery.
- Four medium JavaScript findings and lower image findings remain tracked in progress. Synthetic tests and agent reviews do not replace independent penetration/cryptographic/deployment assessment. Windows full-suite PostgreSQL backend-exit limitations remain separate from verified Linux execution.
- Current live identity configuration supports one issuer mapped to one SaveIT tenant. Multi-customer connections, SCIM/SAML/lifecycle, delegated permission administration and customer onboarding require further implementation and evidence.

## Proposed first release

An invitation-only documentation/inventory pilot with one real Okta connection, explicitly approved users/data and multiple authorized organizations is a proposal, not a reduction of enterprise scope. Staging stays synthetic until applicable gates pass. Vault/password/OTP, uploads, connectors, rich collaboration, portals, mobile/extensions, discovery, AI and other absent features must remain visibly unimplemented.

Do not publish a percentage-complete estimate, launch date, vendor-parity claim, SLO or compliance claim without evidence. [Completion register](completion-register.md) separates implemented code from remaining scope. Earlier readiness wording is available at the [pre-cleanup snapshot](https://github.com/creatoradams/saveIT/blob/3711107/docs/production-readiness.md); it is historical, not current status.
