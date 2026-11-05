// Public input only. Copy only the exact bytes validated through each open file;
// no private key, PEM payload, credential or certificate subject is printed.
import { createRequire } from "node:module";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
const { readBundle } = createRequire(import.meta.url)(
  "../apps/web/public-trust.cjs",
);
try {
  const [apiPath, databasePath, output, ...extra] = process.argv.slice(2);
  if (!apiPath || !databasePath || !output || extra.length) throw new Error();
  const api = readBundle(apiPath);
  const database = readBundle(databasePath);
  const directory = resolve(output);
  mkdirSync(directory, { recursive: true });
  writeFileSync(resolve(directory, "api-ca.crt"), api.bytes, {
    mode: 0o644,
    flag: "wx",
  });
  writeFileSync(resolve(directory, "database-ca.crt"), database.bytes, {
    mode: 0o644,
    flag: "wx",
  });
  const manifest = {
    schemaVersion: 1,
    apiCaSha256: api.sha256,
    databaseCaSha256: database.sha256,
  };
  writeFileSync(
    resolve(directory, "manifest.json"),
    JSON.stringify(manifest) + "\n",
    { mode: 0o644, flag: "wx" },
  );
  console.log(JSON.stringify(manifest));
} catch {
  console.error("Public CA release preparation failed.");
  process.exitCode = 1;
}
