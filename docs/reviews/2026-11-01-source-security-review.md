# Source security and inventory source review

Two user-requested read-only agents reviewed the ADR 026 delta separately for efficiency/correctness and security. They did not edit code or execute tests; this is not an independent assessment.

Findings remedied before CI:

- CodeQL results with medium/low security scores and effective error level must fail independently of the numeric score. Explicit and inherited-level regression cases now cover that path.
- Syft 1.55 uses native `source.metadata`, not the old `target` field. A real Docker-save archive canary caught the schema mismatch; the validator now uses actual metadata.
- Docker tag and archive-derived source versions differ. Both scans now supply explicit source name and configuration-ID aliases, which both formats must preserve. CycloneDX 1.6 is selected explicitly; its default 1.7 output previously failed the stricter validator. Native and CycloneDX tool/revision metadata and image bindings are checked, including contradictory revision properties.
- Named users and UID-zero aliases do not prove nonroot execution. Release images must declare canonical positive numeric UIDs; web uses 1000 and .NET uses its base image's numeric APP_UID. Zero aliases, names and out-of-range IDs fail tests.
- Upstream Gitleaks defaults include path/stopword exclusions. The runbook and ADR state that inherited scope, the exact historical prose fingerprint exception and uncommitted/unreachable-history limits. Inline allow comments remain disabled.

Local verification: three security test groups, the real pinned Syft two-format archive canary (including actual package and wrong-image rejection), the real Gitleaks detection/redaction/inline-ignore canary, zero findings in full reachable Git history, ignore rules, TypeScript/optimized web build and 29 real PostgreSQL deployment cases pass. A final format-pin canary rerun passed after the observed mismatch. Full Linux CodeQL, same-image Docker inventories and general/packaged CI remain pending at this checkpoint; final evidence will be recorded in progress.

No signature, registry promotion, image vulnerability/license scan, arbitrary-secret coverage, package completeness, live provider/cloud verification or production approval is implied. Medium/low CodeQL results and existing moderate development-tool dependency reports require triage. All enterprise capability labels remain in the inventory.

The first actual Linux CodeQL run then found four high file-system races (stat/exists checks followed by reopening paths), which blocked the workflow. Source remediation now checks/reads a single open bounded regular-file handle, does positioned reads so archive stdin remains seekable, closes in finally, uses exclusive cache creation and passes the same hash-verified descriptor to tar. Windows ZIP extraction via a byte pipe failed; real descriptor-based extraction of both pinned tools passes. CodeQL also reports three medium expected network/file flows, retained for explicit triage. Linux re-verification of this remedy is pending. No suppression/threshold weakening was used.
