import assert from "node:assert/strict";
import { Readable } from "node:stream";
import test from "node:test";
import { createHash, createHmac } from "node:crypto";
import {
  CognitoIdentityClient,
  GetOpenIdTokenForDeveloperIdentityCommand,
} from "@aws-sdk/client-cognito-identity";
import { HttpResponse } from "@smithy/protocol-http";
import { verifySyntheticSigV4 } from "./production/synthetic-sigv4.mjs";

test("synthetic provider verifies the pinned official signer and detects changed signed bytes, token and authority", async () => {
  const credentials = {
    accessKeyId: "synthetic-access",
    secretAccessKey: "synthetic-secret",
    sessionToken: "synthetic-session",
  };
  let calls = 0;
  const client = new CognitoIdentityClient({
    region: "us-east-1",
    endpoint: "https://cognito-identity.us-east-1.amazonaws.com",
    credentials,
    defaultsMode: "standard",
    retryMode: "standard",
    maxAttempts: 1,
    authSchemePreference: ["sigv4"],
    ignoreConfiguredEndpointUrls: true,
    requestHandler: {
      handle: async (wire) => {
        calls++;
        const request = {
          method: wire.method,
          url: wire.path,
          headers: wire.headers,
        };
        const body =
          typeof wire.body === "string"
            ? wire.body
            : Buffer.from(wire.body).toString("utf8");
        const expected = {
          access: credentials.accessKeyId,
          secret: credentials.secretAccessKey,
          token: credentials.sessionToken,
          service: "cognito-identity",
        };
        verifySyntheticSigV4(request, body, expected);
        // Correctly signed but impossible month: refusal must not depend on signature mismatch.
        const invalid = { ...request, headers: { ...request.headers } };
        invalid.headers["x-amz-date"] =
          invalid.headers["x-amz-date"].slice(0, 4) +
          "14" +
          invalid.headers["x-amz-date"].slice(6);
        const hash = (value) =>
          createHash("sha256").update(value).digest("hex");
        const hmac = (key, value) =>
          createHmac("sha256", key).update(value).digest();
        const auth =
          /Credential=([^/]+)\/([^,]+), SignedHeaders=([^,]+), Signature=/.exec(
            invalid.headers.authorization,
          );
        const scope =
          invalid.headers["x-amz-date"].slice(0, 8) + auth[2].slice(8);
        const headers = auth[3]
          .split(";")
          .map(
            (name) =>
              `${name}:${String(invalid.headers[name]).trim().replace(/\s+/g, " ")}\n`,
          )
          .join("");
        const canonical = `POST\n/\n\n${headers}\n${auth[3]}\n${hash(body)}`;
        const key = hmac(
          hmac(
            hmac(
              hmac("AWS4" + expected.secret, scope.slice(0, 8)),
              "us-east-1",
            ),
            expected.service,
          ),
          "aws4_request",
        );
        const signature = hmac(
          key,
          `AWS4-HMAC-SHA256\n${invalid.headers["x-amz-date"]}\n${scope}\n${hash(canonical)}`,
        ).toString("hex");
        invalid.headers.authorization = `AWS4-HMAC-SHA256 Credential=${expected.access}/${scope}, SignedHeaders=${auth[3]}, Signature=${signature}`;
        assert.throws(() => verifySyntheticSigV4(invalid, body, expected));
        assert.throws(() =>
          verifySyntheticSigV4(request, body + " ", expected),
        );
        assert.throws(() =>
          verifySyntheticSigV4(
            {
              ...request,
              headers: {
                ...request.headers,
                "x-amz-security-token": "foreign",
              },
            },
            body,
            expected,
          ),
        );
        assert.throws(() =>
          verifySyntheticSigV4(
            {
              ...request,
              headers: { ...request.headers, host: "foreign.invalid" },
            },
            body,
            expected,
          ),
        );
        return {
          response: new HttpResponse({
            statusCode: 200,
            headers: { "content-type": "application/x-amz-json-1.1" },
            body: Readable.from([
              JSON.stringify({
                IdentityId: "synthetic-identity",
                Token: "synthetic-no-authority",
              }),
            ]),
          }),
        };
      },
    },
  });
  try {
    const result = await client.send(
      new GetOpenIdTokenForDeveloperIdentityCommand({
        IdentityPoolId: "us-east-1:10000000-0000-0000-0000-000000000001",
        Logins: { "saveit.pilot.staging.web": "web" },
        TokenDuration: 60,
      }),
    );
    assert.equal(result.Token, "synthetic-no-authority");
    assert.equal(calls, 1);
  } finally {
    client.destroy();
  }
});
