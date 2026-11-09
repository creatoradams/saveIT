import assert from "node:assert/strict";
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { createRequire } from "node:module";
import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import ts from "typescript";

// Compile the same server module with existing tooling; remove only Next's
// build-time poison marker, which intentionally refuses standalone Node imports.
const source = readFileSync(
  new URL("../apps/web/lib/workload.server.ts", import.meta.url),
  "utf8",
);
assert.match(source, /import "server-only";/);
const compiled = ts.transpileModule(
  source.replace('import "server-only";', ""),
  {
    compilerOptions: {
      target: ts.ScriptTarget.ES2022,
      module: ts.ModuleKind.CommonJS,
      esModuleInterop: true,
    },
  },
).outputText;
const module = { exports: {} };
new Function("require", "module", "exports", compiled)(
  createRequire(new URL("../apps/web/lib/workload.server.ts", import.meta.url)),
  module,
  module.exports,
);
const { createWorkloadTokenProvider, boundedWorkloadWire } = module.exports;
const failure = "SaveIT workload admission is unavailable.";
const pool = "us-east-1:10000000-0000-0000-0000-000000000001";
const environment = () => ({
  SAVEIT_WORKLOAD_POOL_ID: pool,
  SAVEIT_WORKLOAD_PROVIDER: "saveit.pilot.staging.web",
  SAVEIT_WORKLOAD_ACCOUNT_ID: "111122223333",
  AWS_CONTAINER_CREDENTIALS_RELATIVE_URI:
    "/v2/credentials/20000000-0000-0000-0000-000000000001",
});
const bytes = (value) => Buffer.from(JSON.stringify(value));
const metadata = () => ({
  RoleArn: "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task",
  AccessKeyId: "synthetic-access",
  SecretAccessKey: "synthetic-secret",
  Token: "synthetic-session",
  Expiration: new Date(Date.now() + 600_000).toISOString(),
});
const token = "eyJhbGciOiJSUzUxMiJ9.eyJleHAiOjF9.c3ludGhldGlj";

test(
  "fresh-process SDK defaults ignore environment/profile poison with real unpinned SDK IMDS positive controls",
  { timeout: 15000 },
  async (t) => {
    let contacts = 0;
    const trap = createServer((request, response) => {
      contacts++;
      response.end(
        request.method === "PUT" ? "synthetic-imds-token" : "us-west-2",
      );
    });
    await new Promise((resolve) => trap.listen(0, "127.0.0.1", resolve));
    const directory = mkdtempSync(join(tmpdir(), "saveit-workload-"));
    t.after(() => {
      rmSync(directory, { recursive: true, force: true });
      trap.closeAllConnections();
      return new Promise((resolve) => trap.close(resolve));
    });
    const endpoint = `http://127.0.0.1:${trap.address().port}`;
    const config = join(directory, "config");
    writeFileSync(config, "[default]\ndefaults_mode = auto\n");
    // Removing only the defaultsMode pin must exercise the real SDK fallback.
    // Remove retryMode as well: an explicit retry mode otherwise avoids that
    // fallback, masking the vulnerability even if defaultsMode regresses.
    const unpinned = compiled
      .replace(/\s*defaultsMode: "standard",/, "")
      .replace(/\s*retryMode: "standard",/, "");
    assert.notEqual(unpinned, compiled);
    for (const profileOnly of [false, true])
      for (const positiveControl of [true, false]) {
        const before = contacts;
        const childSource = `
    import assert from 'node:assert/strict';
    import { createRequire } from 'node:module';
    const module = { exports: {} };
    new Function('require','module','exports',${JSON.stringify(positiveControl ? unpinned : compiled)})(createRequire(${JSON.stringify(new URL("../apps/web/lib/workload.server.ts", import.meta.url).href)}),module,module.exports);
    let calls = 0;
    const getToken = module.exports.createWorkloadTokenProvider(${JSON.stringify(environment())},async (_url, method) => {
      calls++;
      return Buffer.from(JSON.stringify(method === 'GET' ? ${JSON.stringify(metadata())} : { IdentityId:'us-east-1:30000000-0000-0000-0000-000000000001',Token:${JSON.stringify(token)}}));
    });
    assert.equal(await getToken(), ${JSON.stringify(token)});
    assert.equal(calls,2);
  `;
        const childEnvironment = {
          ...process.env,
          AWS_CONFIG_FILE: config,
          AWS_EC2_METADATA_SERVICE_ENDPOINT: endpoint,
        };
        for (const name of [
          "AWS_EC2_METADATA_DISABLED",
          "AWS_RETRY_MODE",
          "AWS_DEFAULTS_MODE",
          "AWS_PROFILE",
          "AWS_DEFAULT_PROFILE",
          "AWS_EXECUTION_ENV",
          "AWS_REGION",
          "AWS_DEFAULT_REGION",
        ])
          delete childEnvironment[name];
        if (!profileOnly) childEnvironment.AWS_DEFAULTS_MODE = "auto";
        const child = spawn(process.execPath, ["--input-type=module"], {
          env: childEnvironment,
          stdio: ["pipe", "pipe", "pipe"],
        });
        t.after(() => {
          if (child.exitCode === null) child.kill();
        });
        let output = "";
        for (const stream of [child.stdout, child.stderr])
          stream.on("data", (chunk) => {
            output += chunk.toString();
            if (output.length > 16384) child.kill();
          });
        child.stdin.end(childSource);
        const code = await new Promise((resolve, reject) => {
          child.on("error", reject);
          child.on("close", resolve);
        });
        assert.equal(code, 0, output);
        assert.equal(
          contacts - before,
          positiveControl ? 2 : 0,
          `${profileOnly ? "profile" : "environment"} ${positiveControl ? "unpinned positive control did not contact IMDS" : "pinned SDK escaped bounded wire"}`,
        );
      }
  },
);

