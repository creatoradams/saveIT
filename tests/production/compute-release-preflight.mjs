// Fresh synthetic signing key stays in this process; only its public SPKI is mounted.
// Frozen mocked observations are not an AWS acquisition or provider/fencing test.
import { generateKeyPairSync, createHash, sign, constants } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { runProtectedCommand } from "./protected-command.mjs";

const contract = JSON.parse(
  await readFile(
    "infra/terraform/ecs-tasks/tests/compute-release-contract.json",
    "utf8",
  ),
);
const observations = JSON.parse(
  await readFile(
    "infra/terraform/ecs-tasks/tests/compute-release-observations.json",
    "utf8",
  ),
);
const { privateKey, publicKey } = generateKeyPairSync("rsa", {
  modulusLength: 3072,
});
const spki = publicKey.export({ type: "spki", format: "der" });
const pin = createHash("sha256").update(spki).digest("hex");
const path = resolve(".local/container-fixture/compute-release-public.der");
await writeFile(path, spki, { mode: 0o644 });
const header = {
  alg: "PS256",
  typ: "saveit-compute-release+jwt",
  kid: "sha256-" + pin,
};
const now = Math.floor(Date.now() / 1000);
const claims = {
  iss: "saveit-release",
  aud: "saveit-compute:111122223333:us-east-1:pilot:staging",
  iat: now - 1,
  exp: now + 299,
  jti: "80000000-0000-0000-0000-000000000001",
  contract,
};
function signed(payload) {
  const body =
    Buffer.from(JSON.stringify(header)).toString("base64url") +
    "." +
    Buffer.from(
      typeof payload === "string" ? payload : JSON.stringify(payload),
    ).toString("base64url");
  return (
    body +
    "." +
    sign("sha256", Buffer.from(body), {
      key: privateKey,
      padding: constants.RSA_PKCS1_PSS_PADDING,
      saltLength: 32,
    }).toString("base64url")
  );
}
const options = ["-v", path + ":/run/compute-public.der:ro"];
for (const [name, value] of Object.entries({
  SAVEIT_COMPUTE_ACCOUNT_ID: "111122223333",
  SAVEIT_COMPUTE_REGION: "us-east-1",
  SAVEIT_COMPUTE_CELL: "pilot",
  SAVEIT_COMPUTE_STAGE: "staging",
  SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE: "/run/compute-public.der",
  SAVEIT_COMPUTE_RELEASE_KEY_SHA256: pin,
  AWS_PROFILE: "synthetic-no-authority",
  AWS_ENDPOINT_URL: "https://foreign.invalid",
  SAVEIT_COMPUTE_CONNECTION: "synthetic-private-marker",
}))
  options.push("-e", name + "=" + value);
const success =
  "SaveIT compute release admission passed (supplied observations only; no launch or fencing).";
const failure = "SaveIT compute release admission failed.";
async function run(
  payload,
  observed,
  expected,
  label,
  holdOpen = false,
  extra = [],
) {
  await runProtectedCommand(
    ["check-compute-release", ...extra],
    JSON.stringify({ signedRelease: signed(payload), observations: observed }),
    expected,
    "compute release " + label,
    success,
    failure,
    holdOpen,
    "none",
    options,
  );
}
try {
  await run(
    claims,
    observations,
    true,
    "native nonroot PSS/DER and all four frozen contracts",
  );
  const drift = structuredClone(observations);
  drift.web.taskDefinition.containerDefinitions[0].environment.push({
    name: "ConnectionStrings__App",
    value: "synthetic-private-marker",
  });
  await run(claims, drift, false, "private drift remains out of output");
  for (const process of ["api", "web", "worker", "relay"]) {
    const changed = structuredClone(observations);
    changed[process].taskDefinition.containerDefinitions[0].privileged = true;
    await run(claims, changed, false, process + " privilege refusal");
  }
  const generation = structuredClone(claims);
  generation.contract.generation = "70000000-0000-0000-0000-000000000002";
  await run(generation, observations, false, "signed generation mismatch");
  await run(
    { ...claims, exp: now + 1000 },
    observations,
    false,
    "signed excessive lifetime",
  );
  await run(
    JSON.stringify(claims).replace(
      '"expectedVersion":1',
      '"expectedVersion":9,"expectedV\\u0065rsion":1',
    ),
    observations,
    false,
    "signed escaped duplicate",
  );
  const efs = structuredClone(observations);
  efs.api.taskDefinition.volumes.find(
    (x) => x.name === "session-keys",
  ).efsVolumeConfiguration.authorizationConfig.iam = "DISABLED";
  await run(claims, efs, false, "EFS authority substitution");
  await run(claims, observations, false, "mixed arguments", false, ["migrate"]);
  await run(claims, observations, false, "EOF deadline", true);
  console.log(
    "Packaged compute release preflight passed: 12 synthetic checks, no provider actions.",
  );
} catch {
  console.error("Packaged compute release preflight failed.");
  process.exitCode = 1;
}
