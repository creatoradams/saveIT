import EmbeddedPostgres from "embedded-postgres";
import { existsSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

export const port = Number(process.env.SAVEIT_DB_PORT || 55432);
export const appConnection = `Host=127.0.0.1;Port=${port};Database=saveit;Username=saveit_app;Password=synthetic-app-only`;
export const ownerConnection = `Host=127.0.0.1;Port=${port};Database=saveit;Username=postgres;Password=local-development-only`;

export function run(command, args, env = {}) {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(command, args, {
      stdio: "inherit",
      env: { ...process.env, ...env },
      windowsHide: true,
    });
    child.once("error", reject);
    child.once("exit", (code) =>
      code === 0
        ? resolvePromise()
        : reject(new Error(`${command} exited with ${code}`)),
    );
  });
}

export async function startDatabase() {
  const dir = resolve(".local/postgres");
  mkdirSync(resolve(".local"), { recursive: true });
  const pg = new EmbeddedPostgres({
    databaseDir: dir,
    user: "postgres",
    password: "local-development-only",
    port,
    persistent: true,
    authMethod: "scram-sha-256",
    postgresFlags: ["-h", "127.0.0.1"],
    onLog: () => {},
    onError: (message) => console.error(String(message)),
  });
  if (!existsSync(resolve(dir, "PG_VERSION"))) await pg.initialise();
  await pg.start();
  try {
    const client = pg.getPgClient();
    await client.connect();
    try {
      const role = await client.query(
        "SELECT 1 FROM pg_roles WHERE rolname='saveit_app'",
      );
      if (!role.rowCount)
        await client.query(
          "CREATE ROLE saveit_app LOGIN PASSWORD 'synthetic-app-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS",
        );
      const database = await client.query(
        "SELECT 1 FROM pg_database WHERE datname='saveit'",
      );
      if (!database.rowCount) await client.query("CREATE DATABASE saveit");
    } finally {
      await client.end();
    }
    return pg;
  } catch (error) {
    await pg.stop();
    throw error;
  }
}

export async function migrate() {
  await run(
    "dotnet",
    [
      "run",
      "--project",
      "services/api",
      "--no-launch-profile",
      "--",
      "--migrate",
    ],
    {
      ASPNETCORE_ENVIRONMENT: "Development",
      DevelopmentIdentity__Enabled: "true",
      ConnectionStrings__App: appConnection,
      ConnectionStrings__Migration: ownerConnection,
    },
  );
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const pg = await startDatabase();
  try {
    await migrate();
  } catch (error) {
    await pg.stop();
    throw error;
  }
  console.log(
    `SaveIT local PostgreSQL is listening on 127.0.0.1:${port}. Ctrl+C to stop.`,
  );
  let stopping = false;
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    await pg.stop();
    process.exit(0);
  };
  process.on("SIGINT", stop);
  process.on("SIGTERM", stop);
  setInterval(() => {}, 60_000);
}
