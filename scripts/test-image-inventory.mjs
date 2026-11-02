import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { execFileSync } from "node:child_process";
import {
  mkdirSync,
  readFileSync,
  rmSync,
  rmdirSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";
import { securityTool } from "./security-tools.mjs";
import { validateImageInventory } from "./release-inventory.mjs";

// A real Docker-save archive exercises the pinned scanner's actual two output formats
// without a Docker daemon, registry, account or issued credential.
const directory = resolve(
  ".local/security",
  `inventory-canary-${randomUUID()}`,
);
const revision = "a".repeat(40);
const digest = (bytes) =>
  "sha256:" + createHash("sha256").update(bytes).digest("hex");
const files = [];
const write = (name, bytes) => {
  const path = resolve(directory, name);
  writeFileSync(path, bytes);
  files.push(path);
};
mkdirSync(resolve(directory, "layer/app"), { recursive: true });
try {
  write(
    "layer/app/package.json",
    JSON.stringify({
      name: "saveit-synthetic-sbom",
      version: "1.0.0",
      license: "MIT",
    }),
  );
  const tar = (args) =>
    execFileSync("tar", args, {
      stdio: "pipe",
      windowsHide: true,
      timeout: 15_000,
    });
  const layer = resolve(directory, "layer.tar");
  files.push(layer);
  tar(["-cf", layer, "-C", resolve(directory, "layer"), "app"]);
  const config = Buffer.from(
    JSON.stringify({
      created: "2026-11-01T00:00:00Z",
      architecture: "amd64",
      os: "linux",
      config: {
        User: "1000",
        Labels: { "org.opencontainers.image.revision": revision },
      },
      rootfs: { type: "layers", diff_ids: [digest(readFileSync(layer))] },
    }),
  );
  const id = digest(config),
    configName = id.slice(7) + ".json";
  write(configName, config);
  write(
    "manifest.json",
    JSON.stringify([
      {
        Config: configName,
        RepoTags: ["saveit-synthetic:fixture"],
        Layers: ["layer.tar"],
      },
    ]),
  );
  const archive = resolve(directory, "image.tar");
  files.push(archive);
  tar([
    "-cf",
    archive,
    "-C",
    directory,
    "manifest.json",
    configName,
    "layer.tar",
  ]);
  const nativeFile = resolve(directory, "native.json"),
    cycloneFile = resolve(directory, "cyclone.json");
  files.push(nativeFile, cycloneFile);
  execFileSync(
    securityTool("syft").path,
    [
      "scan",
      `docker-archive:${archive}`,
      "--source-name",
      "saveit-synthetic:fixture",
      "--source-version",
      id,
      "--quiet",
      "-o",
      `syft-json=${nativeFile}`,
      "-o",
      `cyclonedx-json@1.6=${cycloneFile}`,
    ],
    {
      stdio: "pipe",
      windowsHide: true,
      timeout: 60_000,
      maxBuffer: 2 * 1024 * 1024,
    },
  );
  const native = JSON.parse(readFileSync(nativeFile)),
    cyclone = JSON.parse(readFileSync(cycloneFile));
  assert.equal(
    validateImageInventory(
      { Id: id, Config: JSON.parse(config).config },
      native,
      cyclone,
      revision,
      "saveit-synthetic:fixture",
    ).configurationDigest,
    id,
  );
  assert(native.artifacts.some((x) => x.name === "saveit-synthetic-sbom"));
  assert(cyclone.components.some((x) => x.name === "saveit-synthetic-sbom"));
  assert.throws(() =>
    validateImageInventory(
      { Id: digest("different image"), Config: JSON.parse(config).config },
      native,
      cyclone,
      revision,
      "saveit-synthetic:fixture",
    ),
  );
  console.log(
    "Real pinned Syft two-format archive canary passed; mismatched image was rejected.",
  );
} finally {
  for (const file of files.reverse()) rmSync(file, { force: true });
  rmdirSync(resolve(directory, "layer/app"));
  rmdirSync(resolve(directory, "layer"));
  rmdirSync(directory);
}
