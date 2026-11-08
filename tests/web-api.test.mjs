import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createServer } from "node:http";
import test from "node:test";
import ts from "typescript";

// Exercise the actual client with existing TypeScript tooling, without adding a
// browser test framework or maintaining a second JavaScript implementation.
const compiled = ts.transpileModule(
  readFileSync(new URL("../apps/web/lib/api.ts", import.meta.url), "utf8"),
  {
    compilerOptions: {
      target: ts.ScriptTarget.ES2022,
      module: ts.ModuleKind.ESNext,
    },
  },
).outputText;
const { api } = await import(
  `data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`
);

async function fixture(t, handle) {
  const server = createServer(handle);
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => {
    server.closeAllConnections();
    return new Promise((resolve) => server.close(resolve));
  });
  const origin = `http://127.0.0.1:${server.address().port}`;
  const nativeFetch = globalThis.fetch;
  t.mock.method(globalThis, "fetch", (path, options) =>
    nativeFetch(new URL(path, origin), options),
  );
}

for (const partialBody of [false, true]) {
  test(
    `cancelling a mutation aborts the CSRF ${partialBody ? "body" : "request"} and sends no write`,
    { timeout: 5000 },
    async (t) => {
      const received = Promise.withResolvers();
      const closed = Promise.withResolvers();
      let writes = 0;
      await fixture(t, (request, response) => {
        if (request.url !== "/api/v1/csrf") {
          writes++;
          response.end("{}");
          return;
        }
        response.on("close", closed.resolve);
        if (partialBody) {
          response.writeHead(200, { "Content-Type": "application/json" });
          response.write('{"token":');
        }
        received.resolve();
      });
      const controller = new AbortController();
      const pending = api("/documents", {
        method: "POST",
        body: "{}",
        signal: controller.signal,
      });
      const rejected = assert.rejects(pending, { name: "AbortError" });
      await received.promise;
      controller.abort();
      await rejected;
      await closed.promise;
      assert.equal(writes, 0);
    },
  );
}

test(
  "each mutation obtains a fresh token and preserves same-origin no-store requests",
  { timeout: 5000 },
  async (t) => {
    let tokens = 0;
    const writes = [];
    await fixture(t, (request, response) => {
      response.setHeader("Content-Type", "application/json");
      if (request.url === "/api/v1/csrf") {
        response.end(JSON.stringify({ token: `csrf-${++tokens}` }));
      } else {
        writes.push(request.headers["x-csrf-token"]);
        response.end("{}");
      }
    });
    const controller = new AbortController();
    await api("/documents", {
      method: "POST",
      body: "{}",
      signal: controller.signal,
    });
    await api("/documents", {
      method: "PUT",
      body: "{}",
      signal: controller.signal,
    });
    assert.deepEqual(writes, ["csrf-1", "csrf-2"]);
    const calls = globalThis.fetch.mock.calls;
    assert.equal(calls.length, 4);
    for (const call of calls) {
      assert.equal(call.arguments[1].signal, controller.signal);
      assert.equal(call.arguments[1].credentials, "same-origin");
      assert.equal(call.arguments[1].cache, "no-store");
    }
  },
);
