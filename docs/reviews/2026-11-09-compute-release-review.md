# Compute release admission review

Scope: ADR052's strict PS256 release verifier, independently pinned public trust/scope, derived four-task contracts, shared metadata normalization, bounded CLI, frozen producer fixtures and native synthetic checks. No database/provider controller, launch or fence is added.

The independent security agent reviewed signature/claim/key restrictions, duplicate/unknown-field handling, exact role/image/secret-version/generation/mTLS/workload/EFS/environment/health contracts, the relay-helper extraction and complete-container Terraform assertions. It found no actionable defect in this bounded implementation. The agent also reviewed the no-network/native fixture and its public-only mount, in-memory private signer, fixed output, bounded child completion and owner-label cleanup without another actionable issue. It did not execute tests or AWS calls.

Local tests exposed an IdentityModel signature-provider cache lifetime issue: a temporarily imported RSA object was disposed after verification while a provider could retain it. The verifier now passes copied public RSA parameters so cached providers own their key lifetime. Repeated independent-anchor verification is covered. There is no private RSA material in the verifier.

Short-lived signatures are not single-use authorization. Trusted provider acquisition, durable admission/CAS, uncertain-outcome inspection, explicit Terraform ownership handoff, privileged-writer exclusion, launch/change and exact task/ENI fencing remain required. Source fixtures/mock plans are not managed evidence. The requested independent efficiency agent remains unavailable after its account usage limit; self-review and passing tests cannot replace that review.
