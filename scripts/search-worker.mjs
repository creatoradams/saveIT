import { run, workerEnvironment } from "./database.mjs";
await run(
  "dotnet",
  [
    "services/workers/bin/Debug/net10.0/SaveIT.Workers.dll",
    ...process.argv.slice(2),
  ],
  workerEnvironment,
);
