# Source security and release inventories

See [ADR 026](adr/026-source-security-and-image-inventory.md) and [progress](progress.md) for design and execution evidence. These gates do not establish enterprise readiness.

From the repository root with Node 24, Git, tar and network access to official GitHub releases:

```powershell
node scripts/security-tools.mjs gitleaks
npm run test:security
node scripts/test-secret-detector.mjs
npm run check:secrets
node scripts/security-tools.mjs syft
node scripts/test-image-inventory.mjs
```

The installer verifies versioned official asset hashes from `scripts/security-tools.json` before extracting the executable under ignored `.local/tools`. It supports Windows/Linux x64. Hash pinning is not publisher signature verification. Refresh pins only after checking official releases and re-running canaries/CI.

`check:secrets` requires full Git history and scans all reachable refs present in the checkout, including deleted lines. It does not scan untracked/uncommitted worktree files, unreachable objects or refs not fetched. Gitleaks 8.30.1 inherits upstream default rules, stopwords and global exclusions, including lockfiles, dependency directories and several binary/image formats. No claim of universal secret coverage follows. Keep dependency locks tracked and never put secrets in them or excluded formats. The only repository-added exception is an exact historical documentation-prose fingerprint in `.gitleaksignore`; inline allow comments are disabled. Review any new exception narrowly. The runtime canary verifies detection/redaction within included paths; it does not test every rule or excluded format. No token is issued or sent to a provider.

On failure, inspect ignored redacted `.local/security/gitleaks.json` locally; CI logs only aggregate counts. Never paste source matches, environment values or credentials into logs/issues. A real exposure requires revocation/rotation and an authorized history-remediation decision; adding an ignore does not remedy it. Reports and SARIF stay ignored. Existing dependency audit findings are not waived by the new checks.

`SaveIT source security` also runs CodeQL security-extended for C#, JavaScript/TypeScript and Actions. C# uses a manual instrumented build. `check-codeql.mjs` refuses malformed/failed reports and blocks security severity >=7 or any effective error result; lower scores are retained for review. SARIF artifacts expire after seven days and contain source locations, so restrict repository/artifact access appropriately. Medium/low findings need triage even when the workflow passes. No claim of independent assessment follows from automated scans.

The production-artifacts workflow labels the four actual process images with the source SHA, runs packaged behavior first, verifies the real Syft archive canary and generates native/CycloneDX SBOMs for those same images. `test-results/release-inventory/release.json` binds each inventory to its local image configuration ID and source revision, with hashes of both SBOM files. The scanner receives an explicit source name and configuration-ID version because ordinary tag-derived versions differ between Docker/archive sources. Native metadata and CycloneDX container/revision metadata must agree; nonroot identity must be a canonical positive numeric UID. Inventory artifacts expire after fourteen days. No images are published.

For a manual image build, pass `--build-arg SAVEIT_REVISION=<full-40-character-source-SHA>` to both Dockerfiles. After all packaged checks pass, run `npm run inventory:images` with those `saveit-{api,worker,deployment,web}:test` images present. The source tree must correspond to that exact revision; labels are declared build metadata, not proof against a malicious builder. No account-free inventory signature is claimed. Registry digest binding, signed provenance, license policy and ADR 027 final patched-image verification, protected promotion, enforced branch/environment gates and cloud deployment remain unfinished.

## Full-image vulnerability gate

ADR 027 adds `node scripts/security-tools.mjs grype` and `npm run scan:images` after all packaged checks and native/CycloneDX inventory generation. The wrapper checks inventory hashes/source, streams those exact bytes to the pinned scanner and rejects high/critical/unknown severities, actual ignored matches and coverage alerts. It clears host GRYPE overrides and requires a current (at most 120h), checksum-validated database with successful update checks. The public database can require several gigabytes of local storage; the first download can take minutes. Network/update/tool/report failures fail the release. Inventory data is processed locally, not uploaded to a scanner service.

The report gate checks selected format/coverage/configuration fields, not a complete JSON Schema or package-completeness proof. Grype retains upstream kernel-header indirect-match defaults; no repository vulnerability exceptions exist. Lower-severity results stay in counts/raw artifacts for triage. Scanner recognition, distribution metadata and advisory quality limit the result; a green scan does not prove exploitability absence, license compliance, signed provenance or promotion approval. Re-run at release time. The workflow retains reports with source/image hashes and DB build/schema in the release manifest, even on a vulnerability failure. Never embed secrets in images/configuration. Actual execution status is in progress.
