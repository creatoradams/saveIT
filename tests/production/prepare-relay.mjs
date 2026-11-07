// Synthetic ignored fixture input only. Real payload population is unfinished.
import { readFile, writeFile } from "node:fs/promises";
import { X509Certificate } from "node:crypto";
const fixture = new URL("../../.local/container-fixture/", import.meta.url);
const certificate = await readFile(new URL("client-valid.pfx", fixture));
const server = await readFile(new URL("relay.pfx", fixture));
const fingerprint = async (name) =>
  new X509Certificate(await readFile(new URL(name, fixture))).fingerprint256
    .replaceAll(":", "")
    .toLowerCase();
const manifest = JSON.parse(
  await readFile(
    new URL("../../.local/public-trust/manifest.json", import.meta.url),
    "utf8",
  ),
);
await writeFile(
  new URL("relay-input.json", fixture),
  JSON.stringify({
    CertificateBase64: certificate.toString("base64"),
    CertificatePassword: "synthetic-peer-only",
    ClientLeafSha256: await fingerprint("client-valid.crt"),
    ApiCaSha256: manifest.apiCaSha256,
    ServerCertificateBase64: server.toString("base64"),
    ServerCertificatePassword: "synthetic-relay-only",
    ServerLeafSha256: await fingerprint("relay.crt"),
  }),
  { mode: 0o600, flag: "wx" },
);
certificate.fill(0);
server.fill(0);
