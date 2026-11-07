// Isolated packaged test runner. The production web service mounts only its
// public CA. Private keys below belong solely to these synthetic TLS servers.
import assert from "node:assert/strict";
import http from "node:http";
import https from "node:https";
import { readFileSync } from "node:fs";
import { spawn } from "node:child_process";

function request(path, port = 3001, hostname = "127.0.0.1") {
  return new Promise((resolve, reject) => {
    const req = http.get(
      {
        hostname,
        port,
        path,
        headers: { Host: "saveit.localhost:8443" },
        agent: false,
      },
      (res) => {
        res.resume();
        res.once("end", () => {
          clearTimeout(deadline);
          resolve(res.statusCode);
        });
        res.once("error", (error) => {
          clearTimeout(deadline);
          reject(error);
        });
      },
    );
    // Absolute response/body deadline, independent of socket activity.
    const deadline = setTimeout(
      () => req.destroy(new Error("Fixture deadline exceeded.")),
      2000,
    );
    req.once("error", (error) => {
      clearTimeout(deadline);
      reject(error);
    });
  });
}

async function check(name, environment, ready, api) {
  let diagnostics = "";
  const marker = "synthetic-query-must-stay-private";
  const child = spawn(process.execPath, ["runtime.cjs"], {
    env: {
      ...process.env,
      HOSTNAME: "127.0.0.1",
      PORT: "3001",
      ...environment,
    },
    stdio: ["ignore", "pipe", "pipe"],
  });
  // Bounded capture stays in memory and is never included in assertion output.
  for (const stream of [child.stdout, child.stderr])
    stream.on("data", (data) => {
      diagnostics = (diagnostics + data.toString()).slice(-65536);
    });
  const closed = new Promise((resolve, reject) => {
    child.once("close", resolve);
    child.once("error", reject);
  });
  const logCount = () =>
    diagnostics.split("SaveIT API upstream request failed.").length - 1;
  async function waitForLog(count) {
    const deadline = Date.now() + 3000;
    while (logCount() < count && Date.now() < deadline)
      await new Promise((resolve) => setTimeout(resolve, 25));
    assert.ok(logCount() >= count, "Sanitized diagnostic must be observed.");
  }
  try {
    let started = false;
    const startupDeadline = Date.now() + 15000;
    while (Date.now() < startupDeadline) {
      if (child.exitCode !== null)
        throw new Error("Packaged transport fixture exited early.");
      try {
        await request("/health/ready");
        started = true;
        break;
      } catch {
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    }
    assert.ok(started, "Packaged transport fixture did not start.");
    assert.equal(await request("/health/ready"), ready, `${name}: readiness`);
    assert.equal(
      await request("/api/v1/auth/options"),
      api,
      `${name}: actual rewrite`,
    );
    if (api === 500) {
      await waitForLog(1);
      const beforeCallback = logCount();
      assert.equal(
        await request(`/api/v1/auth/callback?code=${marker}&state=${marker}`),
        500,
      );
      await waitForLog(beforeCallback + 1);
    }
    console.log(`Packaged transport canary passed: ${name}`);
  } finally {
    child.kill("SIGTERM");
    const timeout = setTimeout(() => child.kill("SIGKILL"), 5000);
    let drainTimeout;
    try {
      await Promise.race([
        closed,
        new Promise((_, reject) => {
          drainTimeout = setTimeout(
            () => reject(new Error("Fixture output drain deadline exceeded.")),
            10000,
          );
        }),
      ]);
    } finally {
      clearTimeout(timeout);
      clearTimeout(drainTimeout);
    }
    assert.ok(
      !diagnostics.includes(marker),
      "Drained proxy diagnostics must exclude callback query values.",
    );
  }
}

async function rejectsStartup(name, environment) {
  let diagnostics = "";
  const child = spawn(process.execPath, ["runtime.cjs"], {
    env: { ...process.env, ...environment },
    stdio: ["ignore", "pipe", "pipe"],
  });
  for (const stream of [child.stdout, child.stderr])
    stream.on("data", (data) => {
      diagnostics = (diagnostics + data.toString()).slice(-65536);
    });
  const closed = new Promise((resolve, reject) => {
    child.once("close", resolve);
    child.once("error", reject);
  });
  let deadline;
  try {
    const code = await Promise.race([
      closed,
      new Promise((_, reject) => {
        deadline = setTimeout(
          () => reject(new Error("Startup rejection deadline exceeded.")),
          5000,
        );
      }),
    ]);
    assert.notEqual(code, 0);
    assert.ok(
      diagnostics.includes("Public CA release configuration is invalid."),
    );
    console.log(`Packaged transport canary passed: ${name}`);
  } finally {
    clearTimeout(deadline);
    child.kill("SIGKILL");
    let cleanupDeadline;
    try {
      await Promise.race([
        closed,
        new Promise((_, reject) => {
          cleanupDeadline = setTimeout(
            () => reject(new Error("Startup cleanup deadline exceeded.")),
            5000,
          );
        }),
      ]);
    } finally {
      clearTimeout(cleanupDeadline);
    }
  }
}

let redirectedRequests = 0;
const sink = http.createServer((_, res) => {
  redirectedRequests++;
  res.end();
});
const redirect = https.createServer(
  {
    key: readFileSync("/run/canary/key"),
    cert: readFileSync("/run/canary/cert"),
  },
  (_, res) => {
    res.writeHead(302, { Location: "http://127.0.0.1:9444/must-not-follow" });
    res.end();
  },
);
const expired = https.createServer(
  {
    key: readFileSync("/run/canary/key"),
    cert: readFileSync("/run/canary/expired"),
  },
  (_, res) => {
    res.end();
  },
);
const listen = (server, port) =>
  new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(port, "127.0.0.1", resolve);
  });
try {
  await listen(sink, 9444);
  await listen(redirect, 9443);
  await listen(expired, 9445);
  await check("trusted CA and exact API hostname", {}, 200, 200);
  await check(
    "keyless web cannot bypass relay client admission",
    { SAVEIT_API_ORIGIN: "https://api:8443" },
    503,
    500,
  );
  await check(
    "untrusted CA",
    { NODE_EXTRA_CA_CERTS: "", SAVEIT_API_CA_SHA256: undefined },
    503,
    500,
  );
  await check(
    "wrong API hostname",
    { SAVEIT_API_ORIGIN: "https://api-wrong:8443" },
    503,
    500,
  );
  await check(
    "expired certificate",
    { SAVEIT_API_ORIGIN: "https://localhost:9445" },
    503,
    500,
  );
  await check(
    "plaintext origin prohibited",
    { SAVEIT_API_ORIGIN: "http://api:8080" },
    503,
    503,
  );
  await rejectsStartup("TLS validation bypass prohibited", {
    NODE_TLS_REJECT_UNAUTHORIZED: "0",
  });
  await rejectsStartup("wrong public CA release hash", {
    SAVEIT_API_CA_SHA256: "0".repeat(64),
  });
  await rejectsStartup("alternate extra CA path prohibited", {
    NODE_EXTRA_CA_CERTS: "/must-not-use-a-different-ca",
  });
  await check(
    "readiness and rewrite do not follow redirects",
    { SAVEIT_API_ORIGIN: "https://localhost:9443" },
    503,
    302,
  );
  assert.equal(redirectedRequests, 0);
  for (const port of [8080, 9999]) {
    await assert.rejects(
      request("/api/v1/auth/options", port, "api"),
      (error) => error.code === "ECONNREFUSED",
    );
  }
  console.log("Packaged transport canary passed: no remote plaintext listener");
} finally {
  for (const server of [sink, redirect, expired]) {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  }
}
