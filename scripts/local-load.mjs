import { parseArgs } from "node:util";
import { mkdirSync, writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { cpus, totalmem } from "node:os";
import { pathToFileURL } from "node:url";
import { Agent, request as httpRequest } from "node:http";

const fixtures = [
  {
    profile: "alice",
    label: "tenant-a",
    tenant: "10000000-0000-0000-0000-000000000001",
    org: "10000000-0000-0000-0000-000000000010",
  },
  {
    profile: "bob",
    label: "tenant-b",
    tenant: "20000000-0000-0000-0000-000000000001",
    org: "20000000-0000-0000-0000-000000000010",
  },
];
const kinds = ["organizations", "assets", "search"];

export function configuration(args) {
  const { values } = parseArgs({
    args,
    options: {
      "allow-local": { type: "boolean", default: false },
      "base-url": { type: "string", default: "http://127.0.0.1:5080" },
      requests: { type: "string", default: "240" },
      concurrency: { type: "string", default: "8" },
    },
  });
  const base = new URL(values["base-url"]);
  if (
    !values["allow-local"] ||
    base.protocol !== "http:" ||
    base.hostname !== "127.0.0.1" ||
    base.username ||
    base.password ||
    base.pathname !== "/" ||
    base.search ||
    base.hash
  )
    throw new Error(
      "Explicit opt-in and a loopback HTTP API origin are required.",
    );
  const requests = Number(values.requests),
    concurrency = Number(values.concurrency);
  if (
    !/^[1-9][0-9]*$/.test(values.requests) ||
    !Number.isInteger(requests) ||
    requests < 6 ||
    requests > 1000 ||
    !/^[1-9][0-9]*$/.test(values.concurrency) ||
    !Number.isInteger(concurrency) ||
    concurrency < 1 ||
    concurrency > 16 ||
    concurrency > requests
  )
    throw new Error("Use 6-1000 requests and 1-16 concurrent clients.");
  return { origin: base.origin, requests, concurrency };
}

export function distribution(samples) {
  if (!samples.length)
    return { count: 0, p50: null, p95: null, p99: null, maximum: null };
  const sorted = [...samples].sort((a, b) => a - b);
  const at = (p) =>
    Math.round(sorted[Math.ceil(sorted.length * p) - 1] * 100) / 100;
  return {
    count: sorted.length,
    p50: at(0.5),
    p95: at(0.95),
    p99: at(0.99),
    maximum: at(1),
  };
}

async function boundedJson(response) {
  const chunks = [];
  let size = 0;
  try {
    if (
      response.headers["content-encoding"] &&
      response.headers["content-encoding"] !== "identity"
    )
      throw new Error("Encoded responses are unsupported by the local probe.");
    for await (const chunk of response) {
      size += chunk.length;
      if (size > 256 * 1024)
        throw new Error("Response exceeds load-probe bound.");
      chunks.push(chunk);
    }
    return size
      ? JSON.parse(Buffer.concat(chunks, size).toString("utf8"))
      : null;
  } finally {
    if (!response.complete) response.destroy();
  }
}

function pageIsScoped(body, kind, fixture) {
  return (
    Array.isArray(body?.items) &&
    body.items.length <= (kind === "search" ? 25 : 50) &&
    body.items.every((item) =>
      kind === "organizations"
        ? typeof item.id === "string" &&
          !fixtures.some((other) => other !== fixture && item.id === other.org)
        : item.organizationId === fixture.org,
    )
  );
}

export async function probe(config) {
  config = configuration([
    "--allow-local",
    "--base-url",
    config.origin,
    "--requests",
    String(config.requests),
    "--concurrency",
    String(config.concurrency),
  ]);
  const port = new URL(config.origin).port || 80;
  const startedAt = new Date().toISOString();
  const deadline = AbortSignal.timeout(120_000);
  // A scoped agent with an empty proxy environment always uses direct HTTP,
  // even when Node's global agent/dispatcher inherits ambient proxy settings.
  const agent = new Agent({
    keepAlive: true,
    maxSockets: 16,
    maxTotalSockets: 16,
    maxFreeSockets: 16,
    proxyEnv: {},
  });
  let httpRequests = 0;
  async function request(path, jar = new Map(), init = {}) {
    httpRequests++;
    const response = await new Promise((resolve, reject) => {
      const pending = httpRequest(
        {
          hostname: "127.0.0.1",
          port,
          path,
          method: init.method ?? "GET",
          agent,
          signal: AbortSignal.any([deadline, AbortSignal.timeout(5_000)]),
          headers: {
            ...init.headers,
            "Accept-Encoding": "identity",
            Cookie: [...jar].map(([k, v]) => `${k}=${v}`).join("; "),
          },
        },
        resolve,
      );
      pending.on("error", reject);
      pending.end(init.body);
    });
    // node:http never follows redirects. Reject them before processing cookies.
    if (response.statusCode >= 300 && response.statusCode < 400) {
      response.destroy();
      throw new Error("Redirects are unsupported by the local probe.");
    }
    for (const cookie of response.headers["set-cookie"] ?? []) {
      const pair = cookie.split(";", 1)[0],
        split = pair.indexOf("=");
      const name = pair.slice(0, split);
      if (split > 0 && ["SaveIT.Csrf", "SaveIT.Development"].includes(name))
        jar.set(name, pair.slice(split + 1));
    }
    return { status: response.statusCode, body: await boundedJson(response) };
  }
  try {
    const options = await request("/api/v1/auth/options");
    if (
      options.status !== 200 ||
      options.body?.development !== true ||
      options.body?.okta !== false
    )
      throw new Error(
        "Only the explicitly enabled synthetic development adapter is supported.",
      );
    const clients = [],
      firstPages = [];
    for (const fixture of fixtures) {
      const jar = new Map();
      const csrf = await request("/api/v1/csrf", jar);
      if (
        csrf.status !== 200 ||
        typeof csrf.body?.token !== "string" ||
        csrf.body.token.length > 4096
      )
        throw new Error("Synthetic session setup failed.");
      const login = await request("/api/v1/dev/login", jar, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": csrf.body.token,
        },
        body: JSON.stringify({ profile: fixture.profile }),
      });
      const session = await request("/api/v1/session", jar);
      if (
        login.status !== 204 ||
        session.status !== 200 ||
        session.body?.tenantId !== fixture.tenant
      )
        throw new Error("Synthetic tenant preflight failed.");
      const foreign = fixtures.find((other) => other !== fixture);
      if (
        (await request(`/api/v1/organizations/${foreign.org}/assets`, jar))
          .status !== 404
      )
        throw new Error("Cross-tenant denial preflight failed.");
      const paths = [
        "/api/v1/organizations",
        `/api/v1/organizations/${fixture.org}/assets`,
        `/api/v1/search?q=switch&organizationId=${fixture.org}`,
      ];
      for (let index = 0; index < paths.length; index++) {
        const warm = await request(paths[index], jar);
        if (
          warm.status !== 200 ||
          !pageIsScoped(warm.body, kinds[index], fixture)
        )
          throw new Error("Scoped workload warm-up failed.");
        firstPages.push({
          tenant: fixture.label,
          workload: kinds[index],
          items: warm.body.items.length,
        });
      }
      clients.push({ fixture, jar, paths });
    }
    const setupRequests = httpRequests,
      measurements = [],
      measurementStartedAt = new Date().toISOString(),
      started = performance.now();
    let next = 0;
    await Promise.all(
      Array.from({ length: config.concurrency }, async () => {
        while (next < config.requests && !deadline.aborted) {
          const index = next++,
            client = clients[index % 2],
            kindIndex = Math.floor(index / 2) % 3;
          const start = performance.now();
          let status = "request-error",
            valid = false,
            items = null;
          try {
            const result = await request(client.paths[kindIndex], client.jar);
            status = String(result.status);
            valid =
              result.status === 200 &&
              pageIsScoped(result.body, kinds[kindIndex], client.fixture);
            if (valid) items = result.body.items.length;
          } catch {
            /* Never retain/print exception messages, cookies or response content. */
          }
          measurements.push({
            tenant: client.fixture.label,
            kind: kinds[kindIndex],
            status,
            valid,
            items,
            ms: performance.now() - start,
          });
        }
      }),
    );
    const elapsedMs = performance.now() - started;
    const statuses = {};
    for (const row of measurements)
      statuses[row.status] = (statuses[row.status] ?? 0) + 1;
    const failures = measurements.filter((row) => !row.valid).length;
    return {
      schemaVersion: 1,
      environment: "synthetic-loopback-development",
      startedAt,
      measurementStartedAt,
      completedAt: new Date().toISOString(),
      client: {
        node: process.version,
        platform: process.platform,
        architecture: process.arch,
        logicalCpus: cpus().length,
        memoryMiB: Math.round(totalmem() / 1024 / 1024),
      },
      configuration: config,
      model:
        "Closed loop in-flight requests shared across two cookie sessions; no arrival-rate control.",
      setup: {
        httpRequests: setupRequests,
        tenants: 2,
        crossTenantDenials: 2,
        warmups: 6,
        firstPages,
      },
      completed: measurements.length,
      failures,
      incomplete: measurements.length !== config.requests,
      elapsedMs: Math.round(elapsedMs),
      requestsPerSecond:
        Math.round(((measurements.length * 1000) / elapsedMs) * 100) / 100,
      latencyMs: distribution(measurements.map((row) => row.ms)),
      statuses,
      groups: fixtures.flatMap((fixture) =>
        kinds.map((kind) => {
          const rows = measurements.filter(
            (row) => row.tenant === fixture.label && row.kind === kind,
          );
          return {
            tenant: fixture.label,
            workload: kind,
            failures: rows.filter((row) => !row.valid).length,
            latencyMs: distribution(rows.map((row) => row.ms)),
            returnedItems: {
              measuredResponses: rows.filter((row) => row.items !== null)
                .length,
              total: rows.reduce((sum, row) => sum + (row.items ?? 0), 0),
              minimum: rows.some((row) => row.items !== null)
                ? Math.min(
                    ...rows
                      .filter((row) => row.items !== null)
                      .map((row) => row.items),
                  )
                : null,
              maximum: rows.some((row) => row.items !== null)
                ? Math.max(
                    ...rows
                      .filter((row) => row.items !== null)
                      .map((row) => row.items),
                  )
                : null,
            },
          };
        }),
      ),
      limitations:
        "Retained synthetic data with unquantified total size; one warm-up per path and uncontrolled cache/JIT state. Direct API loopback latency includes HTTP/body validation, live authority, PostgreSQL, response registration and required read audits. Durable completion callbacks may finish after body receipt and are not measured. Excludes browser, ingress/TLS, live Okta, network geography, large-dataset characterization, business mutations, imports, noisy neighbours, failover, staging capacity and service SLOs. No content/cookies/tokens are retained.",
    };
  } finally {
    agent.destroy();
  }
}

