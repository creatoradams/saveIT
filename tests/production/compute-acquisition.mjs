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
import { computeImageFixture } from "./compute-image-fixture.mjs";
import { computeConfigurationFixture } from "./compute-configuration-fixture.mjs";
import { computeNetworkFixture } from "./compute-network-fixture.mjs";
const compose = [
  "compose",
  "-p",
  "saveit-release",
  "-f",
  "tests/production/compose.yml",
];
const fixture = resolve(".local/container-fixture");
const endpointFixture = JSON.parse(
  await readFile(
    "infra/terraform/runtime-endpoints/tests/compute-endpoint-observations.json",
    "utf8",
  ),
);
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
let imageFixture;
function token(mode) {
  const now = Math.floor(Date.now() / 1000);
  const family = mode?.split("-")[0];
  const networkBound = [
    "groups",
    "routes",
    "endpoints",
    "network",
    "resolver",
    "rds",
    "efs",
  ].includes(family);
  let approved = contract;
  if (networkBound || family === "configuration" || family === "images") {
    const fallback = networkBound ? "groups-valid" : family + "-valid";
    approved = (imageFixture.variants[mode] ?? imageFixture.variants[fallback])
      .contract;
  }
  if (["endpoints", "network", "resolver"].includes(family)) {
    approved = structuredClone(approved);
    approved.network.bindings.endpointPolicySha256 = Object.fromEntries(
      endpointFixture.endpointPolicySha256,
    );
  }
  if (family === "dns") {
    approved = structuredClone(approved);
    approved.network.privateDns = {
      namespaceArn:
        "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001",
      hostedZoneId: "Z0000000000000001",
    };
  }
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
        contract: approved,
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
const control = (path, body, service = "compute-provider") =>
  docker([
    ...compose,
    "exec",
    "-T",
    service,
    "node",
    "-e",
    requestCode,
    path,
    ...(body ? [body] : []),
  ]);
const counts = async () => {
  const [main, foreign] = await Promise.all([
    control("/counts"),
    control("/counts", undefined, "compute-foreign-provider"),
  ]);
  return { ...JSON.parse(main), foreign: JSON.parse(foreign).requests };
};
const success =
  "SaveIT acquired task metadata passed (no launch, image/configuration verification or fencing).";
const failure = "SaveIT compute task acquisition failed.";
const acquisitionKinds = {
  tasks: { command: "check-compute-tasks", success, failure },
  cell: {
    command: "check-compute-parked-services",
    success:
      "SaveIT acquired parked cell metadata passed (no ownership handoff, launch or fencing).",
    failure: "SaveIT compute cell acquisition failed.",
  },
  parkedTasks: {
    command: "check-compute-parked-tasks",
    success:
      "SaveIT parked task inventory passed (no task termination or fencing verification).",
    failure: "SaveIT parked task inventory failed.",
  },
  dns: {
    command: "check-compute-parked-dns",
    success:
      "SaveIT parked private DNS passed (Profiles and live DNS answers unverified).",
    failure: "SaveIT parked private DNS acquisition failed.",
  },
  rds: {
    command: "check-compute-rds",
    success:
      "SaveIT private RDS metadata passed (database TLS, roles and recovery unverified).",
    failure: "SaveIT private RDS acquisition failed.",
  },
  efs: {
    command: "check-compute-efs",
    success:
      "SaveIT EFS attachment metadata passed (mounts, policy enforcement and recovery unverified).",
    failure: "SaveIT EFS attachment acquisition failed.",
  },
  images: {
    command: "check-compute-images",
    success:
      "SaveIT acquired image configurations passed (no layer verification, promotion, launch or fencing).",
    failure: "SaveIT compute image acquisition failed.",
  },
  configuration: {
    command: "check-compute-configuration",
    success:
      "SaveIT acquired configuration cohort passed (no live database, identity, launch or fencing verification).",
    failure: "SaveIT compute configuration acquisition failed.",
  },
  groups: {
    command: "check-compute-security-groups",
    success:
      "SaveIT acquired security-group graph passed (no route, DNS, attachment, launch or fencing verification).",
    failure: "SaveIT compute security-group acquisition failed.",
  },
  routes: {
    command: "check-compute-routes",
    success:
      "SaveIT acquired private route topology passed (no endpoint policy, attachment, launch or fencing verification).",
    failure: "SaveIT compute route acquisition failed.",
  },
  endpoints: {
    command: "check-compute-endpoints",
    success:
      "SaveIT acquired private endpoints and S3 prefix passed (no effective policy, attachment, launch or fencing verification).",
    failure: "SaveIT compute endpoint acquisition failed.",
  },
  network: {
    command: "check-compute-network",
    success:
      "SaveIT acquired DHCP and endpoint interfaces passed (no Resolver policy, packet, launch or fencing verification).",
    failure: "SaveIT compute network acquisition failed.",
  },
  resolver: {
    command: "check-compute-resolver",
    success:
      "SaveIT acquired direct Resolver baseline passed (DNS filtering, Profiles, live answers and launch unverified).",
    failure: "SaveIT compute Resolver acquisition failed.",
  },
};
let stage = "private session fixture setup",
  checks = 0;
async function check(
  mode,
  expected,
  label,
  expectedCounts,
  carrier = "valid",
  extras = [],
  hold = false,
  kind = "tasks",
) {
  assert.equal(typeof expected, "boolean");
  assert.equal(typeof hold, "boolean");
  assert.ok(Object.hasOwn(acquisitionKinds, kind));
  const selected = acquisitionKinds[kind];
  stage = label;
  await control("/mode", mode);
  const before = await counts();
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
    [selected.command],
    JSON.stringify({ signedRelease: token(mode) }),
    expected,
    "compute acquisition " + label,
    selected.success,
    selected.failure,
    hold,
    "saveit-release_compute",
    options,
  );
  let after = await counts();
  if (
    mode === "stall" ||
    mode === "stall-body" ||
    mode === "images-registry-stall" ||
    mode === "configuration-stall" ||
    mode === "groups-stall" ||
    mode === "routes-stall" ||
    mode === "endpoints-stall"
  ) {
    // The provider times the stalled response itself. The bounded child
    // watchdog already covers Docker startup and preceding successful reads.
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
  assert.equal(after.cluster - before.cluster, expectedCounts[2] ?? 0);
  assert.equal(after.services - before.services, expectedCounts[3] ?? 0);
  assert.equal(after.ecr - before.ecr, expectedCounts[4] ?? 0);
  assert.equal(
    after.authorization - before.authorization,
    expectedCounts[5] ?? 0,
  );
  assert.equal(after.registry - before.registry, expectedCounts[6] ?? 0);
  assert.equal(
    after.configuration - before.configuration,
    expectedCounts[7] ?? 0,
  );
  assert.equal(after.foreign, before.foreign);
  assert.equal(after.groups - before.groups, expectedCounts[8] ?? 0);
  assert.equal(after.routes - before.routes, expectedCounts[9] ?? 0);
  assert.equal(after.endpoints - before.endpoints, expectedCounts[10] ?? 0);
  assert.equal(after.prefix - before.prefix, expectedCounts[11] ?? 0);
  assert.equal(after.dhcp - before.dhcp, expectedCounts[12] ?? 0);
  assert.equal(after.interfaces - before.interfaces, expectedCounts[13] ?? 0);
  assert.equal(after.resolver - before.resolver, expectedCounts[14] ?? 0);
  assert.equal(after.parkedTasks - before.parkedTasks, expectedCounts[15] ?? 0);
  assert.equal(after.discovery - before.discovery, expectedCounts[16] ?? 0);
  assert.equal(after.dns - before.dns, expectedCounts[17] ?? 0);
  assert.equal(after.rds - before.rds, expectedCounts[18] ?? 0);
  assert.equal(after.efs - before.efs, expectedCounts[19] ?? 0);
  assert.equal(after.proxy, before.proxy);
  assert.equal(after.monitoring, before.monitoring);
  assert.equal(after.unexpected, 0);
  checks++;
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
      "mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb",
      "-euc",
      "chown 1654:1654 /fixture/compute-session-valid.json /fixture/compute-session-expired.json /fixture/compute-session-public.json; chmod 600 /fixture/compute-session-valid.json /fixture/compute-session-expired.json; chmod 644 /fixture/compute-session-public.json",
    ],
    20000,
  );
  stage = "actual packaged image inspection";
  const inspected = {};
  for (const p of ["api", "web", "worker", "relay"])
    inspected[p] = JSON.parse(
      await docker([
        "image",
        "inspect",
        `saveit-${p === "relay" ? "deployment" : p}:test`,
      ]),
    )[0];
  imageFixture = computeImageFixture(
    contract,
    JSON.parse(
      await readFile(
        "infra/terraform/ecs-tasks/tests/compute-release-observations.json",
        "utf8",
      ),
    ),
    inspected,
  );
  if (process.env.GITHUB_SHA)
    assert.equal(imageFixture.revision, process.env.GITHUB_SHA);
  const configurations = await computeConfigurationFixture(
    fixture,
    imageFixture,
  );
  const groupXml = computeNetworkFixture(
    imageFixture,
    JSON.parse(
      await readFile(
        "infra/terraform/runtime-endpoints/tests/compute-network-graph.json",
        "utf8",
      ),
    ),
  );
  await writeFile(fixture + "/compute-groups.xml", groupXml, { mode: 0o644 });
  // Only synthetic copies are readable by the isolated UID1654 fixture; no real
  // secret is populated or mounted into any ordinary application service.
  await writeFile(
    fixture + "/compute-private.json",
    JSON.stringify(configurations),
    { mode: 0o644 },
  );
  await writeFile(
    fixture + "/compute-images.json",
    JSON.stringify(imageFixture),
    { mode: 0o644 },
  );
  stage = "internal network setup";
  await docker([
    ...compose,
    "up",
    "-d",
    "compute-provider",
    "compute-foreign-provider",
  ]);
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
  for (const name of ["compute-provider", "compute-foreign-provider"]) {
    const provider = await docker([...compose, "ps", "-q", name]);
    const state = JSON.parse(await docker(["inspect", provider]))[0];
    assert.deepEqual(Object.keys(state.NetworkSettings.Networks), [
      "saveit-release_compute",
    ]);
    assert.equal(state.Config.User, "1654");
    assert.equal(state.HostConfig.ReadonlyRootfs, true);
    assert.equal(
      state.Config.Labels["com.saveit.compute-fixture"],
      "saveit-release",
    );
  }
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
  // A TLS-valid forbidden destination is reachable. Redirect refusal must prevent contact.
  await docker([
    ...compose,
    "exec",
    "-T",
    "compute-provider",
    "node",
    "-e",
    "const h=require('node:https'),f=require('node:fs');const r=h.get({hostname:'foreign-control.invalid',path:'/positive-control',ca:f.readFileSync('/run/compute/ca.crt'),timeout:2000},s=>{if(s.statusCode!==200)process.exitCode=1;s.resume()});r.on('timeout',()=>r.destroy());r.on('error',()=>process.exitCode=1)",
  ]);
  assert.equal((await counts()).foreign, 1);
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
  for (const [mode, passed, label, expected] of [
    ["valid", true, "parked cluster/four services", [1, 4, 1, 1]],
    ["foreign-cluster", false, "foreign parked cluster", [1, 4, 1, 0]],
    ["active-service", false, "active parked service", [1, 4, 1, 1]],
    [
      "unknown-service",
      false,
      "original unknown service semantics",
      [1, 4, 1, 1],
    ],
    ["extra-group", false, "extra service network authority", [1, 4, 1, 1]],
    ["service-load-balancer", false, "unexpected load balancer", [1, 4, 1, 1]],
  ])
    await check(mode, passed, label, expected, "valid", [], false, "cell");
  await check(
    "valid",
    false,
    "parked stdin EOF deadline",
    [0, 0, 0, 0],
    "valid",
    [],
    true,
    "cell",
  );
  await check(
    "valid",
    true,
    "parked positive control after refusals",
    [1, 4, 1, 1],
    "valid",
    [],
    false,
    "cell",
  );
  for (const [mode, passed, label, expected] of [
    [
      "images-valid",
      true,
      "actual packaged image startup configurations",
      [1, 4, 1, 1, 4, 1, 4],
    ],
    [
      "images-manifest-hash",
      false,
      "original image manifest digest",
      [1, 4, 1, 1, 1, 0, 0],
    ],
    [
      "images-index",
      false,
      "signed unsupported platform index",
      [1, 4, 1, 1, 1, 0, 0],
    ],
    ["images-root", false, "signed root image user", [1, 4, 1, 1, 4, 1, 1]],
    [
      "images-entrypoint",
      false,
      "signed inherited image command",
      [1, 4, 1, 1, 4, 1, 1],
    ],
    [
      "images-env",
      false,
      "signed inherited private environment",
      [1, 4, 1, 1, 4, 1, 1],
    ],
    [
      "images-auth-foreign",
      false,
      "registry credential destination refusal",
      [1, 4, 1, 1, 4, 1, 0],
    ],
    [
      "images-config-hash",
      false,
      "original configuration digest",
      [1, 4, 1, 1, 4, 1, 1],
    ],
    [
      "images-registry-redirect",
      false,
      "registry redirect and bearer confinement",
      [1, 4, 1, 1, 4, 1, 1],
    ],
    [
      "images-registry-wrong-name",
      false,
      "verified registry server hostname",
      [1, 4, 1, 1, 4, 1, 0],
    ],
    [
      "images-registry-stall",
      false,
      "registry complete-body deadline",
      [1, 4, 1, 1, 4, 1, 1],
    ],
  ])
    await check(mode, passed, label, expected, "valid", [], false, "images");
  await check(
    "images-valid",
    false,
    "image stdin EOF deadline",
    [0, 0, 0, 0, 0, 0, 0],
    "valid",
    [],
    true,
    "images",
  );
  await check(
    "images-valid",
    true,
    "image positive control after refusals",
    [1, 4, 1, 1, 4, 1, 4],
    "valid",
    [],
    false,
    "images",
  );
  for (const [mode, passed, reads] of [
    ["configuration-valid", true, 3],
    ["configuration-version", false, 1],
    ["configuration-binary", false, 1],
    ["configuration-unknown", false, 1],
    ["configuration-duplicate", false, 1],
    ["configuration-extra", false, 3],
    ["configuration-api-name", false, 3],
    ["configuration-db", false, 3],
    ["configuration-key-reuse", false, 3],
    ["configuration-redirect", false, 1],
    ["configuration-wrong-name", false, 0],
    ["configuration-stall", false, 1],
    ["configuration-valid", true, 3],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, reads],
      "valid",
      [],
      false,
      "configuration",
    );
  await check(
    "configuration-valid",
    false,
    "configuration stdin EOF deadline",
    [0, 0, 0, 0, 0, 0, 0, 0],
    "valid",
    [],
    true,
    "configuration",
  );
  for (const [mode, passed, reads] of [
    ["groups-valid", true, 1],
    ["groups-unknown", false, 1],
    ["groups-duplicate", false, 1],
    ["groups-next-page", false, 1],
    ["groups-foreign-owner", false, 1],
    ["groups-redirect", false, 1],
    ["groups-wrong-name", false, 0],
    ["groups-stall", false, 1],
    ["groups-valid", true, 1],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, reads],
      "valid",
      [],
      false,
      "groups",
    );
  await check(
    "groups-valid",
    false,
    "group stdin EOF deadline",
    [0, 0, 0, 0, 0, 0, 0, 0, 0],
    "valid",
    [],
    true,
    "groups",
  );
  await check(
    "configuration-valid",
    false,
    "legacy approval has no signed network binding",
    [0, 0, 0, 0, 0, 0, 0, 0, 0],
    "valid",
    [],
    false,
    "groups",
  );
  for (const [mode, passed, reads] of [
    ["routes-valid", true, 5],
    ["routes-vpc-ipv6", false, 1],
    ["routes-disabled-dns", false, 2],
    ["routes-subnet-public", false, 4],
    ["routes-main-public", false, 5],
    ["routes-next-page", false, 5],
    ["routes-association", false, 5],
    ["routes-stall", false, 5],
    ["routes-valid", true, 5],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, reads],
      "valid",
      [],
      false,
      "routes",
    );
  await check(
    "routes-valid",
    false,
    "route stdin EOF deadline",
    [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    "valid",
    [],
    true,
    "routes",
  );
  for (const [mode, passed, prefixReads] of [
    ["endpoints-valid", true, 1],
    ["endpoints-policy", false, 0],
    ["endpoints-group", false, 0],
    ["endpoints-next-page", false, 0],
    ["endpoints-prefix-owner", false, 1],
    ["endpoints-stall", false, 1],
    ["endpoints-valid", true, 1],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, 5, 1, prefixReads],
      "valid",
      [],
      false,
      "endpoints",
    );
  for (const [mode, passed, interfaceReads] of [
    ["network-valid", true, 1],
    ["network-custom-dns", false, 0],
    ["network-foreign-interface", false, 1],
    ["network-interface-page", false, 1],
    ["network-valid", true, 1],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, 5, 1, 1, 1, interfaceReads],
      "valid",
      [],
      false,
      "network",
    );
  for (const [mode, passed, resolverReads] of [
    ["resolver-valid", true, 3],
    ["resolver-forward", false, 1],
    ["resolver-group", false, 2],
    ["resolver-page", false, 2],
    ["resolver-owner", false, 3],
    ["resolver-open", false, 3],
    ["resolver-valid", true, 3],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, 5, 1, 1, 1, 1, resolverReads],
      "valid",
      [],
      false,
      "resolver",
    );
  for (const [mode, passed] of [
    ["inventory-valid", true],
    ["inventory-active", false],
    ["inventory-page", false],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1],
      "valid",
      [],
      false,
      "parkedTasks",
    );
  for (const [mode, passed, discovery, dns] of [
    ["dns-valid", true, 5, 2],
    ["dns-foreign", false, 5, 1],
    ["dns-stale", false, 5, 2],
    ["dns-instance", false, 3, 0],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, discovery, dns],
      "valid",
      [],
      false,
      "dns",
    );
  for (const [mode, passed] of [
    ["rds-valid", true],
    ["rds-public", false],
    ["rds-target", false],
    ["rds-subnet", false],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, 5, 0, 0, 0, 0, 0, 0, 0, 0, 1],
      "valid",
      [],
      false,
      "rds",
    );
  for (const [mode, passed, reads] of [
    ["efs-valid", true, 4],
    ["efs-mode", false, 1],
    ["efs-page", false, 2],
    ["efs-group", false, 4],
  ])
    await check(
      mode,
      passed,
      mode,
      [1, 4, 1, 1, 4, 1, 4, 3, 1, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, reads],
      "valid",
      [],
      false,
      "efs",
    );
  console.log(
    `Packaged compute acquisition passed: ${checks} synthetic checks, isolated native TLS/SDK identity and original compute/network metadata; no AWS or provider writes.`,
  );
} catch {
  console.error("Packaged compute acquisition failed during " + stage + ".");
  process.exitCode = 1;
} finally {
  let cleanupFailed = false;
  const providers = [];
  for (const name of ["compute-provider", "compute-foreign-provider"]) {
    try {
      const id = await docker([...compose, "ps", "-aq", name]);
      if (id) providers.push(id);
    } catch {
      cleanupFailed = true;
    }
  }
  // Independent attempts: a preparation/inspection failure cannot skip provider shutdown.
  for (const name of ["saveit-compute-prepare", ...providers]) {
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
