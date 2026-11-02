import { readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { readBoundedFile } from "./read-bounded-file.mjs";

export function assessCodeql(report) {
  if (
    report?.version !== "2.1.0" ||
    !Array.isArray(report.runs) ||
    report.runs.length === 0
  )
    throw new Error("Malformed CodeQL SARIF report.");
  const summary = { results: 0, high: 0, medium: 0, low: 0, errors: 0 };
  for (const run of report.runs) {
    if (
      run?.tool?.driver?.name !== "CodeQL" ||
      !Array.isArray(run.results) ||
      run.invocations?.some((x) => x.executionSuccessful === false)
    )
      throw new Error("CodeQL did not complete successfully.");
    const rules = new Map(
      [
        ...(run.tool.driver.rules ?? []),
        ...(run.tool.extensions ?? []).flatMap((x) => x.rules ?? []),
      ].map((x) => [x.id, x]),
    );
    for (const result of run.results) {
      const rule = rules.get(result.ruleId ?? result.rule?.id);
      if (!rule) throw new Error("CodeQL result is missing its rule metadata.");
      summary.results++;
      if ((result.level ?? rule.defaultConfiguration?.level) === "error")
        summary.errors++;
      const raw = rule.properties?.["security-severity"];
      if (raw === undefined) {
        if (rule.properties?.tags?.includes("security"))
          throw new Error("CodeQL security severity is unavailable.");
        continue;
      }
      if (!/^\d+(\.\d+)?$/.test(String(raw)) || Number(raw) > 10)
        throw new Error("Invalid CodeQL security severity.");
      const score = Number(raw);
      if (score >= 7) summary.high++;
      else if (score >= 4) summary.medium++;
      else summary.low++;
    }
  }
  return summary;
}
if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const directory = process.argv[2];
  if (!directory) throw new Error("CodeQL results directory is required.");
  const files = readdirSync(directory).filter((x) => x.endsWith(".sarif"));
  if (files.length === 0 || files.length > 16)
    throw new Error("Expected bounded CodeQL SARIF files.");
  let failed = false;
  for (const file of files) {
    const path = join(directory, file);
    const result = assessCodeql(
      JSON.parse(readBoundedFile(path, 64 * 1024 * 1024).toString("utf8")),
    );
    console.log(JSON.stringify({ report: file, ...result }));
    failed ||= result.high > 0 || result.errors > 0;
  }
  if (failed) process.exitCode = 1;
}
