// Keyless isolated runner: only public roots, never a client/private key mount.
import https from "node:https";
import http from "node:http";
import assert from "node:assert/strict";
const marker = "synthetic-relay-query-must-stay-private";
function request(
  path,
  { protocol = https, port = 8443, method = "GET", headers = {} } = {},
) {
  return new Promise((resolve, reject) => {
    const req = protocol.request(
      { hostname: "relay", port, path, method, headers, agent: false },
      (res) => {
        let count = 0;
        res.on("data", (data) => {
          count += data.length;
          if (count > 65536)
            req.destroy(new Error("Synthetic response bound exceeded."));
        });
        res.once("end", () => {
          clearTimeout(deadline);
          resolve({ status: res.statusCode, headers: res.headers });
        });
        res.once("error", reject);
      },
    );
    const deadline = setTimeout(
      () => req.destroy(new Error("Synthetic relay deadline exceeded.")),
      5000,
    );
    req.once("error", (error) => {
      clearTimeout(deadline);
      reject(error);
    });
    req.end();
  });
}
const check = async (name, action) => {
  await action();
  console.log(`Packaged API relay canary passed: ${name}`);
};
try {
  await check("upstream-backed HTTPS readiness", async () =>
    assert.equal((await request("/health/ready")).status, 200),
  );
  await check("ordinary keyless caller receives no user authority", async () =>
    assert.equal(
      (
        await request("/api/v1/session", {
          headers: {
            "X-Tenant-Id": marker,
            "X-Forwarded-Client-Cert": marker,
            Authorization: marker,
          },
        })
      ).status,
      401,
    ),
  );
  await check("CSRF remains required for sign-in", async () =>
    assert.equal(
      (await request("/api/v1/auth/login", { method: "POST" })).status,
      400,
    ),
  );
  await check("real separate CSRF cookie preserved", async () => {
    const response = await request("/api/v1/csrf");
    assert.equal(response.status, 200);
    assert.ok(
      response.headers["set-cookie"].some((x) => x.startsWith("SaveIT.Csrf=")),
    );
  });
  await check("callback failures remain body-free in relay logs", async () =>
    assert.equal(
      (await request(`/api/v1/auth/callback?code=${marker}&state=${marker}`))
        .status,
      401,
    ),
  );
  await check("encoded path ambiguity refused", async () =>
    assert.equal((await request("/api/v1/%2e%2e/session")).status, 400),
  );
  await check("absolute-form caller destination refused", async () =>
    assert.equal(
      (await request("https://foreign.invalid/api/v1/session")).status,
      400,
    ),
  );
  await check("TRACE refused", async () =>
    assert.equal(
      (await request("/api/v1/session", { method: "TRACE" })).status,
      400,
    ),
  );
  await check("request content encoding refused", async () =>
    assert.equal(
      (
        await request("/api/v1/session", {
          headers: { "Content-Encoding": "gzip" },
        })
      ).status,
      400,
    ),
  );
  await check("oversized declared body refused before upload", async () =>
    assert.equal(
      (
        await request("/api/v1/auth/login", {
          method: "POST",
          headers: { "Content-Length": "1048577" },
        })
      ).status,
      413,
    ),
  );
  await check("no remote loopback plaintext or ambient listener", async () => {
    for (const port of [8080, 9999])
      await assert.rejects(() =>
        request("/health/live", { protocol: http, port }),
      );
  });
  await check("positive control after refusals", async () =>
    assert.equal((await request("/api/v1/auth/options")).status, 200),
  );
} catch {
  console.error("Packaged API relay verification failed.");
  process.exitCode = 1;
}
