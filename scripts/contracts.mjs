import { mkdirSync, writeFileSync } from "node:fs";
const response = await fetch(
  `${process.env.SAVEIT_API_ORIGIN || "http://127.0.0.1:5080"}/openapi/v1.json`,
);
if (!response.ok) throw new Error(`OpenAPI export failed: ${response.status}`);
const schema = await response.json();
delete schema.servers;
mkdirSync("contracts", { recursive: true });
writeFileSync("contracts/openapi.json", JSON.stringify(schema, null, 2) + "\n");
