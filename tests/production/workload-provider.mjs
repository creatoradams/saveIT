// Synthetic isolated fixture, never used as SaveIT identity or a cloud client.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { readFileSync } from "node:fs";
import { sign } from "node:crypto";
import { verifySyntheticSigV4 } from "./synthetic-sigv4.mjs";
const root = "/run/workload/";
const pool = "us-east-1:10000000-0000-0000-0000-000000000001";
const provider = "saveit.pilot.staging.web";
const counts = { metadata: 0, issuance: 0, unexpected: 0 };
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
        verifySyntheticSigV4(request, body, {
          access: "synthetic-access",
          secret: "synthetic-secret",
          token: "synthetic-session",
          service: "cognito-identity",
        });
        if (
          request.headers["x-amz-target"] !==
          "AWSCognitoIdentityService.GetOpenIdTokenForDeveloperIdentity"
        )
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
