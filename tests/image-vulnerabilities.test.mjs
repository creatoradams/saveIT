import assert from "node:assert/strict";
import { test } from "node:test";
import {
  assessVulnerabilities,
  verifyScannerStatus,
} from "../scripts/scan-image-vulnerabilities.mjs";
import { securityTool } from "../scripts/security-tools.mjs";

const revision = "a".repeat(40),
  image = { configurationDigest: "sha256:" + "b".repeat(64) };
const now = Date.parse("2026-11-02T12:00:00Z");
function report(severity) {
  return {
    descriptor: {
      name: "grype",
      version: securityTool("grype").version,
      configuration: {
        "only-fixed": false,
        "only-notfixed": false,
        "ignore-wontfix": "",
        exclude: [],
        db: {
          "validate-by-hash-on-start": true,
          "validate-age": true,
          "require-update-check": true,
          "auto-update": true,
          "max-update-check-frequency": 0,
          "max-allowed-built-age": 432000000000000,
          "update-url": "https://grype.anchore.io/databases",
        },
      },
      db: {
        status: {
          valid: true,
          built: "2026-11-02T06:00:00Z",
          schemaVersion: "v6.1.10",
        },
      },
    },
    source: {
      type: "image",
      target: {
        imageID: image.configurationDigest,
        labels: { "org.opencontainers.image.revision": revision },
      },
    },
    matches: severity
      ? [
          {
            vulnerability: { id: "synthetic-policy-case", severity },
            artifact: { name: "synthetic-package", version: "1.0.0" },
          },
        ]
      : [],
  };
}
const assess = (value) => assessVulnerabilities(value, image, revision, now);

test("Image vulnerability gate blocks high, critical and unknown severity without hiding lower matches", () => {
  assert.equal(assess(report()).failed, false);
  for (const severity of ["Critical", "High", "Unknown"]) {
    const result = assess(report(severity));
    assert.equal(result.failed, true);
    assert.equal(result.counts[severity], 1);
  }
  for (const severity of ["Medium", "Low", "Negligible"]) {
    const result = assess(report(severity));
    assert.equal(result.failed, false);
    assert.equal(result.counts[severity], 1);
  }
  assert.throws(() => assess(report("invented")));
});

test("Pinned Grype threshold exit2 is distinct from tool failure1 and agrees with report counts", () => {
  assert.doesNotThrow(() => verifyScannerStatus(0, { High: 0, Critical: 0 }));
  assert.doesNotThrow(() => verifyScannerStatus(2, { High: 1, Critical: 0 }));
  assert.doesNotThrow(() => verifyScannerStatus(2, { High: 0, Critical: 1 }));
  for (const status of [null, 1, 3, 0])
    assert.throws(() => verifyScannerStatus(status, { High: 1, Critical: 0 }));
  assert.throws(() => verifyScannerStatus(2, { High: 0, Critical: 0 }));
});

test("Image scan refuses stale/failed databases, wrong images/revisions, suppression and coverage alerts", () => {
  for (const change of [
    (r) => {
      r.descriptor.version = "untrusted";
    },
    (r) => {
      r.descriptor.db.status.valid = false;
    },
    (r) => {
      r.descriptor.db.status.built = "2026-10-27T00:00:00Z";
    },
    (r) => {
      r.descriptor.db.status.built = "2026-11-02T12:06:00Z";
    },
    (r) => {
      r.descriptor.db.status.built = "unknown";
    },
    (r) => {
      r.descriptor.db.status.schemaVersion = "v7.0.0";
    },
    (r) => {
      r.source.target.imageID = "sha256:" + "c".repeat(64);
    },
    (r) => {
      r.source.target.labels = {};
    },
    (r) => {
      r.matches = undefined;
    },
    (r) => {
      r.ignoredMatches = [{}];
    },
    (r) => {
      r.ignoredMatches = {};
    },
    (r) => {
      r.alertsByPackage = [{ type: "distro-eol" }];
    },
    (r) => {
      r.alertsByPackage = {};
    },
    (r) => {
      r.descriptor.configuration["only-fixed"] = true;
    },
    (r) => {
      r.descriptor.configuration.exclude = ["/app/**"];
    },
    (r) => {
      r.descriptor.configuration.db["validate-age"] = false;
    },
    (r) => {
      r.descriptor.configuration.db["require-update-check"] = false;
    },
    (r) => {
      r.descriptor.configuration.db["validate-by-hash-on-start"] = false;
    },
    (r) => {
      r.descriptor.configuration.db["auto-update"] = false;
    },
    (r) => {
      r.descriptor.configuration.db["max-update-check-frequency"] =
        7200000000000;
    },
    (r) => {
      r.descriptor.configuration.db["max-allowed-built-age"] *= 2;
    },
    (r) => {
      r.descriptor.configuration.db["update-url"] = "https://example.invalid";
    },
  ]) {
    const value = report();
    change(value);
    assert.throws(() => assess(value));
  }
  assert.throws(() => assess(null));
});
