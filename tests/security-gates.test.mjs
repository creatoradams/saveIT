import { test } from "node:test";
import assert from "node:assert/strict";
import { assessCodeql } from "../scripts/check-codeql.mjs";
import { validateImageInventory } from "../scripts/release-inventory.mjs";
import { securityTool } from "../scripts/security-tools.mjs";
import { readBoundedFile } from "../scripts/read-bounded-file.mjs";
import {
  mkdirSync,
  mkdtempSync,
  rmSync,
  rmdirSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";

test("Report/cache reads enforce limits on regular files through a single handle", () => {
  mkdirSync(".local/security", { recursive: true });
  const directory = mkdtempSync(resolve(".local/security/read-canary-"));
  const file = resolve(directory, "input.json");
  try {
    writeFileSync(file, "abc");
    assert.equal(readBoundedFile(file, 3).toString(), "abc");
    assert.throws(() => readBoundedFile(file, 2));
    assert.throws(() => readBoundedFile(file, -1));
    assert.throws(() => readBoundedFile(directory, 100));
  } finally {
    rmSync(file, { force: true });
    rmdirSync(directory);
  }
});

const report = (score = "6.0") => ({
  version: "2.1.0",
  runs: [
    {
      tool: {
        driver: {
          name: "CodeQL",
          rules: [
            {
              id: "test/security",
              properties: { "security-severity": score, tags: ["security"] },
            },
          ],
        },
      },
      results: [{ ruleId: "test/security" }],
    },
  ],
});
test("CodeQL gate classifies security scores and reports high findings", () => {
  assert.equal(assessCodeql(report()).medium, 1);
  assert.equal(assessCodeql(report("7.0")).high, 1);
  assert.equal(assessCodeql(report("10.0")).high, 1);
  const explicit = report("6.0");
  explicit.runs[0].results[0].level = "error";
  assert.equal(assessCodeql(explicit).errors, 1);
  const inherited = report("2.0");
  inherited.runs[0].tool.driver.rules[0].defaultConfiguration = {
    level: "error",
  };
  assert.equal(assessCodeql(inherited).errors, 1);
});
test("CodeQL refuses unknown metadata, malformed reports and failed execution", () => {
  for (const value of [
    null,
    { version: "2.1.0", runs: [] },
    report("NaN"),
    report("11"),
    report(" "),
  ])
    assert.throws(() => assessCodeql(value));
  const missing = report();
  delete missing.runs[0].tool.driver.rules[0].properties["security-severity"];
  assert.throws(() => assessCodeql(missing));
  const failed = report();
  failed.runs[0].invocations = [{ executionSuccessful: false }];
  assert.throws(() => assessCodeql(failed));
  const absent = report();
  absent.runs[0].results = [{ ruleId: "unrecognized" }];
  assert.throws(() => assessCodeql(absent));
});
test("Unsigned image inventory binds source, nonroot identity, scanner and native image ID", () => {
  const revision = "a".repeat(40),
    id = "sha256:" + "b".repeat(64),
    manifestDigest = "sha256:" + "d".repeat(64);
  const image = {
    Id: id,
    Config: {
      User: "1654",
      Labels: { "org.opencontainers.image.revision": revision },
    },
  };
  const native = {
    descriptor: { name: "syft", version: securityTool("syft").version },
    source: {
      type: "image",
      name: "saveit:test",
      version: id,
      metadata: {
        imageID: id,
        manifestDigest,
        labels: { "org.opencontainers.image.revision": revision },
      },
    },
    artifacts: [{}],
  };
  const cyclone = {
    bomFormat: "CycloneDX",
    specVersion: "1.6",
    metadata: {
      tools: {
        components: [{ name: "syft", version: securityTool("syft").version }],
      },
      component: {
        type: "container",
        name: native.source.name,
        version: id,
      },
      properties: [
        {
          name: "syft:image:labels:org.opencontainers.image.revision",
          value: revision,
        },
      ],
    },
    components: [{}],
  };
  assert.equal(
    validateImageInventory(image, native, cyclone, revision, "saveit:test")
      .configurationDigest,
    id,
  );
  for (const candidate of [
    { ...image, Id: "untrusted" },
    { ...image, Config: { ...image.Config, User: "0:0" } },
    { ...image, Config: { ...image.Config, User: "root" } },
    ...[
      "00",
      "+0",
      "00:1000",
      "node",
      "root:1000",
      "-1",
      "2147483648",
      "1000:0",
    ].map((User) => ({ ...image, Config: { ...image.Config, User } })),
    { ...image, Config: { ...image.Config, Labels: {} } },
  ])
    assert.throws(() =>
      validateImageInventory(
        candidate,
        native,
        cyclone,
        revision,
        "saveit:test",
      ),
    );
  assert.throws(() =>
    validateImageInventory(
      image,
      {
        ...native,
        source: {
          ...native.source,
          metadata: {
            ...native.source.metadata,
            imageID: "sha256:" + "c".repeat(64),
          },
        },
      },
      cyclone,
      revision,
      "saveit:test",
    ),
  );
  assert.throws(() =>
    validateImageInventory(
      image,
      { ...native, artifacts: [] },
      cyclone,
      revision,
      "saveit:test",
    ),
  );
  assert.throws(() =>
    validateImageInventory(
      image,
      native,
      { ...cyclone, components: [] },
      revision,
      "saveit:test",
    ),
  );
  for (const metadata of [
    {
      ...cyclone.metadata,
      tools: { components: [{ name: "syft", version: "untrusted" }] },
    },
    {
      ...cyclone.metadata,
      properties: [
        ...cyclone.metadata.properties,
        {
          name: "syft:image:labels:org.opencontainers.image.revision",
          value: "contradiction",
        },
      ],
    },
    {
      ...cyclone.metadata,
      component: { ...cyclone.metadata.component, version: manifestDigest },
    },
    {
      ...cyclone.metadata,
      component: { ...cyclone.metadata.component, name: "another-image" },
    },
    { ...cyclone.metadata, properties: [] },
    {
      ...cyclone.metadata,
      properties: [
        {
          name: "syft:image:labels:org.opencontainers.image.revision",
          value: "c".repeat(40),
        },
      ],
    },
  ])
    assert.throws(() =>
      validateImageInventory(
        image,
        native,
        { ...cyclone, metadata },
        revision,
        "saveit:test",
      ),
    );
  assert.throws(() =>
    validateImageInventory(
      image,
      { ...native, source: { type: "image", target: { imageID: id } } },
      cyclone,
      revision,
      "saveit:test",
    ),
  );
});
