// Fresh synthetic signing material. Only public JWKS enters the image context.
import { generateKeyPairSync, createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
const directory = new URL("../../.local/workload-trust/", import.meta.url);
await mkdir(directory); // Refuse replacement of existing build trust.
const { privateKey, publicKey } = generateKeyPairSync("rsa", {
  modulusLength: 2048,
});
const jwks = Buffer.from(
  JSON.stringify({
    keys: [
      {
        ...publicKey.export({ format: "jwk" }),
        kid: "fixture",
        use: "sig",
        alg: "RS512",
      },
    ],
  }),
);
await writeFile(new URL("workload-jwks.json", directory), jwks, {
  flag: "wx",
  mode: 0o444,
});
await writeFile(
  new URL("../../.local/container-fixture/workload.key", import.meta.url),
  privateKey.export({ format: "pem", type: "pkcs8" }),
  { flag: "wx", mode: 0o600 },
);
await writeFile(
  new URL("../../.local/container-fixture/workload-sha256", import.meta.url),
  createHash("sha256").update(jwks).digest("hex"),
  { flag: "wx", mode: 0o444 },
);
