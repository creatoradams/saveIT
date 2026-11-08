// Linux packaged CLI only. No live AWS traffic: the literal credential IP and
// regional service hostname terminate on this isolated internal fixture network.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { copyFile, mkdtemp, chmod, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const owner = `saveit-secret-loader-${process.pid}`;
const label = `com.saveit.secret-loader-owner=${owner}`;
const network = `${owner}-network`;
const fixture = `${owner}-fixture`;
const containers = new Set();
let directory;
const source = resolve(".local/container-fixture");
const relative = "/v2/credentials/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
const arn =
  "arn:aws:secretsmanager:us-east-1:123456789012:secret:saveit/demo/staging/relay/config-Ab1234";
const version = "11111111-2222-4333-8444-555555555555";
const started = "SaveIT API relay started.";
const failed = "SaveIT API relay failed.";
let sequence = 0;
let networkAttempted = false;
let stage = "fixture setup";

async function within(promise, milliseconds) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(
          () => reject(new Error("Fixture deadline exceeded.")),
          milliseconds,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}
async function docker(args, timeout = 15000) {
  // Never surface child stderr/arguments, which may contain diagnostics from a
  // rejected payload. Caller assertions print only bounded content-free labels.
  const child = spawn("docker", args, { stdio: ["ignore", "pipe", "pipe"] });
  const output = [];
  const errors = [];
  let count = 0;
  let exceeded = false;
  const collect = (target) => (bytes) => {
    count += bytes.length;
    if (count > 65536) {
      exceeded = true;
      child.kill("SIGKILL");
    } else target.push(bytes);
  };
  child.stdout.on("data", collect(output));
  child.stderr.on("data", collect(errors));
  const completion = new Promise((resolve) => {
    child.once("error", () => resolve(-1));
    child.once("close", resolve);
  });
  try {
    const code = await within(completion, timeout);
    if (code !== 0 || exceeded) throw new Error();
    return Buffer.concat(args[0] === "logs" ? [...output, ...errors] : output)
      .toString("utf8")
      .trim();
  } catch {
    child.kill("SIGKILL");
    try {
      await within(completion, 5000);
    } catch {
      child.stdout.destroy();
      child.stderr.destroy();
      child.unref();
    }
    throw new Error("Packaged loader fixture operation failed.");
  }
}
async function owned(name, timeout = 15000) {
  const value = JSON.parse(await docker(["inspect", name], timeout));
  assert.equal(value[0].Config.Labels["com.saveit.secret-loader-owner"], owner);
  return value[0];
}
async function remove(name) {
  if (!containers.has(name)) return;
  if (
    (await docker([
      "container",
      "ls",
      "--all",
      "--filter",
      `name=^/${name}$`,
      "--format",
      "{{.Names}}",
    ])) === ""
  ) {
    containers.delete(name);
    return;
  }
  await owned(name);
  await docker(["rm", "--force", "--volumes", name]);
  containers.delete(name);
}
async function create(name, args) {
  // Track before the request: a failed acknowledgment can still create it.
  containers.add(name);
  await docker(["create", "--name", name, "--label", label, ...args]);
  await docker(["start", name]);
}
async function driver(code) {
  const name = `${owner}-driver-${++sequence}`;
  await create(name, [
    "--read-only",
    "--network",
    network,
    "saveit-web:test",
    "-e",
    code,
  ]);
  try {
    assert.equal(await docker(["wait", name], 10000), "0");
    return await docker(["logs", name]);
  } finally {
    await remove(name);
  }
}
async function control(mode) {
  await driver(
    `const h=require('node:http');const r=h.request('http://169.254.170.2/control/${mode}',{method:'POST',timeout:3000},s=>{s.resume();s.on('end',()=>{if(s.statusCode!==204)process.exitCode=1;});});r.on('timeout',()=>r.destroy());r.on('error',()=>process.exitCode=1);r.end();`,
  );
}
async function status() {
  const counts = JSON.parse(
    await driver(
      "const h=require('node:http');const r=h.get('http://169.254.170.2/status',{timeout:3000},s=>{let b='';s.on('data',c=>{b+=c;if(b.length>1024)r.destroy();});s.on('end',()=>{if(s.statusCode!==200)process.exitCode=1;else process.stdout.write(b);});});r.on('timeout',()=>r.destroy());r.on('error',()=>process.exitCode=1);",
    ),
  );
  assert.deepEqual(
    Object.keys(counts).sort(),
    ["controls", "metadata", "secrets", "udpPackets", "unexpected"].sort(),
  );
  assert.ok(
    Object.values(counts).every(
      (value) => Number.isSafeInteger(value) && value >= 0 && value <= 10000,
    ),
  );
  return counts;
}
async function waitFor(name, predicate) {
  const end = Date.now() + 16000;
  while (Date.now() < end) {
    const remaining = () => Math.max(1, end - Date.now());
    const state = await owned(name, remaining());
    const logs = await docker(["logs", name], remaining());
    if (predicate(state, logs)) return;
    await delay(100);
  }
  throw new Error("Packaged loader startup exceeded its deadline.");
}
async function run(mode, expected, options = {}) {
  stage = options.label ?? mode;
  await control(mode);
  await driver(
    "const d=require('node:dgram').createSocket('udp4');const t=setTimeout(()=>{process.exitCode=1;d.close();},3000);d.on('message',b=>{if(b.toString()!=='synthetic-udp-ack')process.exitCode=1;clearTimeout(t);d.close();});d.on('error',()=>{process.exitCode=1;clearTimeout(t);d.close();});d.send('synthetic-udp-control',31000,'169.254.170.2');",
  );
  const name = `${owner}-relay-${++sequence}`;
  const root = options.untrusted ? "client-ca.crt" : "ca.crt";
  const environment = options.missing
    ? []
    : ["-e", `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI=${relative}`];
  if (options.poison)
    environment.push("-e", "AWS_ACCESS_KEY_ID=synthetic-forbidden-alternative");
  if (!options.profileOnly)
    environment.push(
      "-e",
      "AWS_CSM_ENABLED=true",
      "-e",
      "AWS_CSM_HOST=169.254.170.2",
      "-e",
      "AWS_CSM_PORT=31000",
    );
  await create(name, [
    "--read-only",
    "--network",
    network,
    "--network-alias",
    "relay",
    "--mount",
    `type=bind,src=${directory}/${root},dst=/etc/ssl/certs/ca-certificates.crt,readonly`,
    "--mount",
    `type=bind,src=${directory}/config,dst=/home/app/.aws/config,readonly`,
    "--mount",
    `type=bind,src=${directory}/config,dst=/home/app/.aws/credentials,readonly`,
    "-e",
    "HOME=/home/app",
    "-e",
    "HTTP_PROXY=http://169.254.170.2:9",
    "-e",
    "HTTPS_PROXY=http://169.254.170.2:9",
    "-e",
    "ALL_PROXY=http://169.254.170.2:9",
    "-e",
    "NO_PROXY=",
    ...environment,
    "saveit-deployment:test",
    "serve-api-relay-secrets",
    "https://api:8443",
    "https://relay:8443",
    arn,
    version,
  ]);
  try {
    await waitFor(name, (state, logs) => {
      if (expected) {
        assert.equal(state.State.Running, true);
        return logs.split(/\r?\n/).includes(started);
      }
      return !state.State.Running;
    });
    if (expected) {
      // A real TLS connection to the Linux listener. Unsupported routes are
      // rejected locally, so no API/DB route exists on this fixture network.
      await driver(
        "const h=require('node:https'),fs=require('node:fs');const r=h.get('https://relay:8443/unsupported',{ca:fs.readFileSync('/opt/saveit/trust/api-ca.crt'),timeout:3000},s=>{s.resume();s.on('end',()=>{if(s.statusCode!==400)process.exitCode=1;});});r.on('timeout',()=>r.destroy());r.on('error',()=>process.exitCode=1);",
      );
      await docker(["stop", "--time", "5", name]);
    }
    const state = await owned(name);
    assert.equal(state.State.Running, false);
    assert.equal(state.State.ExitCode, expected ? 0 : 1);
    // Read final logs only after process exit. SDK/provider content must never
    // reach diagnostics, including on TLS, parsing and deadline failures.
    const logs = await docker(["logs", name]);
    assert.equal(logs, expected ? started : failed);
    const counts = await status();
    assert.deepEqual(counts, {
      metadata: options.missing || options.poison ? 0 : 1,
      secrets:
        options.missing ||
        options.poison ||
        options.untrusted ||
        mode === "role" ||
        mode === "metadata-stall"
          ? 0
          : 1,
      unexpected: 0,
      udpPackets: 0,
      controls: 1,
    });
    console.log(
      `Packaged relay secret loader passed: ${options.label ?? mode}`,
    );
  } catch {
    // Counter-only evidence; never print SDK errors, payloads or retained logs.
    try {
      console.error(
        `Packaged loader failure counters: ${JSON.stringify(await status())}`,
      );
    } catch {
      console.error("Packaged loader counters unavailable.");
    }
    throw new Error("Packaged loader case failed.");
  } finally {
    await remove(name);
  }
}

