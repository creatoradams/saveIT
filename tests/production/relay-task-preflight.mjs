// Supplied synthetic observations only; no AWS request or activation.
import { readFile } from "node:fs/promises";
import { runProtectedCommand } from "./protected-command.mjs";

const container = JSON.parse(
  await readFile(
    new URL(
      "../../infra/terraform/ecs-tasks/tests/relay-container.json",
      import.meta.url,
    ),
    "utf8",
  ),
);
const arn =
  "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-relay:10";
const fixture = {
  expected: {
    accountId: "111122223333",
    region: "us-east-1",
    cell: "pilot",
    environment: "staging",
    taskDefinitionArn: arn,
    image: container.image,
    configurationArn: container.command[3],
    configurationVersionId: container.command[4],
  },
  observed: {
    taskDefinition: {
      taskDefinitionArn: arn,
      family: "saveit-pilot-staging-relay",
      revision: 10,
      status: "ACTIVE",
      taskRoleArn:
        "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-task",
      executionRoleArn:
        "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution",
      networkMode: "awsvpc",
      requiresCompatibilities: ["FARGATE"],
      cpu: "512",
      memory: "1024",
      runtimePlatform: {
        cpuArchitecture: "X86_64",
        operatingSystemFamily: "LINUX",
      },
      volumes: [{ name: "runtime", configuredAtLaunch: false }],
      containerDefinitions: [container],
    },
  },
};
const success =
  "SaveIT relay task preflight passed (supplied metadata only; no launch approval).";
const failure = "SaveIT relay task preflight failed.";
async function run(value, expected, label, holdOpen = false, extra = []) {
  await runProtectedCommand(
    ["check-relay-task", ...extra],
    typeof value === "string" ? value : JSON.stringify(value),
    expected,
    `relay task ${label}`,
    success,
    failure,
    holdOpen,
  );
}
try {
  await run(fixture, true, "independent exact fixture without networking");
  let value = structuredClone(fixture);
  value.observed.taskDefinition.containerDefinitions[0].environment.push({
    name: "ConnectionStrings__App",
    value: "synthetic-private-marker",
  });
  await run(value, false, "private environment drift stays out of output");
  value = structuredClone(fixture);
  value.observed.taskDefinition.containerDefinitions[0].image =
    container.image.replace(/4{64}$/, "5".repeat(64));
  await run(value, false, "registry manifest mismatch");
  value = structuredClone(fixture);
  value.observed.taskDefinition.containerDefinitions[0].command[4] =
    "10000000-0000-0000-0000-000000000008";
  await run(value, false, "pinned configuration mismatch");
  value = structuredClone(fixture);
  value.observed.taskDefinition.volumes[0].host = { sourcePath: "/host" };
  await run(value, false, "host-volume escalation");
  await run(
    JSON.stringify(fixture).replace(
      '"status":"ACTIVE"',
      '"status":"INACTIVE","st\\u0061tus":"ACTIVE"',
    ),
    false,
    "escaped duplicate",
  );
  await run(fixture, false, "mixed arguments", false, [
    "serve-api-relay-secrets",
  ]);
  await run(fixture, false, "EOF deadline", true);
  console.log("Packaged relay task preflight passed: 8 synthetic checks.");
} catch {
  console.error("Packaged relay task preflight failed.");
  process.exitCode = 1;
}
