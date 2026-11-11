// Synthetic registry digests, derived from actual packaged image inspection.
// These are deliberately not ECR/registry digests or source-to-binary attestations.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
const digest = (value) =>
  "sha256:" + createHash("sha256").update(value).digest("hex");
export function computeImageFixture(contract, tasks, inspected) {
  const variants = {};
  let revision;
  for (const image of Object.values(inspected)) {
    const value = image.Config.Labels["org.opencontainers.image.revision"];
    assert.match(value, /^[a-f0-9]{40}$/);
    revision ??= value;
    assert.equal(value, revision);
    assert.equal(image.Os, "linux");
    assert.equal(image.Architecture, "amd64");
  }
  for (const mode of [
    "images-valid",
    "images-root",
    "images-entrypoint",
    "images-env",
    "images-index",
  ]) {
    const approved = structuredClone(contract),
      observations = structuredClone(tasks),
      images = {};
    approved.sourceRevision = revision;
    for (const process of ["api", "web", "worker", "relay"]) {
      const inspectedImage = inspected[process];
      const blob = {
        architecture: inspectedImage.Architecture,
        os: inspectedImage.Os,
        config: structuredClone(inspectedImage.Config),
        rootfs: { type: "layers", diff_ids: inspectedImage.RootFS.Layers },
      };
      if (process === "api") {
        if (mode === "images-root") blob.config.User = "0";
        if (mode === "images-entrypoint")
          blob.config.Entrypoint = ["synthetic-private-command"];
        if (mode === "images-env")
          blob.config.Env.push("LD_PRELOAD=synthetic-private-marker");
      }
      const body = JSON.stringify(blob),
        configDigest = digest(body);
      let manifest = {
        schemaVersion: 2,
        mediaType: "application/vnd.oci.image.manifest.v1+json",
        config: {
          mediaType: "application/vnd.oci.image.config.v1+json",
          digest: configDigest,
          size: Buffer.byteLength(body),
        },
        layers: blob.rootfs.diff_ids.map((_, index) => ({
          mediaType: "application/vnd.oci.image.layer.v1.tar+gzip",
          digest: digest("synthetic-compressed-layer-" + index),
          size: 100,
        })),
      };
      if (mode === "images-index" && process === "api")
        manifest = {
          schemaVersion: 2,
          mediaType: "application/vnd.oci.image.index.v1+json",
          manifests: [],
        };
      const original = JSON.stringify(manifest),
        manifestDigest = digest(original);
      const repositoryName =
        "saveit/pilot/staging/" +
        (process === "relay" ? "deployment" : process);
      approved.tasks[process].image =
        "111122223333.dkr.ecr.us-east-1.amazonaws.com/" +
        repositoryName +
        "@" +
        manifestDigest;
      observations[process].taskDefinition.containerDefinitions[0].image =
        approved.tasks[process].image;
      images[process] = {
        body,
        configDigest,
        envelope: {
          images: [
            {
              registryId: "111122223333",
              repositoryName,
              imageId: { imageDigest: manifestDigest },
              imageManifest: original,
              imageManifestMediaType: manifest.mediaType,
            },
          ],
          failures: [],
        },
      };
    }
    variants[mode] = { contract: approved, tasks: observations, images };
  }
  return { variants, revision };
}
