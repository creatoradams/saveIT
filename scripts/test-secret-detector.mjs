import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  mkdtempSync,
  mkdirSync,
  readFileSync,
  rmSync,
  rmdirSync,
  writeFileSync,
} from "node:fs";
import { resolve, join } from "node:path";
import { spawnSync } from "node:child_process";
import { securityTool } from "./security-tools.mjs";

mkdirSync(".local/security", { recursive: true });
const directory = mkdtempSync(resolve(".local/security/canary-"));
const file = join(directory, "injected.txt");
const report = join(directory, "result.json");
// Deliberately fake runtime-only token; no account/API call or committed canary secret.
const token =
  "ghp_" +
  createHash("sha256")
    .update("SaveIT runtime scanner canary v1")
    .digest("hex")
    .slice(0, 36);
writeFileSync(file, `token = "${token}" // gitleaks:allow\n`);
try {
  const result = spawnSync(
    securityTool("gitleaks").path,
    [
      "dir",
      directory,
      "--config=.gitleaks.toml",
      "--gitleaks-ignore-path=.gitleaksignore",
      "--ignore-gitleaks-allow",
      "--redact=100",
      "--no-banner",
      "--log-level=error",
      "--report-format=json",
      `--report-path=${report}`,
      "--timeout=30",
    ],
    {
      encoding: "utf8",
      windowsHide: true,
      timeout: 40_000,
      maxBuffer: 2 * 1024 * 1024,
    },
  );
  assert.equal(
    result.status,
    1,
    "Secret canary must fail the gate despite inline allow comment.",
  );
  const output = readFileSync(report, "utf8");
  const findings = JSON.parse(output);
  assert.ok(findings.some((x) => x.RuleID === "github-pat"));
  assert.ok(
    !output.includes(token) && !(result.stdout + result.stderr).includes(token),
    "Secret canary output must be fully redacted.",
  );
  console.log(
    "Pinned secret detector rejects and redacts the synthetic canary; inline allow comments do not bypass it.",
  );
} finally {
  // Only known files under the uniquely created workspace directory; no recursive deletion.
  rmSync(file, { force: true });
  rmSync(report, { force: true });
  rmdirSync(directory);
}
