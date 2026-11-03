import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { rmSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { readBoundedFile, withBoundedFile } from "./read-bounded-file.mjs";
import { securityTool } from "./security-tools.mjs";

const maximumAge = 120 * 60 * 60 * 1000;
const names = ["api", "worker", "deployment", "web"];
const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
export function assessVulnerabilities(
  report,
  image,
  revision,
  now = Date.now(),
) {
  const status = report?.descriptor?.db?.status;
  const built = Date.parse(status?.built);
  const config = report?.descriptor?.configuration;
  if (
    report?.descriptor?.name !== "grype" ||
    report.descriptor.version !== securityTool("grype").version ||
    config?.["only-fixed"] !== false ||
    config["only-notfixed"] !== false ||
    config["ignore-wontfix"] !== "" ||
    !Array.isArray(config.exclude) ||
    config.exclude.length !== 0 ||
    config.db?.["validate-by-hash-on-start"] !== true ||
    config.db["validate-age"] !== true ||
    config.db["require-update-check"] !== true ||
    config.db["auto-update"] !== true ||
    config.db["max-update-check-frequency"] !== 0 ||
    config.db["max-allowed-built-age"] !== maximumAge * 1_000_000 ||
    config.db["update-url"] !== "https://grype.anchore.io/databases" ||
    status?.valid !== true ||
    !/^v6\.[0-9]+\.[0-9]+$/.test(status?.schemaVersion) ||
    !Number.isFinite(built) ||
    built < now - maximumAge ||
    built > now + 5 * 60 * 1000 ||
    !/^sha256:[a-f0-9]{64}$/.test(image?.configurationDigest) ||
    !/^[a-f0-9]{40}$/.test(revision) ||
    report.source?.type !== "image" ||
    report.source.target?.imageID !== image.configurationDigest ||
    report.source.target.labels?.["org.opencontainers.image.revision"] !==
      revision ||
    !Array.isArray(report.matches) ||
    (report.ignoredMatches !== undefined &&
      (!Array.isArray(report.ignoredMatches) ||
        report.ignoredMatches.length !== 0)) ||
    (report.alertsByPackage !== undefined &&
      (!Array.isArray(report.alertsByPackage) ||
        report.alertsByPackage.length !== 0))
  )
    throw new Error(
      "Image scan has invalid/stale database, source, tool or coverage metadata.",
    );
  const counts = {
    Critical: 0,
    High: 0,
    Medium: 0,
    Low: 0,
    Negligible: 0,
    Unknown: 0,
  };
  for (const match of report.matches) {
    const severity = match.vulnerability?.severity;
    if (
      !Object.hasOwn(counts, severity) ||
      typeof match.vulnerability.id !== "string" ||
      typeof match.artifact?.name !== "string" ||
      typeof match.artifact.version !== "string"
    )
      throw new Error("Unrecognized vulnerability finding metadata.");
    counts[severity]++;
  }
  return {
    counts,
    databaseBuilt: status.built,
    databaseSchema: status.schemaVersion,
    failed: counts.Critical > 0 || counts.High > 0 || counts.Unknown > 0,
  };
}

export function verifyScannerStatus(status, counts) {
  const threshold = counts.High > 0 || counts.Critical > 0;
  // Pinned Grype uses 2 for above-threshold findings, 1 for tool failures.
  if (![0, 2].includes(status) || (status === 2) !== threshold)
    throw new Error(
      "Image scanner exit status disagrees with its finding threshold.",
    );
}

export function scanImages(directory = "test-results/release-inventory") {
  const root = resolve(directory),
    path = resolve(root, "release.json");
  const release = JSON.parse(readBoundedFile(path, 64 * 1024));
  if (
    release.schemaVersion !== 1 ||
    !/^[a-f0-9]{40}$/.test(release.sourceRevision) ||
    !Array.isArray(release.images) ||
    release.images.length !== names.length ||
    new Set(release.images.map((x) => x.name)).size !== names.length ||
    release.images.some((x) => !names.includes(x.name))
  )
    throw new Error("Expected the exact four-image release manifest.");
  const tool = securityTool("grype");
  // Host GRYPE_* settings must not silently weaken the checked repository config.
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key]) => !key.toUpperCase().startsWith("GRYPE_"),
    ),
  );
  const results = [];
  for (const image of release.images) {
    const file = `${image.name}.syft.json`;
    const binding = image.sboms?.find((x) => x.file === file);
    const reportFile = `${image.name}.grype.json`,
      reportPath = resolve(root, reportFile);
    rmSync(reportPath, { force: true });
    const processResult = withBoundedFile(
      resolve(root, file),
      64 * 1024 * 1024,
      (bytes, fd) => {
        if (
          !/^[a-f0-9]{64}$/.test(binding?.sha256) ||
          hash(bytes) !== binding.sha256
        )
          throw new Error("Native image inventory hash mismatch.");
        const source = JSON.parse(bytes).source;
        if (
          source?.type !== "image" ||
          source.metadata?.imageID !== image.configurationDigest ||
          source.metadata.labels?.["org.opencontainers.image.revision"] !==
            release.sourceRevision
        )
          throw new Error("Native image inventory source mismatch.");
        // Grype's documented stdin mode has no source argument. Inherit the checked
        // seekable handle: large Windows input pipes can report EOF on threshold exit.
        return spawnSync(
          tool.path,
          [
            "--config",
            ".grype.yaml",
            "--quiet",
            "-o",
            "json",
            "--file",
            reportPath,
            "--fail-on",
            "high",
          ],
          {
            env: environment,
            windowsHide: true,
            encoding: "utf8",
            timeout: 600_000,
            stdio: [fd, "pipe", "pipe"],
            maxBuffer: 2 * 1024 * 1024,
          },
        );
      },
    );
    // Never echo raw scanner output, image configuration or metadata on failures.
    if (processResult.error || ![0, 2].includes(processResult.status))
      throw new Error(
        "Image vulnerability scan did not complete successfully.",
      );
    const reportBytes = readBoundedFile(reportPath, 64 * 1024 * 1024);
    const summary = assessVulnerabilities(
      JSON.parse(reportBytes),
      image,
      release.sourceRevision,
    );
    verifyScannerStatus(processResult.status, summary.counts);
    results.push({
      name: image.name,
      ...summary,
      report: { file: reportFile, sha256: hash(reportBytes) },
    });
    console.log(JSON.stringify({ image: image.name, ...summary }));
  }
  release.vulnerabilities = {
    scanner: { name: "grype", version: tool.version },
    checkedAt: new Date().toISOString(),
    failed: results.some((x) => x.failed),
    images: results,
  };
  writeFileSync(path, JSON.stringify(release, null, 2) + "\n");
  if (release.vulnerabilities.failed) process.exitCode = 1;
}
if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  scanImages(process.argv[2]);
