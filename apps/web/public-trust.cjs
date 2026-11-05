const fs = require("node:fs");
const { createHash, X509Certificate } = require("node:crypto");
const maximumBytes = 128 * 1024;
const apiPath = "/opt/saveit/trust/api-ca.crt";
const invalid = "Public CA release configuration is invalid.";

function readBundle(path, expectedHash) {
  let fd;
  try {
    if (expectedHash !== undefined && !/^[0-9a-f]{64}$/.test(expectedHash))
      throw new Error();
    fd = fs.openSync(path, fs.constants.O_RDONLY | fs.constants.O_NONBLOCK);
    const stat = fs.fstatSync(fd);
    if (!stat.isFile() || stat.size <= 0 || stat.size > maximumBytes)
      throw new Error();
    const bytes = Buffer.alloc(maximumBytes + 1);
    let count = 0;
    while (count < bytes.length) {
      const read = fs.readSync(fd, bytes, count, bytes.length - count, null);
      if (read === 0) break;
      count += read;
    }
    const input = bytes.subarray(0, count);
    const sha256 = createHash("sha256").update(input).digest("hex");
    if (
      count === 0 ||
      count > maximumBytes ||
      input.some((byte) => byte > 127) ||
      (expectedHash !== undefined && sha256 !== expectedHash)
    )
      throw new Error();
    const pem = input.toString("ascii");
    const blocks = [
      ...pem.matchAll(
        /-----BEGIN CERTIFICATE-----\r?\n([A-Za-z0-9+/=\r\n]+)-----END CERTIFICATE-----/g,
      ),
    ];
    if (
      blocks.length === 0 ||
      blocks.length > 16 ||
      pem
        .replace(
          /-----BEGIN CERTIFICATE-----\r?\n[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----/g,
          "",
        )
        .trim()
    )
      throw new Error();
    const fingerprints = new Set();
    for (const block of blocks) {
      const compact = block[1].replace(/[\r\n]/g, "");
      const der = Buffer.from(compact, "base64");
      if (der.toString("base64") !== compact) throw new Error();
      const certificate = new X509Certificate(der);
      if (!der.equals(certificate.raw)) throw new Error();
      const key = certificate.publicKey;
      const strength = key.asymmetricKeyDetails;
      const validFrom = Date.parse(certificate.validFrom);
      const validTo = Date.parse(certificate.validTo);
      if (
        fingerprints.has(certificate.fingerprint256) ||
        !certificate.ca ||
        !Number.isFinite(validFrom) ||
        !Number.isFinite(validTo) ||
        validFrom > Date.now() ||
        validTo <= Date.now() ||
        !(
          (key.asymmetricKeyType === "rsa" && strength.modulusLength >= 2048) ||
          (key.asymmetricKeyType === "ec" &&
            ["prime256v1", "secp384r1", "secp521r1"].includes(
              strength.namedCurve,
            ))
        )
      )
        throw new Error();
      fingerprints.add(certificate.fingerprint256);
    }
    return { bytes: Buffer.from(input), sha256 };
  } catch {
    throw new Error(invalid);
  } finally {
    if (fd !== undefined) fs.closeSync(fd);
  }
}
function validate(path, expectedHash) {
  if (typeof expectedHash !== "string" || !/^[0-9a-f]{64}$/.test(expectedHash))
    throw new Error(invalid);
  return readBundle(path, expectedHash).sha256;
}

function configure() {
  const hash = process.env.SAVEIT_API_CA_SHA256;
  if (hash === undefined) return;
  if (
    process.env.NODE_EXTRA_CA_CERTS !== apiPath ||
    process.env.NODE_TLS_REJECT_UNAUTHORIZED === "0"
  )
    throw new Error(invalid);
  validate(apiPath, hash);
}
module.exports = { validate, readBundle, configure, apiPath };
