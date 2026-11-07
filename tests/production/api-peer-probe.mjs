// Synthetic private material travels only over redirected stdin to the isolated
// deployment image. Ordinary web receives no key, mount or secret permission.
import { readFile } from "node:fs/promises";
import { X509Certificate } from "node:crypto";
import { runProtectedCommand } from "./protected-command.mjs";
const fixture = new URL("../../.local/container-fixture/", import.meta.url);
const manifest = JSON.parse(
  await readFile(
    new URL("../../.local/public-trust/manifest.json", import.meta.url),
    "utf8",
  ),
);
const payload = async (name = "valid") => ({
  CertificateBase64: (
    await readFile(new URL(`client-${name}.pfx`, fixture))
  ).toString("base64"),
  CertificatePassword: "synthetic-peer-only",
  ApiCaSha256: manifest.apiCaSha256,
  ClientLeafSha256: new X509Certificate(
    await readFile(new URL(`client-${name}.crt`, fixture)),
  ).fingerprint256
    .replaceAll(":", "")
    .toLowerCase(),
});
const success = "SaveIT API peer probe passed (selected endpoint only).";
const failure = "SaveIT API peer probe failed.";
const origin = "https://api-client-auth:8443";
async function run(value, expected, label, target = origin, holdOpen = false) {
  await runProtectedCommand(
    ["probe-api-peer", target],
    typeof value === "string" ? value : JSON.stringify(value),
    expected,
    label,
    success,
    failure,
    holdOpen,
    "saveit-release_default",
  );
  console.log(`Packaged API peer probe passed: ${label}`);
}
try {
  const valid = await payload();
  await run(valid, true, "approved private client");
  await run(
    valid,
    false,
    "server-only TLS is insufficient",
    "https://api-server-only:8443",
  );
  await run(await payload("spare"), true, "second approved pin");
  await run(await payload("unapproved"), false, "same CA unapproved peer");
  await run(await payload("foreign"), false, "pinned foreign issuer");
  await run(await payload("expired"), false, "expired private client");
  await run(await payload("server"), false, "server-only private certificate");
  await run(
    { ...valid, CertificatePassword: "synthetic-wrong" },
    false,
    "wrong protected password",
  );
  await run(
    { ...valid, ClientLeafSha256: "0".repeat(64) },
    false,
    "wrong expected leaf",
  );
  await run(
    { ...valid, ApiCaSha256: "0".repeat(64) },
    false,
    "wrong public server trust hash",
  );
  await run(
    valid,
    false,
    "server DNS identity mismatch",
    "https://api-wrong:8443",
  );
  await run(
    valid,
    false,
    "caller-selected path refused",
    origin + "/api/v1/organizations",
  );
  await run(
    { ...valid, CertificatePath: "/untrusted-marker" },
    false,
    "extra path source refused",
  );
  await run(
    JSON.stringify(valid) + " ".repeat(65537),
    false,
    "aggregate carrier overflow",
  );
  await run("", false, "empty input deadline", origin, true);
  await run(
    JSON.stringify(valid),
    false,
    "valid input still requires EOF",
    origin,
    true,
  );
  await run(valid, true, "positive control after refusals");
} catch {
  console.error("Packaged API peer probe verification failed.");
  process.exitCode = 1;
}