try {
  directory = await mkdtemp(resolve(".local/relay-secrets-"));
  for (const file of [
    "relay-input.json",
    "secrets.pfx",
    "ca.crt",
    "client-ca.crt",
  ]) {
    await copyFile(`${source}/${file}`, `${directory}/${file}`);
    await chmod(`${directory}/${file}`, file.endsWith(".crt") ? 0o444 : 0o600);
  }
  await writeFile(
    `${directory}/config`,
    "[default]\ncsm_enabled = true\ncsm_host = 169.254.170.2\ncsm_port = 31000\n",
    { mode: 0o444 },
  );
  networkAttempted = true;
  await docker([
    "network",
    "create",
    "--internal",
    "--subnet",
    "169.254.170.0/24",
    "--label",
    label,
    network,
  ]);
  await create(fixture, [
    "--read-only",
    "--network",
    network,
    "--ip",
    "169.254.170.2",
    "--network-alias",
    "secretsmanager.us-east-1.amazonaws.com",
    "--user",
    `${process.getuid()}:${process.getgid()}`,
    "--sysctl",
    "net.ipv4.ip_unprivileged_port_start=0",
    "--mount",
    `type=bind,src=${directory},dst=/run/fixture,readonly`,
    "saveit-identity-fixture:test",
    "relay-secrets-fixture",
  ]);
  await waitFor(fixture, (state, logs) => {
    assert.equal(state.State.Running, true);
    return logs === "Synthetic relay secrets fixture started.";
  });
  await run("valid", true);
  await run("valid", true, {
    profileOnly: true,
    label: "default-profile-only CSM poison",
  });
  await run("version", false, {
    profileOnly: true,
    label: "default-profile-only CSM poison on failure",
  });
  for (const mode of [
    "role",
    "version",
    "binary",
    "oversize",
    "redirect",
    "metadata-stall",
    "body-stall",
  ])
    await run(mode, false);
  await run("valid", false, {
    untrusted: true,
    label: "native TLS untrusted service root",
  });
  await run("valid", false, {
    missing: true,
    label: "missing task credentials",
  });
  await run("valid", false, {
    poison: true,
    label: "forbidden credential alternative",
  });
} catch {
  console.error(`Packaged relay secret loader checks failed: ${stage}.`);
  process.exitCode = 1;
} finally {
  for (const name of containers) {
    try {
      await remove(name);
    } catch {
      console.error("Owned loader fixture cleanup failed.");
      process.exitCode = 1;
    }
  }
  if (networkAttempted) {
    try {
      if (
        (await docker([
          "network",
          "ls",
          "--filter",
          `name=^${network}$`,
          "--format",
          "{{.Name}}",
        ])) !== ""
      ) {
        const value = JSON.parse(await docker(["network", "inspect", network]));
        assert.equal(value[0].Labels["com.saveit.secret-loader-owner"], owner);
        await docker(["network", "rm", network]);
      }
    } catch {
      console.error("Owned loader network cleanup failed.");
      process.exitCode = 1;
    }
  }
}
