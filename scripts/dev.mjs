import { spawn, spawnSync } from "node:child_process";
import { resolve } from "node:path";
import {
  startDatabase,
  migrate,
  appConnection,
  workerEnvironment,
  run,
} from "./database.mjs";

const pg = await startDatabase();
const children = [];
let stopping = false;
async function stop(code = 0) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (!child.pid || child.exitCode !== null) continue;
    if (process.platform === "win32")
      spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], {
        windowsHide: true,
        stdio: "ignore",
      });
    else child.kill("SIGTERM");
  }
  await pg.stop();
  process.exit(code);
}
process.on("SIGINT", () => stop());
process.on("SIGTERM", () => stop());
try {
  await migrate();
  await run("dotnet", ["build", "services/workers", "--no-restore"]);
  await run(
    "dotnet",
    [
      "services/workers/bin/Debug/net10.0/SaveIT.Workers.dll",
      "--rebuild",
      "--once",
    ],
    workerEnvironment,
  );
  const apiPort = process.env.SAVEIT_API_PORT || "5080";
  const webPort = process.env.SAVEIT_WEB_PORT || "3000";
  const api = spawn(
    "dotnet",
    [resolve("services/api/bin/Debug/net10.0/SaveIT.Api.dll")],
    {
      cwd: resolve("services/api"),
      stdio: "inherit",
      windowsHide: true,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
        DevelopmentIdentity__Enabled: "true",
        ConnectionStrings__App: appConnection,
      },
    },
  );
  children.push(api);
  const worker = spawn(
    "dotnet",
    [resolve("services/workers/bin/Debug/net10.0/SaveIT.Workers.dll")],
    {
      stdio: "inherit",
      windowsHide: true,
      env: { ...process.env, ...workerEnvironment },
    },
  );
  children.push(worker);
  const web = spawn(
    process.execPath,
    [
      resolve("node_modules/next/dist/bin/next"),
      "dev",
      "--hostname",
      "127.0.0.1",
      "--port",
      webPort,
    ],
    {
      cwd: resolve("apps/web"),
      stdio: "inherit",
      windowsHide: true,
      env: {
        ...process.env,
        SAVEIT_API_ORIGIN: `http://127.0.0.1:${apiPort}`,
        NEXT_TELEMETRY_DISABLED: "1",
      },
    },
  );
  children.push(web);
  for (const child of children) {
    child.on("error", (error) => {
      console.error(error);
      stop(1);
    });
    child.on("exit", (code) => {
      if (!stopping) stop(code || 1);
    });
  }
  console.log(
    `SaveIT: http://127.0.0.1:${webPort} — development identity, synthetic data only.`,
  );
} catch (error) {
  console.error(error);
  await stop(1);
}
