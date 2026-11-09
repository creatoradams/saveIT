# Relay network and discovery source review

Scope: [ADR046](../adr/046-relay-network-and-private-discovery.md), optional endpoint relay group/path metadata, private discovery transport selection and API-owned origin. No application authority, schema, runtime dependency or new Terraform root changes.

The requested security agent reviewed the design and concrete source/tests. It found no actionable security defect after checking separate endpoint callers, exact network/provider metadata, replaced direct API directions, protected DNS registry and actual producer contracts. The requested efficiency agent found no actionable correctness or bloat defect; it checked single rule ownership, known resource keys, acyclic producer contracts and replaced direct access. Their wording correction to the task output description was applied, and protected withdrawal limits are explicit in ADR046. Agent reviews are not independent penetration tests.

Local affected suites pass: endpoints76, discovery51 and tasks54. Complete verification is recorded in [progress](../progress.md) after execution; prior implementation evidence remains identified separately until then. No AWS API, credentials, apply, secret population or registry publication is used.

Limits: initial null compatibility is not safe operated withdrawal/failover; protected registry removal may block a transition and null can restore the direct declaration. SG aggregation/attachments, Amazon resolver paths, actual DNS/TLS/health, managed IAM/KMS/PKI, direct Logs customer CallerAccount, caller authentication and fencing remain unverified. No running relay service or production readiness is claimed.
