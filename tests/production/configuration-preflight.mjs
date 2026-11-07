// Synthetic payloads only. Supply private fixture bytes over stdin, never argv,
// environment, logs, Terraform, an output payload file or image build input.
import { readFile } from "node:fs/promises";
import { runProtectedCommand } from "./protected-command.mjs";

const failure = "SaveIT protected configuration preflight failed.";
const success =
  "SaveIT protected configuration preflight passed (local validation only).";
const fixture = new URL("../../.local/container-fixture/", import.meta.url);
const session = (await readFile(new URL("protection.pfx", fixture))).toString(
  "base64",
);
const transport = (await readFile(new URL("api.pfx", fixture))).toString(
  "base64",
);
const connection = (user) =>
  `Host=db;Database=saveit;Username=${user};Password=synthetic-preflight-only;SSL Mode=VerifyFull`;
const payload = (workload, retired = 0) => {
  if (workload !== "api")
    return {
      [{
        worker: "ConnectionStrings__SearchWorker",
        migration: "SAVEIT_DEPLOYMENT_CONNECTION",
        access: "SAVEIT_ACCESS_CONNECTION",
      }[workload]]: connection(
        {
          worker: "saveit_search_worker",
          migration: "synthetic_deployer",
          access: "synthetic_access",
        }[workload],
      ),
    };
  const value = {
    ConnectionStrings__App: connection("saveit_app"),
    Identity__Okta__ClientSecret: "synthetic-only",
    Identity__KeyCertificateBase64: session,
    Identity__KeyCertificatePassword: "synthetic-container-only",
    Deployment__ApiTls__CertificateBase64: transport,
    Deployment__ApiTls__CertificatePassword: "synthetic-transport-only",
  };
  for (let i = 0; i < retired; i++) {
    value[`Identity__PreviousKeyCertificates__${i}__Base64`] = session;
    value[`Identity__PreviousKeyCertificates__${i}__Password`] =
      "synthetic-container-only";
  }
  return value;
};

async function run(
  workload,
  retired,
  bytes,
  expected,
  label,
  holdOpen = false,
) {
  await runProtectedCommand(
    ["check-configuration", workload, String(retired)],
    bytes,
    expected,
    label,
    success,
    failure,
    holdOpen,
  );
  console.log(`Packaged configuration preflight passed: ${label}`);
}

try {
  for (const workload of ["api", "worker", "migration", "access"])
    await run(
      workload,
      0,
      JSON.stringify(payload(workload)),
      true,
      `${workload} exact fields`,
    );
  await run(
    "api",
    4,
    JSON.stringify(payload("api", 4)),
    true,
    "four retired protectors",
  );
  const valid = JSON.stringify(payload("api"));
  await run(
    "api",
    0,
    valid + " ".repeat(65536 - Buffer.byteLength(valid)),
    true,
    "exact 64 KiB original bytes",
  );
  await run(
    "api",
    0,
    valid + " ".repeat(65537 - Buffer.byteLength(valid)),
    false,
    "aggregate carrier overflow",
  );
  await run(
    "api",
    0,
    valid.slice(0, -1) +
      ',"Identity__Okta__ClientSecret":"synthetic-duplicate"}',
    false,
    "duplicate selector",
  );
  await run(
    "api",
    0,
    JSON.stringify({
      ...payload("api"),
      Deployment__ApiTls__CertificatePassword: "synthetic-wrong",
    }),
    false,
    "wrong PFX password",
  );
  await run(
    "api",
    0,
    JSON.stringify({
      ...payload("api"),
      Deployment__ApiTls__CertificateBase64: session,
      Deployment__ApiTls__CertificatePassword: "synthetic-container-only",
    }),
    false,
    "transport and session key reuse",
  );
  await run("api", 1, valid, false, "pinned retired-count mismatch");
  await run("web", 0, "{}", false, "web has no secret contract");
  await run("worker", 0, "", false, "empty stdin deadline", true);
  await run(
    "worker",
    0,
    JSON.stringify(payload("worker")),
    false,
    "valid JSON still requires bounded EOF",
    true,
  );
} catch {
  console.error("Synthetic protected configuration verification failed.");
  process.exitCode = 1;
}
