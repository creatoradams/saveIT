// Isolated synthetic client, never the ordinary web service. Private fixture
// files are read only by this runner, as their host owner; nothing is logged.
import https from "node:https";
import { readFileSync } from "node:fs";
const ca = readFileSync("/opt/saveit/trust/api-ca.crt");
function request(name, path = "/health/ready", headers = {}) {
  return new Promise((resolve, reject) => {
    const options = {
      hostname: "api-client-auth",
      servername: "api",
      port: 8443,
      path,
      ca,
      agent: false,
      headers: { Host: "api", ...headers },
      maxHeaderSize: 8192,
    };
    if (name) {
      options.cert = readFileSync(`/run/client/${name}.crt`);
      options.key = readFileSync(`/run/client/${name}.key`);
    }
    const req = https.get(options, (response) => {
      let bytes = 0;
      response.on("data", (chunk) => {
        bytes += chunk.length;
        if (bytes > 4096)
          response.destroy(new Error("Fixture response limit."));
      });
      response.once("end", () => {
        clearTimeout(timer);
        resolve(response.statusCode);
      });
      response.once("error", (error) => {
        clearTimeout(timer);
        reject(error);
      });
    });
    const timer = setTimeout(
      () => req.destroy(new Error("Fixture deadline.")),
      3000,
    );
    req.once("error", (error) => {
      clearTimeout(timer);
      if (
        error.code === "ECONNRESET" ||
        /^ERR_SSL_|^ERR_TLS_/.test(error.code ?? "") ||
        error.code === "EPROTO"
      )
        resolve(null);
      else reject(error);
    });
  });
}
async function check(name, expected, label, path, headers) {
  if ((await request(name, path, headers)) !== expected)
    throw new Error("Client TLS fixture check failed.");
  console.log(`Packaged client TLS canary passed: ${label}`);
}
try {
  await check("valid", 200, "approved client and separate trusted CA");
  await check("spare", 200, "second approved rotation pin");
  await check(null, null, "certificate required");
  await check("unapproved", null, "same CA with unapproved leaf refused");
  await check("foreign", null, "pinned leaf from foreign CA refused");
  await check("expired", null, "pinned expired leaf refused");
  await check("server", null, "unpinned server-purpose certificate refused");
  await check(
    null,
    null,
    "forwarded certificate headers grant nothing",
    "/health/ready",
    {
      "X-Forwarded-Client-Cert": "synthetic-value",
      "X-Client-Cert": "synthetic-value",
    },
  );
  await check(
    "valid",
    401,
    "valid peer remains unauthenticated as a user",
    "/api/v1/organizations",
  );
  await check("valid", 200, "positive control after rejection cases");
} catch {
  console.error("Packaged client TLS verification failed.");
  process.exitCode = 1;
}
