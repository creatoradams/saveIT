// Actual packaged operator process; every identity/endpoint is synthetic and isolated.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import {
  generateKeyPairSync,
  createHash,
  sign,
  constants,
  randomUUID,
} from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { runProtectedCommand } from "./protected-command.mjs";
const compose = [
  "compose",
  "-p",
  "saveit-release",
  "-f",
  "tests/production/compose.yml",
];
const fixture = resolve(".local/container-fixture");
const contract = JSON.parse(
  await readFile(
    "infra/terraform/ecs-tasks/tests/compute-release-contract.json",
    "utf8",
  ),
);
const { privateKey, publicKey } = generateKeyPairSync("rsa", {
  modulusLength: 3072,
});
const spki = publicKey.export({ type: "spki", format: "der" });
const pin = createHash("sha256").update(spki).digest("hex");
await writeFile(fixture + "/compute-acquire-public.der", spki, { mode: 0o644 });
for (const name of ["valid", "expired", "public"]) {
  const carrier = {
    accessKeyId: "ASIA" + "0".repeat(16),
    secretAccessKey: "a".repeat(40),
    sessionToken: "synthetic-temporary-session-token",
    expiration: new Date(
      Date.now() + (name === "expired" ? -60000 : 1800000),
    ).toISOString(),
  };
  await writeFile(
    fixture + `/compute-session-${name}.json`,
    JSON.stringify(carrier),
    { mode: 0o600 },
  );
}
const settings = {
  SAVEIT_COMPUTE_ACCOUNT_ID: "111122223333",
  SAVEIT_COMPUTE_REGION: "us-east-1",
  SAVEIT_COMPUTE_CELL: "pilot",
  SAVEIT_COMPUTE_STAGE: "staging",
  SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE: "/run/compute-public.der",
  SAVEIT_COMPUTE_RELEASE_KEY_SHA256: pin,
  SAVEIT_COMPUTE_AWS_SESSION_FILE: "/run/compute-session.json",
  SSL_CERT_FILE: "/opt/saveit/trust/database-ca.crt",
  AWS_REGION: "us-west-2",
  AWS_DEFAULT_REGION: "us-west-2",
  AWS_DEFAULTS_MODE: "auto",
  AWS_CSM_ENABLED: "true",
  AWS_CSM_HOST: "compute-provider",
  AWS_CSM_PORT: "31000",
  HTTP_PROXY: "http://compute-provider:18080",
  HTTPS_PROXY: "http://compute-provider:18080",
  ALL_PROXY: "http://compute-provider:18080",
  http_proxy: "http://compute-provider:18080",
  https_proxy: "http://compute-provider:18080",
  NO_PROXY: "",
  no_proxy: "",
};
await writeFile(
  fixture + "/compute-acquire.env",
  Object.entries(settings)
    .map(([k, v]) => k + "=" + v)
    .join("\n") + "\n",
  { mode: 0o600 },
);
async function within(promise, milliseconds) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error()), milliseconds);
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}
async function docker(args, timeout = 15000) {
  const child = spawn("docker", args, {
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });
  const output = [];
  let length = 0,
    exceeded = false;
  const collect = (keep) => (bytes) => {
    length += bytes.length;
    if (length > 65536) {
      exceeded = true;
      child.kill("SIGKILL");
    } else if (keep) output.push(bytes);
  };
  child.stdout.on("data", collect(true));
  child.stderr.on("data", collect(false));
  const completion = new Promise((resolve) => {
    child.once("error", () => resolve(-1));
    child.once("close", resolve);
  });
  try {
    if ((await within(completion, timeout)) !== 0 || exceeded)
      throw new Error();
    return Buffer.concat(output).toString("utf8").trim();
  } catch {
    child.kill("SIGKILL");
    try {
      await within(completion, 5000);
    } catch {
      child.stdout.destroy();
      child.stderr.destroy();
      child.unref();
    }
    throw new Error("Packaged compute fixture command failed.");
  }
}
function token() {
  const now = Math.floor(Date.now() / 1000);
  const body =
    Buffer.from(
      JSON.stringify({
        alg: "PS256",
        typ: "saveit-compute-release+jwt",
        kid: "sha256-" + pin,
      }),
    ).toString("base64url") +
    "." +
    Buffer.from(
      JSON.stringify({
        iss: "saveit-release",
        aud: "saveit-compute:111122223333:us-east-1:pilot:staging",
        iat: now - 1,
        exp: now + 299,
        jti: randomUUID(),
        contract,
      }),
    ).toString("base64url");
  return (
    body +
    "." +
    sign("sha256", Buffer.from(body), {
      key: privateKey,
      padding: constants.RSA_PKCS1_PSS_PADDING,
      saltLength: 32,
    }).toString("base64url")
  );
}
const requestCode = `const h=require('node:http');const p=process.argv[1],body=process.argv[2];const r=h.request({host:'127.0.0.1',port:80,path:p,method:body?'POST':'GET',timeout:2000},s=>{let b='';s.on('data',c=>{b+=c;if(b.length>2048)r.destroy()});s.on('end',()=>{if(s.statusCode!==200)process.exitCode=1;else process.stdout.write(b)})});r.on('timeout',()=>r.destroy());r.on('error',()=>process.exitCode=1);r.end(body);`;
const control = (path, body) =>
  docker([
    ...compose,
    "exec",
    "-T",
    "compute-provider",
    "node",
    "-e",
    requestCode,
    path,
    ...(body ? [body] : []),
  ]);
