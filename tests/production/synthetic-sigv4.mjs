// Test-provider verification only. Production callers use the official SDK signer.
import { createHash, createHmac, timingSafeEqual } from "node:crypto";
const hash = (value) => createHash("sha256").update(value).digest("hex");
const hmac = (key, value) => createHmac("sha256", key).update(value).digest();
export function verifySyntheticSigV4(
  request,
  body,
  {
    access,
    secret,
    token,
    service,
    host = service + ".us-east-1.amazonaws.com",
  },
) {
  const auth =
    /^AWS4-HMAC-SHA256 Credential=([^/]+)\/(\d{8}\/us-east-1\/([a-z][a-z0-9-]*)\/aws4_request), SignedHeaders=([a-z0-9;-]+), Signature=([a-f0-9]{64})$/.exec(
      request.headers.authorization ?? "",
    );
  if (
    !auth ||
    auth[1] !== access ||
    auth[3] !== service ||
    request.headers["x-amz-security-token"] !== token ||
    request.method !== "POST" ||
    request.url !== "/" ||
    request.headers.host !== host
  )
    throw new Error();
  const names = auth[4].split(";");
  if (
    names.length > 32 ||
    new Set(names).size !== names.length ||
    [
      "host",
      "x-amz-date",
      "x-amz-security-token",
      "content-type",
      ...(request.headers["x-amz-target"] ? ["x-amz-target"] : []),
    ].some((x) => !names.includes(x))
  )
    throw new Error();
  const date = request.headers["x-amz-date"];
  if (
    !/^\d{8}T\d{6}Z$/.test(date ?? "") ||
    auth[2].slice(0, 8) !== date.slice(0, 8)
  )
    throw new Error();
  const time = Date.parse(
    date.slice(0, 4) +
      "-" +
      date.slice(4, 6) +
      "-" +
      date.slice(6, 8) +
      "T" +
      date.slice(9, 11) +
      ":" +
      date.slice(11, 13) +
      ":" +
      date.slice(13, 15) +
      "Z",
  );
  if (!Number.isFinite(time) || Math.abs(Date.now() - time) > 60000)
    throw new Error();
  const headers = names
    .map((name) => {
      if (typeof request.headers[name] !== "string") throw new Error();
      return (
        name + ":" + request.headers[name].trim().replace(/\s+/g, " ") + "\n"
      );
    })
    .join("");
  const canonical = `POST\n/\n\n${headers}\n${auth[4]}\n${hash(body)}`;
  const toSign = `AWS4-HMAC-SHA256\n${date}\n${auth[2]}\n${hash(canonical)}`;
  const key = hmac(
    hmac(
      hmac(hmac("AWS4" + secret, auth[2].slice(0, 8)), "us-east-1"),
      service,
    ),
    "aws4_request",
  );
  if (!timingSafeEqual(hmac(key, toSign), Buffer.from(auth[5], "hex")))
    throw new Error();
}
