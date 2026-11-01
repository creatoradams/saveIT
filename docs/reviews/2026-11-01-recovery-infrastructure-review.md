# Recovery rehearsal and state-bootstrap review

Separate efficiency/security agents reviewed the source read-only. The primary agent ran local checks and inspected actual CI. These reviews are not an independent penetration test, managed recovery drill or AWS authorization assessment.

## Synthetic recovery

The efficiency review found inner container shell blocks could mask failed tar/copy steps. Every block now fails immediately; the source must have persisted key XML, the destination must be empty and carry the fixture's Compose project label, and cleanup removes only that owned recovery volume. Security review found no further issue in quiescence, privileged fixture restore with owners/grants/RLS retained, separate targets, restricted worker rebuild or browser/session/history/isolation/write proof. Both agents rechecked the final source.

The first Linux rehearsal successfully restored database/key targets and read the current document using the old cookie, then hit a new test using a nonexistent per-revision route. It now uses the existing audited bounded history page and checks both exact snapshots. No application route or authorization was changed.

Commit **1bb1f8e39cc7bea3adec3d7ecf0cc02695b2adae** passed [packaged CI 41324591986](https://github.com/creatoradams/saveIT/actions/runs/41324591986), all three real Production-mode flows plus the final worker failure transition, and [general CI 41324591848](https://github.com/creatoradams/saveIT/actions/runs/41324591848). The latter includes the complete backend/browser/build/contract/ignore/audit/scaffold gates. Original cluster roles/CA remain; no managed PITR, independent host/account, offsite/immutable backup, KMS recovery or achieved RPO/RTO is claimed. The fixture-only browser certificate exception remains; curl/DB verify their CA.

## AWS state bootstrap

Only an unapplied state-storage root is implemented; the application cell remains a scaffold. Explicit account/region/role inputs, private versioned ownership-enforced S3, rotating KMS and distinct named state/lock permissions have native mocked-plan tests. The official AWS6.71.0 provider has signed Windows/Linux lock hashes. No AWS credentials/API/apply or real resources were used.

Security review found explicit aws:kms without a key ID could select aws/s3, and broader ordinary-role IAM grants could change bucket lifecycle/protections or key administration despite data-plane restrictions. Explicit missing-key and nonadministrator bucket/key control-plane denies fix those cases. Both agents reviewed the scopes; the security agent rechecked all remedies without further actionable findings. Root/key administrators remain privileged control-plane/recovery authorities.

Local formatting/validation, nine native mocked plan cases and extended ignore policy pass. The tests assert actual conditions/resources and a narrow documented encryption-header truth table; they are not an IAM simulator. Linux CI is pending for this delta. Actual principal existence, IAM enforcement, lockout safety, backend migration/version/key recovery, account auditing and a reviewed real plan/apply remain staging gates. Secret/backend/state/plan files stay ignored, while locks and sanitized examples remain trackable. No enterprise capability was removed or marked vendor-verified.

## Explicit certificate TLS

Further source review confirmed Npgsql10.0.3 attempts GSS before TLS and returns on successful GSS negotiation; SSL Mode VerifyFull alone therefore does not promise certificate TLS when ambient/server GSS support exists. This is alternative encrypted transport, not an observed cleartext downgrade. Common normalization now explicitly serializes GSS Disable and rejects explicit Require, including before loopback exceptions. API/readiness/worker/deployment paths use it. The regression covers omitted/Prefer/Disable/Require and ambient PGGSSENCMODE with guaranteed environment restoration. Nine focused transport/retry cases pass locally; the security agent rechecked all call paths and found no further issue. No live Kerberos handshake was performed. Full new CI is pending. See [versioned provider source](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/NpgsqlConnector.cs#L904) and [official GSS guidance](https://www.npgsql.org/doc/security.html#gss-session-encryption-gss-api).

Final exact-source verification: b627dec1bda044f8c38d6416e870d66f16e1df1f passed general CI41327037222 (380 backend/zero skipped,61 Chromium,nine mocked plans and build/contract/ignore/audit gates) and packaged CI41327037269 (all three flows, verified database TLS and isolated logical recovery). Both source reviewers rechecked the transport remedy without further findings. Linux CI confirms the configured artifacts; no live Kerberos/AWS/Okta service was used. The same three moderate npm development-tool reports remain.