test("official SDK signs exact short developer-token request; concurrent calls share one bounded mint", async () => {
  let count = 0;
  const get = createWorkloadTokenProvider(
    environment(),
    async (url, method, headers, body, limit, signal) => {
      count++;
      assert.equal(limit, 32768);
      assert.equal(signal.aborted, false);
      if (method === "GET") {
        assert.equal(
          url.href,
          "http://169.254.170.2/v2/credentials/20000000-0000-0000-0000-000000000001",
        );
        assert.deepEqual(headers, {});
        assert.equal(body, undefined);
        return bytes(metadata());
      }
      assert.equal(
        url.href,
        "https://cognito-identity.us-east-1.amazonaws.com/",
      );
      assert.equal(method, "POST");
      assert.match(
        headers.authorization,
        /^AWS4-HMAC-SHA256 Credential=synthetic-access\//,
      );
      assert.match(
        headers.authorization,
        /\/us-east-1\/cognito-identity\/aws4_request/,
      );
      assert.equal(headers["x-amz-security-token"], "synthetic-session");
      assert.deepEqual(JSON.parse(Buffer.from(body).toString("utf8")), {
        IdentityPoolId: pool,
        Logins: { "saveit.pilot.staging.web": "web" },
        TokenDuration: 60,
      });
      return bytes({
        IdentityId: "us-east-1:30000000-0000-0000-0000-000000000001",
        Token: token,
      });
    },
  );
  assert.deepEqual(
    await Promise.all(Array.from({ length: 16 }, () => get())),
    Array(16).fill(token),
  );
  assert.equal(await get(), token);
  assert.equal(count, 2);
});

test("partial bindings, alternate sources, noncanonical metadata URI and cancelled callers fail before contact", async () => {
  const cases = [
    { SAVEIT_WORKLOAD_POOL_ID: pool },
    { ...environment(), AWS_PROFILE: "synthetic-marker" },
    {
      ...environment(),
      AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://foreign.invalid",
    },
    {
      ...environment(),
      AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/v2/credentials/../foreign",
    },
  ];
  for (const env of cases)
    await assert.rejects(
      createWorkloadTokenProvider(env, () => assert.fail("must not contact"))(),
      { message: failure },
    );
  assert.equal(
    await createWorkloadTokenProvider({}, () =>
      assert.fail("must not contact"),
    )(),
    null,
  );
  await assert.rejects(
    createWorkloadTokenProvider(environment())(AbortSignal.abort()),
    { message: failure },
  );
});

test("wrong roles, invalid/short expiry and oversized credentials never reach token issuance", async () => {
  for (const change of [
    { RoleArn: "arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task" },
    { Expiration: "2026-99-99T00:00:00Z" },
    { Expiration: new Date().toISOString() },
    { Token: "x".repeat(16385) },
  ]) {
    let count = 0;
    await assert.rejects(
      createWorkloadTokenProvider(environment(), async () => {
        count++;
        return bytes({ ...metadata(), ...change });
      })(),
      { message: failure },
    );
    assert.equal(count, 1);
  }
});

test("issuance failure has no retry/cache and fixed error excludes private provider fragments", async () => {
  let calls = 0;
  const get = createWorkloadTokenProvider(
    environment(),
    async (_url, method) => {
      calls++;
      if (method === "GET") return bytes(metadata());
      throw new Error("synthetic-private-marker");
    },
  );
  await assert.rejects(get(), { message: failure });
  await assert.rejects(get(), { message: failure });
  assert.equal(calls, 4);
});

test("one cancelled waiter does not cancel a shared mint for another caller", async () => {
  let release;
  const gate = new Promise((resolve) => {
    release = resolve;
  });
  const get = createWorkloadTokenProvider(
    environment(),
    async (_url, method) => {
      if (method === "GET") {
        await gate;
        return bytes(metadata());
      }
      return bytes({
        IdentityId: "us-east-1:30000000-0000-0000-0000-000000000001",
        Token: token,
      });
    },
  );
  const abort = new AbortController();
  const cancelled = get(abort.signal);
  const other = get();
  abort.abort();
  await assert.rejects(cancelled, { message: failure });
  release();
  assert.equal(await other, token);
});

test("real bounded HTTP transport refuses redirects, encoding, overflow, trailers and stalled EOF", async (t) => {
  const server = createServer((request, response) => {
    switch (request.url) {
      case "/redirect":
        response.writeHead(302, { Location: "/ok" });
        response.end();
        break;
      case "/encoded":
        response.writeHead(200, { "Content-Encoding": "gzip" });
        response.end("{}");
        break;
      case "/overflow":
        response.writeHead(200);
        response.end("x".repeat(100));
        break;
      case "/trailers":
        response.writeHead(200, { Trailer: "X-Synthetic" });
        response.addTrailers({ "X-Synthetic": "value" });
        response.end("{}");
        break;
      case "/stall":
        response.writeHead(200);
        response.write("{");
        break;
      default:
        response.end("{}");
    }
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => {
    server.closeAllConnections();
    return new Promise((resolve) => server.close(resolve));
  });
  const base = `http://127.0.0.1:${server.address().port}`;
  assert.equal(
    Buffer.from(
      await boundedWorkloadWire(
        new URL(base + "/ok"),
        "GET",
        {},
        undefined,
        32,
        AbortSignal.timeout(1000),
      ),
    ).toString(),
    "{}",
  );
  for (const path of ["redirect", "encoded", "overflow", "trailers", "stall"])
    await assert.rejects(
      boundedWorkloadWire(
        new URL(base + "/" + path),
        "GET",
        {},
        undefined,
        32,
        AbortSignal.timeout(150),
      ),
      { message: failure },
    );
});
