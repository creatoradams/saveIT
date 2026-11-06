// Public CA bytes only. Private workload keys never enter a build context.
import { createRequire } from "node:module";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
const { readBundle } = createRequire(import.meta.url)(
  "../apps/web/public-trust.cjs",
);
try {
  const [path, output, ...extra] = process.argv.slice(2);
  if (!path || !output || extra.length) throw new Error();
  const bundle = readBundle(path);
  const directory = resolve(output);
  mkdirSync(dirname(directory), { recursive: true });
  mkdirSync(directory);
  writeFileSync(resolve(directory, "client-ca.crt"), bundle.bytes, {
    mode: 0o644,
    flag: "wx",
  });
  const manifest = { schemaVersion: 1, clientCaSha256: bundle.sha256 };
  writeFileSync(
    resolve(directory, "manifest.json"),
    JSON.stringify(manifest) + "\n",
    { mode: 0o644, flag: "wx" },
  );
  console.log(JSON.stringify(manifest));
} catch {
  console.error("Public client CA release preparation failed.");
  process.exitCode = 1;
}