const counts = async () => JSON.parse(await control("/counts"));
const success =
  "SaveIT acquired task metadata passed (no launch, image/configuration verification or fencing).";
const failure = "SaveIT compute task acquisition failed.";
let stage = "private session fixture setup";
async function check(
  mode,
  expected,
  label,
  expectedCounts,
  carrier = "valid",
  extras = [],
  hold = false,
) {
  stage = label;
  await control("/mode", mode);
  const before = await counts();
  const started = performance.now();
  const options = [
    "--env-file",
    fixture + "/compute-acquire.env",
    "-v",
    fixture + "/compute-acquire-public.der:/run/compute-public.der:ro",
    "-v",
    fixture + `/compute-session-${carrier}.json:/run/compute-session.json:ro`,
    ...extras,
  ];
  await runProtectedCommand(
    ["check-compute-tasks"],
    JSON.stringify({ signedRelease: token() }),
    expected,
    "compute acquisition " + label,
    success,
    failure,
    hold,
    "saveit-release_compute",
    options,
  );
  const elapsed = performance.now() - started;
  let after = await counts();
  if (mode === "stall" || mode === "stall-body") {
    assert.ok(
      elapsed < 9000,
      "client deadline exceeded native assertion window",
    );
    for (
      let i = 0;
      after.deadlineAborts === before.deadlineAborts && i < 20;
      i++
    ) {
      await delay(50);
      after = await counts();
    }
    assert.equal(after.deadlineAborts - before.deadlineAborts, 1);
  }
  assert.equal(after.stallFallbacks, 0);
  assert.equal(after.sts - before.sts, expectedCounts[0]);
  assert.equal(after.ecs - before.ecs, expectedCounts[1]);
  assert.equal(after.proxy, before.proxy);
  assert.equal(after.monitoring, before.monitoring);
  assert.equal(after.unexpected, 0);
}
try {
  // Known synthetic files only. Operator input stays private and owned by default UID1654.
  await docker(
    [
      "run",
      "--rm",
      "--user",
      "0",
      "--name",
      "saveit-compute-prepare",
      "--label",
      "com.saveit.compute-fixture=saveit-release",
      "--entrypoint",
      "/bin/sh",
      "-v",
      fixture + ":/fixture",
      "mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317",
      "-euc",
      "chown 1654:1654 /fixture/compute-session-valid.json /fixture/compute-session-expired.json /fixture/compute-session-public.json; chmod 600 /fixture/compute-session-valid.json /fixture/compute-session-expired.json; chmod 644 /fixture/compute-session-public.json",
    ],
    20000,
  );
  stage = "internal network setup";
  await docker([...compose, "up", "-d", "compute-provider"]);
  assert.equal(
    await docker([
      "network",
      "inspect",
      "saveit-release_compute",
      "--format",
      "{{.Internal}}",
    ]),
    "true",
  );
  const provider = await docker([...compose, "ps", "-q", "compute-provider"]);
  const state = JSON.parse(await docker(["inspect", provider]))[0];
  assert.deepEqual(Object.keys(state.NetworkSettings.Networks), [
    "saveit-release_compute",
  ]);
  assert.equal(state.Config.User, "1654");
  assert.equal(state.HostConfig.ReadonlyRootfs, true);
  for (let i = 0; ; i++) {
    try {
      await counts();
      break;
    } catch {
      if (i === 30) throw new Error();
      await delay(100);
    }
  }
  // Active controls prove proxy/UDP receivers work before checking no SDK emissions.
  await docker([
    ...compose,
    "exec",
    "-T",
    "compute-provider",
    "node",
    "-e",
    "const h=require('node:http'),d=require('node:dgram').createSocket('udp4');h.get('http://127.0.0.1:18080/positive-control',r=>r.resume()).on('error',()=>process.exitCode=1);const c=h.request({host:'127.0.0.1',port:18080,path:'sts.us-east-1.amazonaws.com:443',method:'CONNECT',timeout:2000});c.on('connect',(r,s)=>{if(r.statusCode!==403)process.exitCode=1;s.destroy()});c.on('timeout',()=>c.destroy());c.on('error',()=>process.exitCode=1);c.end();d.send('synthetic-positive-control',31000,'127.0.0.1',e=>{d.close();if(e)process.exitCode=1})",
  ]);
  for (let i = 0; ; i++) {
    const c = await counts();
    if (c.proxy === 2 && c.monitoring === 1) break;
    if (i === 20) throw new Error();
    await delay(50);
  }
  await check(
    "valid",
    true,
    "native TLS/SigV4 under active proxy/CSM/default-mode/region poison",
    [1, 4],
  );
  await check("foreign-role", false, "foreign caller role", [1, 0]);
  await check("unknown-task", false, "original unknown task semantics", [1, 1]);
  await check("wrong-name", false, "verified server hostname", [0, 0]);
  await check("valid", false, "verified server root", [0, 0], "valid", [
    "-v",
    fixture + "/client-ca.crt:/run/wrong-root.crt:ro",
    "-e",
    "SSL_CERT_FILE=/run/wrong-root.crt",
  ]);
  await check("redirect", false, "redirect refusal", [1, 0]);
  await check("stall", false, "response header deadline", [1, 0]);
  await check("stall-body", false, "response body deadline", [1, 0]);
  await check("valid", false, "ambient profile refusal", [0, 0], "valid", [
    "-e",
    "AWS_PROFILE=synthetic-private-marker",
  ]);
  await check("valid", false, "expired temporary session", [0, 0], "expired");
  await check(
    "valid",
    false,
    "public credential file refusal",
    [0, 0],
    "public",
  );
  await check("valid", false, "stdin EOF deadline", [0, 0], "valid", [], true);
  await check("valid", true, "positive control after refusals", [1, 4]);
  console.log(
    "Packaged compute acquisition passed: 13 synthetic checks, isolated native TLS/SDK identity and four original task contracts; no AWS or provider writes.",
  );
} catch {
  console.error("Packaged compute acquisition failed during " + stage + ".");
  process.exitCode = 1;
} finally {
  let cleanupFailed = false,
    provider;
  try {
    provider = await docker([...compose, "ps", "-aq", "compute-provider"]);
  } catch {
    cleanupFailed = true;
  }
  // Independent attempts: a preparation/inspection failure cannot skip provider shutdown.
  for (const name of ["saveit-compute-prepare", provider]) {
    if (!name) continue;
    try {
      let raw;
      try {
        raw = await docker(["inspect", name]);
      } catch {
        if (name === "saveit-compute-prepare") continue;
        throw new Error();
      }
      const state = JSON.parse(raw)[0];
      if (
        state.Config.Labels?.["com.saveit.compute-fixture"] !== "saveit-release"
      )
        throw new Error();
      await docker(["stop", "--time", "2", name], 10000);
      // Preparation uses --rm; stopping it already delegates exact owned removal to Docker.
    } catch {
      cleanupFailed = true;
    }
  }
  if (cleanupFailed) {
    console.error(
      "Packaged compute fixture cleanup failed or ownership was not verified.",
    );
    process.exitCode = 1;
  }
}
