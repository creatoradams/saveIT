// Test-only bounded stdin transport. Private bytes never enter child arguments.
import { spawn } from "node:child_process";
let sequence = 0;
async function within(promise, milliseconds) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(
          () =>
            reject(new Error("Synthetic preflight process deadline exceeded.")),
          milliseconds,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}
async function boundedClose(child, completion, milliseconds) {
  try {
    return await within(completion, milliseconds);
  } catch {
    child.kill("SIGKILL");
    try {
      await within(completion, 5000);
    } catch {
      child.stdin?.destroy();
      child.stdout?.destroy();
      child.stderr?.destroy();
      child.unref();
    }
    throw new Error(
      "Synthetic preflight process failed or exceeded its deadline.",
    );
  }
}
export async function runProtectedCommand(
  command,
  bytes,
  expected,
  label,
  success,
  failure,
  holdOpen = false,
  network = "none",
) {
  const owner = `saveit-preflight-${process.pid}`;
  const name = `${owner}-${++sequence}`;
  const child = spawn(
    "docker",
    [
      "run",
      "--rm",
      "--network",
      network,
      "--read-only",
      "--name",
      name,
      "--label",
      `com.saveit.preflight-owner=${owner}`,
      "-i",
      "saveit-deployment:test",
      ...command,
    ],
    { stdio: ["pipe", "pipe", "pipe"] },
  );
  let output = "",
    errors = "",
    exceeded = false,
    closed = false;
  child.stdout.on("data", (data) => {
    output += data;
    if (output.length > 4096) {
      output = "";
      exceeded = true;
    }
  });
  child.stderr.on("data", (data) => {
    errors += data;
    if (errors.length > 4096) {
      errors = "";
      exceeded = true;
    }
  });
  child.stdin.on("error", () => {}); // A refused command may close stdin before consuming it.
  const completion = new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", (code) => {
      closed = true;
      resolve(code);
    });
  });
  try {
    if (holdOpen) child.stdin.write(bytes);
    else child.stdin.end(bytes);
    const code = await boundedClose(child, completion, 20_000);
    if (
      exceeded ||
      code !== (expected ? 0 : 1) ||
      output.trim() !== (expected ? success : "") ||
      errors.trim() !== (expected ? "" : failure)
    )
      throw new Error("Synthetic protected configuration canary failed.");
    console.log(`Packaged protected command passed: ${label}`);
  } finally {
    if (!closed) {
      child.kill("SIGKILL");
      try {
        await within(completion, 5000);
      } catch {
        child.stdin?.destroy();
        child.stdout?.destroy();
        child.stderr?.destroy();
        child.unref();
      }
    }
    // Docker can outlive a killed client. Inspect this exact container's owner
    // label before removing it; never enumerate/delete unrelated resources.
    const inspect = spawn(
      "docker",
      [
        "inspect",
        "--format",
        '{{index .Config.Labels "com.saveit.preflight-owner"}}',
        name,
      ],
      { stdio: ["ignore", "pipe", "ignore"] },
    );
    let found = "";
    inspect.stdout.on("data", (data) => {
      found = (found + data).slice(-256);
    });
    const inspection = new Promise((resolve, reject) => {
      inspect.once("error", reject);
      inspect.once("close", resolve);
    });
    {
      const code = await boundedClose(inspect, inspection, 5000);
      if (code === 0 && found.trim() === owner) {
        const remove = spawn("docker", ["rm", "--force", name], {
          stdio: "ignore",
        });
        const removed = new Promise((resolve, reject) => {
          remove.once("error", reject);
          remove.once("close", resolve);
        });
        if ((await boundedClose(remove, removed, 5000)) !== 0)
          throw new Error("Synthetic preflight cleanup failed.");
      } else if (code !== 1)
        throw new Error("Synthetic preflight ownership check failed.");
    }
  }
}
