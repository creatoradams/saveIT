// Real packaged Next -> bound relay -> existing mTLS API, synthetic AWS only.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
const compose = [
  "compose",
  "-p",
  "saveit-release",
  "-f",
  "tests/production/compose.yml",
];
const name = "saveit-release-workload-relay";
let relay;
function command(args, input, timeout = 15000) {
  const child = spawn("docker", args, { stdio: ["pipe", "pipe", "pipe"] });
  let output = "",
    exceeded = false,
    count = 0;
  const failed = Promise.withResolvers();
  const collect = (protocol) => (chunk) => {
    if (exceeded) return;
    count += chunk.length;
    if (protocol) output += chunk;
    if (count > 65536) {
      exceeded = true;
      output = "";
      failed.reject(new Error());
    }
  };
  child.stdout.on("data", collect(true));
  // Compose's bounded setup/progress stderr is not the container stdout protocol.
  child.stderr.on("data", collect(false));
  child.stdin.on("error", () => {});
  const completion = new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", resolve);
  });
  const timer = setTimeout(() => failed.reject(new Error()), timeout);
  async function closedWithin(milliseconds) {
    let drain;
    try {
      return await Promise.race([
        completion.then(
          () => true,
          () => true,
        ),
        new Promise((resolve) => {
          drain = setTimeout(() => resolve(false), milliseconds);
        }),
      ]);
    } finally {
      clearTimeout(drain);
    }
  }
  const done = (async () => {
    try {
      const code = await Promise.race([completion, failed.promise]);
      if (code !== 0 || exceeded) throw new Error();
      return output.trim();
    } catch {
      child.kill("SIGTERM");
      if (!(await closedWithin(1000))) {
        child.kill("SIGKILL");
        if (!(await closedWithin(3000))) {
          child.stdin.destroy();
          child.stdout.destroy();
          child.stderr.destroy();
          child.unref();
        }
      }
      throw new Error("Packaged workload fixture command failed.");
    } finally {
      clearTimeout(timer);
    }
  })();
  child.stdin.end(input);
  return { child, done, output: () => output };
}
const run = (args, input, timeout) => command(args, input, timeout).done;
const execWeb = (code) =>
  run([...compose, "exec", "-T", "web-workload", "node", "-e", code]);
// All fixture driver responses are bounded and diagnostics exclude body/header data.
const driver = `
const a=require('node:assert/strict'),h=require('node:http'),s=require('node:https'),fs=require('node:fs');
async function request(url,headers={},body){return await new Promise((resolve,reject)=>{const req=(url.startsWith('https:')?s:h).request(url,{method:body===undefined?'GET':'POST',headers,ca:fs.readFileSync('/opt/saveit/trust/api-ca.crt'),signal:AbortSignal.timeout(4000)},r=>{let text='';r.on('data',b=>{text+=b;if(text.length>32768)req.destroy();});r.on('end',()=>resolve({status:r.statusCode,headers:r.headers,text}));r.on('error',reject);});req.on('error',reject);req.end(body);});}
`;
try {
  relay = command(
    [
      ...compose,
      "run",
      "--rm",
      "-T",
      "--no-deps",
      "--use-aliases",
      "--name",
      name,
      "relay-workload",
      "serve-api-relay",
      "https://api:8443",
      "https://relay-workload:8443",
    ],
    await readFile(".local/container-fixture/relay-input.json"),
    120000,
  );
  // Observe early failure immediately; retain rejection for the final awaited close.
  relay.done.catch(() => {});
  for (let attempt = 0; ; attempt++) {
    if (relay.output().trim() === "SaveIT API relay started.") break;
    if (attempt === 100 || relay.child.exitCode !== null) throw new Error();
    await delay(100);
  }
  await run(
    [...compose, "up", "-d", "workload-provider", "web-workload"],
    undefined,
    45000,
  );
  for (let attempt = 0; ; attempt++) {
    try {
      await execWeb(
        driver +
          "request('http://127.0.0.1:3000/health/ready').then(r=>a.equal(r.status,200)).catch(()=>process.exitCode=1);",
      );
      break;
    } catch {
      if (attempt === 30) throw new Error();
      await delay(250);
    }
  }
  await execWeb(
    driver +
      `
(async()=>{
  a.equal((await request('http://127.0.0.1:3000/api/v1/csrf',{host:'saveit.localhost:8443'})).status,200);
  const baseline=JSON.parse((await request('http://169.254.170.2/counts')).text);
  a.ok(baseline.metadata>=1);a.equal(baseline.metadata,baseline.issuance);a.equal(baseline.unexpected,0);
  for(const header of [undefined,'browser-forged-workload-token']){
    const headers={host:'saveit.localhost:8443'};if(header)headers['X-SaveIT-Workload']=header;
    const r=await request('http://127.0.0.1:3000/api/v1/csrf',headers);
    a.equal(r.status,200);a.equal(typeof JSON.parse(r.text).token,'string');
    a.equal(r.headers['cache-control'],'no-store');a.equal(r.headers['x-saveit-workload'],undefined);
    a.equal(JSON.stringify(r).includes('browser-forged-workload-token'),false);
    a.equal(JSON.stringify(r.headers).includes('x-middleware-request'),false);
    a.equal(/eyJ[A-Za-z0-9_-]*[.]/.test(JSON.stringify(r)),false);
  }
  for(const token of [undefined,'forged.token.value']){
    const r=await request('https://relay-workload:8443/health/ready',token?{'X-SaveIT-Workload':token}:{});a.equal(r.status,401);
  }
  const r=await request('http://169.254.170.2/counts');const c=JSON.parse(r.text);
  a.deepEqual(c,baseline);
})().catch(()=>process.exitCode=1);
`,
  );
  console.log(
    "Packaged workload intake passed: SDK-signed issuance, native TLS/JWT/mTLS, browser replacement, no response leakage and cache reuse",
  );
  for (const mode of ["long", "foreign"]) {
    await execWeb(
      driver +
        `request('http://169.254.170.2/mode',{},${JSON.stringify(mode)}).then(r=>a.equal(r.status,200)).catch(()=>process.exitCode=1);`,
    );
    // A new process must obtain a fresh token; no test-only cache reset hook.
    await run([...compose, "restart", "web-workload"]);
    for (let attempt = 0; ; attempt++) {
      try {
        await execWeb(
          driver +
            "request('http://127.0.0.1:3000/api/v1/csrf',{host:'saveit.localhost:8443'}).then(r=>a.equal(r.status,401)).catch(()=>process.exitCode=1);",
        );
        break;
      } catch {
        if (attempt === 30) throw new Error();
        await delay(250);
      }
    }
    console.log(
      `Packaged workload intake passed: signed ${mode} token refused`,
    );
  }
} catch {
  throw new Error("Packaged workload intake failed.");
} finally {
  try {
    const inspected = JSON.parse(await run(["inspect", name]));
    assert.equal(
      inspected[0].Config.Labels["com.saveit.relay-fixture"],
      "saveit-release",
    );
    await run(["stop", "--time", "5", name]);
    if (relay) {
      await relay.done;
      assert.equal(relay.output().trim(), "SaveIT API relay started.");
    }
  } finally {
    // Independently attempt both service stops even when relay startup/inspection failed.
    const cleanup = await Promise.allSettled([
      run([...compose, "stop", "web-workload"]),
      run([...compose, "stop", "workload-provider"]),
    ]);
    if (cleanup.some((result) => result.status === "rejected"))
      throw new Error("Packaged workload cleanup failed.");
  }
}
