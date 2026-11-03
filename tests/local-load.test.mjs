import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { createServer as createTcpServer } from "node:net";
import { execFile } from "node:child_process";
import { configuration, distribution, probe } from "../scripts/local-load.mjs";

test("load CLI requires opt-in, local origin and bounded canonical counts", () => {
  assert.throws(() => configuration([]));
  for (const base of [
    "https://127.0.0.1:5080",
    "http://localhost:5080",
    "http://example.com",
    "http://127.0.0.1/a",
    "http://u:p@127.0.0.1",
    "http://127.0.0.1?q=x",
    "http://127.0.0.1#x",
  ])
    assert.throws(() => configuration(["--allow-local", "--base-url", base]));
  for (const count of ["0", "5", "1001", "1e2", "+20", "6.1"])
    assert.throws(() => configuration(["--allow-local", "--requests", count]));
  for (const count of ["0", "17", "2.5", "1e1"])
    assert.throws(() =>
      configuration(["--allow-local", "--concurrency", count]),
    );
  assert.deepEqual(
    configuration(["--allow-local", "--requests", "12", "--concurrency", "3"]),
    { origin: "http://127.0.0.1:5080", requests: 12, concurrency: 3 },
  );
});

test("latency summaries use nearest-rank percentiles and disclose absent groups", () => {
  const samples = [30, 10, 20];
  assert.deepEqual(distribution(samples), {
    count: 3,
    p50: 20,
    p95: 30,
    p99: 30,
    maximum: 30,
  });
  assert.deepEqual(samples, [30, 10, 20]);
  assert.deepEqual(distribution([]), {
    count: 0,
    p50: null,
    p95: null,
    p99: null,
    maximum: null,
  });
});

async function server(t, handle) {
  const app = createServer(handle);
  await new Promise((resolve) => app.listen(0, "127.0.0.1", resolve));
  t.after(() => {
    app.closeAllConnections();
    app.close();
  });
  return `http://127.0.0.1:${app.address().port}`;
}

test("nondevelopment and oversized preflight responses stop before any login", async (t) => {
  for (const large of [false, true]) {
    let calls = 0;
    const origin = await server(t, (req, res) => {
      calls++;
      res.end(
        large
          ? " ".repeat(256 * 1024 + 1)
          : JSON.stringify({ development: false, okta: true }),
      );
    });
    await assert.rejects(probe({ origin, requests: 6, concurrency: 1 }));
    assert.equal(calls, 1);
  }
});

test("redirects are rejected rather than followed with synthetic cookies", async (t) => {
  let redirectedCalls = 0;
  const destination = await server(t, (req, res) => {
    redirectedCalls++;
    res.end("{}");
  });
  const origin = await server(t, (req, res) => {
    res.writeHead(302, { Location: destination });
    res.end();
  });
  await assert.rejects(probe({ origin, requests: 6, concurrency: 1 }));
  assert.equal(redirectedCalls, 0);
});

test("the response deadline also aborts a body that stalls after headers", async (t) => {
  const origin = await server(t, (req, res) => {
    res.writeHead(200);
    res.write("{");
  });
  const started = performance.now();
  await assert.rejects(probe({ origin, requests: 6, concurrency: 1 }));
  const elapsed = performance.now() - started;
  assert.ok(
    elapsed >= 4000 && elapsed < 10000,
    "The five-second deadline must cover response-body reads.",
  );
});