async function main() {
  const config = configuration(process.argv.slice(2));
  const snapshot = () => ({
    revision: execFileSync("git", ["rev-parse", "HEAD"], {
      encoding: "utf8",
      timeout: 5000,
    }).trim(),
    worktreeModified: !!execFileSync("git", ["status", "--porcelain"], {
      encoding: "utf8",
      timeout: 5000,
    }).trim(),
  });
  const start = snapshot();
  const report = await probe(config);
  report.repository = {
    start,
    end: snapshot(),
    limitation:
      "Repository checkpoints, not proof of the loaded API binary or absence of edits between checkpoints.",
  };
  mkdirSync(".local/load", { recursive: true });
  const path = `.local/load/${Date.now()}-${randomUUID()}.json`;
  writeFileSync(path, JSON.stringify(report, null, 2) + "\n", {
    flag: "wx",
    mode: 0o600,
  });
  console.log(
    JSON.stringify({
      report: path,
      completed: report.completed,
      failures: report.failures,
      incomplete: report.incomplete,
      latencyMs: report.latencyMs,
    }),
  );
  if (report.failures || report.incomplete) process.exitCode = 1;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href)
  main().catch(() => {
    console.error(
      "Local synthetic load probe failed; verify opt-in, bounds, readiness and synthetic tenant access. No response/credential details are logged.",
    );
    process.exitCode = 1;
  });
