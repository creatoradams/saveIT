// Synthetic isolated fixture, never used as SaveIT identity or a cloud client.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { readFileSync } from "node:fs";
import { createHash, createHmac, sign, timingSafeEqual } from "node:crypto";
const root = "/run/workload/";
const pool = "us-east-1:10000000-0000-0000-0000-000000000001";
const provider = "saveit.pilot.staging.web";
const counts = { metadata: 0, issuance: 0, unexpected: 0 };
const hash = (value) => createHash("sha256").update(value).digest("hex");
const hmac = (key, value) => createHmac("sha256", key).update(value).digest();
const encode = (value) =>
  Buffer.from(JSON.stringify(value)).toString("base64url");
let mode = "valid";
const control = httpServer((request, response) => {
  if (request.url === "/counts") return response.end(JSON.stringify(counts));
  if (request.url === "/mode" && request.method === "POST") {
    let value = "";
    request.on("data", (chunk) => {
      value += chunk;
      if (value.length > 32) request.destroy();
    });
    request.on("end", () => {
      if (!["valid", "long", "foreign"].includes(value)) {
        response.writeHead(400);
        response.end();
      } else {
        mode = value;
        response.end("ok");
      }
    });
    return;
  }
  if (
    request.url === "/v2/credentials/20000000-0000-0000-0000-000000000001" &&
    request.method === "GET"
  ) {
    counts.metadata++;
    return response.end(
      JSON.stringify({
        RoleArn: "arn:aws:iam::111122223333:role/saveit-pilot-staging-web-task",
        AccessKeyId: "synthetic-access",
        SecretAccessKey: "synthetic-secret",
        Token: "synthetic-session",
        Expiration: new Date(Date.now() + 600000).toISOString(),
      }),
    );
  }
  counts.unexpected++;
  response.writeHead(400);
  response.end();
});
const service = httpsServer(
  {
    key: readFileSync(root + "cognito.key"),
    cert: readFileSync(root + "cognito.crt"),
    minVersion: "TLSv1.2",
  },
  (request, response) => {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
      if (body.length > 4096) request.destroy();
    });
    request.on("end", () => {
      try {
        const auth =
          /^AWS4-HMAC-SHA256 Credential=synthetic-access\/(\d{8}\/us-east-1\/cognito-identity\/aws4_request), SignedHeaders=([a-z0-9;-]+), Signature=([a-f0-9]{64})$/.exec(
            request.headers.authorization ?? "",
          );
        if (
          !auth ||
          request.method !== "POST" ||
          request.url !== "/" ||
          request.headers.host !== "cognito-identity.us-east-1.amazonaws.com" ||
          request.headers["x-amz-security-token"] !== "synthetic-session" ||
          request.headers["x-amz-target"] !==
            "AWSCognitoIdentityService.GetOpenIdTokenForDeveloperIdentity"
        )
          throw new Error();
        const headers = auth[2]
          .split(";")
          .map(
            (name) =>
              `${name}:${String(request.headers[name]).trim().replace(/\s+/g, " ")}\n`,
          )
          .join("");
        const canonical = `POST\n/\n\n${headers}\n${auth[2]}\n${hash(body)}`;
        const toSign = `AWS4-HMAC-SHA256\n${request.headers["x-amz-date"]}\n${auth[1]}\n${hash(canonical)}`;
        const key = hmac(
          hmac(
            hmac(
              hmac("AWS4synthetic-secret", auth[1].slice(0, 8)),
              "us-east-1",
            ),
            "cognito-identity",
          ),
          "aws4_request",
        );
        if (!timingSafeEqual(hmac(key, toSign), Buffer.from(auth[3], "hex")))
          throw new Error();
        if (
          JSON.stringify(JSON.parse(body)) !==
          JSON.stringify({
            IdentityPoolId: pool,
            Logins: { [provider]: "web" },
            TokenDuration: 60,
          })
        )
          throw new Error();
        counts.issuance++;
        const now = Math.floor(Date.now() / 1000);
        const content =
          encode({ alg: "RS512", kid: "fixture", typ: "JWS" }) +
          "." +
          encode({
            iss: "https://cognito-identity.amazonaws.com",
            aud: mode === "foreign" ? pool.slice(0, -1) + "2" : pool,
            sub: "us-east-1:30000000-0000-0000-0000-000000000001",
            amr: ["authenticated", provider],
            iat: now,
            exp: now + (mode === "long" ? 600 : 60),
          });
        const token =
          content +
          "." +
          sign(
            "RSA-SHA512",
            Buffer.from(content),
            readFileSync(root + "workload.key"),
          ).toString("base64url");
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(
          JSON.stringify({
            IdentityId: "us-east-1:30000000-0000-0000-0000-000000000001",
            Token: token,
          }),
        );
      } catch {
        counts.unexpected++;
        response.writeHead(400);
        response.end("{}");
      }
    });
  },
);
control.listen(80, "0.0.0.0");
service.listen(443, "0.0.0.0");
