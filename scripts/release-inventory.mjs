import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { securityTool } from "./security-tools.mjs";

const imageNames = ["api", "worker", "deployment", "web"];
export function validateImageInventory(image, native, cyclone, revision, name) {
  if (
    !/^[a-f0-9]{40}$/.test(revision) ||
    !/^sha256:[a-f0-9]{64}$/.test(image?.Id) ||
    image.Config?.Labels?.["org.opencontainers.image.revision"] !== revision
  )
    throw new Error("Image source/configuration digest binding is invalid.");
  const user = image.Config?.User;
  if (!/^[1-9][0-9]{0,9}$/.test(user) || Number(user) > 2147483647)
    throw new Error("Release image must use a canonical positive numeric UID.");
  if (
    native?.descriptor?.name !== "syft" ||
    native.descriptor.version !== securityTool("syft").version ||
    native.source?.type !== "image" ||
    native.source.metadata?.imageID !== image.Id ||
    typeof name !== "string" ||
    name.length === 0 ||
    native.source.name !== name ||
    native.source.version !== image.Id ||
    !/^sha256:[a-f0-9]{64}$/.test(native.source.metadata.manifestDigest) ||
    native.source.metadata.labels?.["org.opencontainers.image.revision"] !==
      revision ||
    !Array.isArray(native.artifacts) ||
    native.artifacts.length === 0
  )
    throw new Error(
      "Native SBOM does not describe the expected image configuration.",
    );
  if (
    cyclone?.bomFormat !== "CycloneDX" ||
    cyclone.specVersion !== "1.6" ||
    cyclone.metadata?.tools?.components?.filter(
      (x) => x.name === "syft" && x.version === securityTool("syft").version,
    ).length !== 1 ||
    cyclone.metadata?.component?.type !== "container" ||
    cyclone.metadata.component.name !== native.source.name ||
    cyclone.metadata.component.version !== image.Id ||
    cyclone.metadata.properties?.filter(
      (x) => x.name === "syft:image:labels:org.opencontainers.image.revision",
    ).length !== 1 ||
    cyclone.metadata.properties.find(
      (x) => x.name === "syft:image:labels:org.opencontainers.image.revision",
    ).value !== revision ||
    !Array.isArray(cyclone.components) ||
    cyclone.components.length === 0
  )
    throw new Error(
      "CycloneDX inventory does not describe the expected image.",
    );
  return {
    configurationDigest: image.Id,
    sourceRevision: revision,
    user,
    packages: native.artifacts.length,
    components: cyclone.components.length,
  };
}
export function createReleaseInventory() {
  const revision =
    process.env.GITHUB_SHA ??
    execFileSync("git", ["rev-parse", "HEAD"], {
      encoding: "utf8",
      windowsHide: true,
    }).trim();
  const syft = securityTool("syft");
  const directory = resolve("test-results/release-inventory");
  mkdirSync(directory, { recursive: true });
  const images = [];
  for (const name of imageNames) {
    const tag = `saveit-${name}:test`;
    const [image] = JSON.parse(
      execFileSync("docker", ["image", "inspect", tag], {
        encoding: "utf8",
        windowsHide: true,
      }),
    );
    const nativeFile = `${name}.syft.json`;
    const cycloneFile = `${name}.cdx.json`;
    execFileSync(
      syft.path,
      [
        "scan",
        `docker:${tag}`,
        "--source-name",
        tag,
        "--source-version",
        image.Id,
        "--quiet",
        "-o",
        `syft-json=${resolve(directory, nativeFile)}`,
        "-o",
        `cyclonedx-json@1.6=${resolve(directory, cycloneFile)}`,
      ],
      {
        stdio: "pipe",
        windowsHide: true,
        timeout: 180_000,
        maxBuffer: 2 * 1024 * 1024,
      },
    );
    const readInventory = (file) => {
      const path = resolve(directory, file);
      if (statSync(path).size > 64 * 1024 * 1024)
        throw new Error("Image inventory is too large.");
      return readFileSync(path);
    };
    const nativeBytes = readInventory(nativeFile);
    const cycloneBytes = readInventory(cycloneFile);
    const verified = validateImageInventory(
      image,
      JSON.parse(nativeBytes),
      JSON.parse(cycloneBytes),
      revision,
      tag,
    );
    const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
    images.push({
      name,
      tag,
      ...verified,
      sboms: [
        { file: nativeFile, sha256: hash(nativeBytes) },
        { file: cycloneFile, sha256: hash(cycloneBytes) },
      ],
    });
  }
  // Local configuration IDs are NOT registry manifest digests, signatures or attestations.
  const manifest = {
    schemaVersion: 1,
    sourceRevision: revision,
    scanner: { name: "syft", version: syft.version },
    images,
  };
  writeFileSync(
    resolve(directory, "release.json"),
    JSON.stringify(manifest, null, 2) + "\n",
  );
  console.log(
    `Verified software inventories for ${images.length} nonroot release images at ${revision}.`,
  );
}
if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  createReleaseInventory();
