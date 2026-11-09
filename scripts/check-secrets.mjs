import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { securityTool } from "./security-tools.mjs";

const tool = securityTool("gitleaks");
mkdirSync(".local/security", { recursive: true });
const report = resolve(".local/security/gitleaks.json");
// CI checkout must fetch reachable history. Only individually reviewed exact fingerprints
// and inherited upstream exclusions apply; inline allow comments are disabled.
if (
  execFileSync("git", ["rev-parse", "--is-shallow-repository"], {
    encoding: "utf8",
    windowsHide: true,
  }).trim() !== "false"
)
  throw new Error(
    "Secret detection requires a full checkout, not shallow history.",
  );
const result = spawnSync(
  tool.path,
  [
    "git",
    ".",
    "--log-opts=--all --full-history",
    "--config=.gitleaks.toml",
    "--gitleaks-ignore-path=.gitleaksignore",
    "--ignore-gitleaks-allow",
    "--no-banner",
    "--redact=100",
    "--log-level=error",
    "--timeout=120",
    "--report-format=json",
    `--report-path=${report}`,
  ],
  {
    encoding: "utf8",
    windowsHide: true,
    timeout: 150_000,
    maxBuffer: 2 * 1024 * 1024,
  },
);
// Do not echo scanner output/source/secret matches, including when the tool fails.
if (![0, 1].includes(result.status) || result.error)
  throw new Error("Secret detection did not complete successfully.");
const findings = JSON.parse(readFileSync(report, "utf8"));
if (!Array.isArray(findings))
  throw new Error("Malformed secret detection result.");
const counts = Object.create(null);
for (const finding of findings) {
  if (typeof finding.RuleID !== "string")
    throw new Error("Malformed secret detection finding.");
  counts[finding.RuleID] = (counts[finding.RuleID] ?? 0) + 1;
}
const summary = {
  schemaVersion: 1,
  scanner: "gitleaks",
  version: tool.version,
  history: "all reachable refs",
  findings: findings.length,
  rules: counts,
};
writeFileSync(
  ".local/security/secrets-summary.json",
  JSON.stringify(summary, null, 2) + "\n",
);
console.log(JSON.stringify(summary));
if (result.status !== 0 || findings.length !== 0) process.exitCode = 1;