async function fixtureServer(t, leakOrganization = false) {
  let active = 0,
    peak = 0;
  const origin = await server(t, async (req, res) => {
    active++;
    peak = Math.max(peak, active);
    res.on("finish", () => active--);
    const send = (body) => res.end(JSON.stringify(body));
    if (req.url === "/api/v1/auth/options")
      return send({ development: true, okta: false });
    if (req.url === "/api/v1/csrf") {
      res.setHeader("Set-Cookie", "SaveIT.Csrf=synthetic-csrf-cookie");
      return send({ token: "synthetic-csrf-token" });
    }
    if (req.url === "/api/v1/dev/login") {
      let body = "";
      for await (const chunk of req) body += chunk;
      const profile = JSON.parse(body).profile;
      assert.ok(["alice", "bob"].includes(profile));
      assert.match(req.headers.cookie, /synthetic-csrf-cookie/);
      assert.equal(req.headers["x-csrf-token"], "synthetic-csrf-token");
      res.setHeader("Set-Cookie", `SaveIT.Development=${profile}`);
      res.statusCode = 204;
      return res.end();
    }
    const digit = req.headers.cookie?.includes("SaveIT.Development=alice")
      ? "1"
      : "2";
    const tenant = `${digit}0000000-0000-0000-0000-000000000001`,
      org = `${digit}0000000-0000-0000-0000-000000000010`;
    if (req.url === "/api/v1/session") return send({ tenantId: tenant });
    if (req.url.includes("/organizations/") && !req.url.includes(org)) {
      res.statusCode = 404;
      return send({});
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
    send({
      items: [
        {
          id:
            leakOrganization && req.url === "/api/v1/organizations"
              ? `${digit === "1" ? "2" : "1"}0000000-0000-0000-0000-000000000010`
              : org,
          organizationId: org,
          name: "SYNTHETIC_PRIVATE_CONTENT_NEVER_REPORT",
        },
      ],
      nextCursor: null,
    });
  });
  return { origin, peak: () => peak };
}

test("real HTTP canary exercises isolated jars, workload concurrency and content-free summaries", async (t) => {
  const fixture = await fixtureServer(t);
  const report = await probe({
    origin: fixture.origin,
    requests: 12,
    concurrency: 3,
  });
  assert.equal(report.completed, 12);
  assert.equal(report.failures, 0);
  assert.equal(report.incomplete, false);
  assert.equal(report.setup.httpRequests, 15);
  assert.deepEqual(report.statuses, { 200: 12 });
  assert.equal(fixture.peak(), 3);
  assert.equal(report.groups.length, 6);
  assert.ok(
    report.groups.every(
      (group) => group.latencyMs.count === 2 && group.failures === 0,
    ),
  );
  assert.ok(
    report.groups.every(
      (group) =>
        group.returnedItems.measuredResponses === 2 &&
        group.returnedItems.total === 2 &&
        group.returnedItems.minimum === 1 &&
        group.returnedItems.maximum === 1,
    ),
  );
  const encoded = JSON.stringify(report);
  for (const forbidden of [
    "SYNTHETIC_PRIVATE_CONTENT",
    "synthetic-csrf",
    "alice",
    "bob",
    "SaveIT.Development",
    "0000000-0000",
  ])
    assert.ok(!encoded.includes(forbidden), forbidden);
});

test("known foreign organization in a list stops the warm-up", async (t) => {
  const fixture = await fixtureServer(t, true);
  await assert.rejects(
    probe({ origin: fixture.origin, requests: 6, concurrency: 1 }),
    /Scoped workload warm-up failed/,
  );
});

test("ambient Node proxy mode cannot route the direct loopback probe through a proxy", async (t) => {
  let proxyConnections = 0;
  const proxy = createTcpServer((socket) => {
    proxyConnections++;
    socket.destroy();
  });
  await new Promise((resolve) => proxy.listen(0, "127.0.0.1", resolve));
  t.after(() => proxy.close());
  const address = `http://127.0.0.1:${proxy.address().port}`;
  const fixture = await fixtureServer(t);
  const environment = {
    ...process.env,
    NODE_USE_ENV_PROXY: "1",
    HTTP_PROXY: address,
    http_proxy: address,
    HTTPS_PROXY: address,
    https_proxy: address,
    NO_PROXY: "",
    no_proxy: "",
  };
  // Positive control: ambient proxy mode really is active for Node's global
  // fetch before proving the probe's private agent bypasses it.
  await new Promise((resolve, reject) =>
    execFile(
      process.execPath,
      [
        "--input-type=module",
        "-e",
        "try { await fetch(process.argv[1], { signal: AbortSignal.timeout(2000) }); } catch {}",
        fixture.origin,
      ],
      { env: environment, timeout: 10_000, windowsHide: true },
      (error) => (error ? reject(error) : resolve()),
    ),
  );
  assert.ok(proxyConnections > 0);
  proxyConnections = 0;
  const stdout = await new Promise((resolve, reject) =>
    execFile(
      process.execPath,
      [
        "scripts/local-load.mjs",
        "--allow-local",
        "--base-url",
        fixture.origin,
        "--requests",
        "6",
        "--concurrency",
        "2",
      ],
      {
        timeout: 15_000,
        maxBuffer: 128 * 1024,
        env: environment,
        windowsHide: true,
      },
      (error, output) => (error ? reject(error) : resolve(output)),
    ),
  );
  const summary = JSON.parse(stdout);
  assert.equal(summary.completed, 6);
  assert.equal(summary.failures, 0);
  assert.equal(proxyConnections, 0);
});
