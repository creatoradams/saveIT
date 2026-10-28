import { readFileSync } from "node:fs";
const report = JSON.parse(readFileSync(process.argv[2], "utf8"));
if (!Array.isArray(report.projects) || report.projects.length === 0)
  throw new Error("NuGet audit returned no project results.");
if (report.problems?.length)
  throw new Error("NuGet vulnerability audit could not complete.");
const affected = (report.projects || []).flatMap((project) =>
  (project.frameworks || []).flatMap((framework) =>
    [
      ...(framework.topLevelPackages || []),
      ...(framework.transitivePackages || []),
    ].filter((pkg) => pkg.vulnerabilities?.length),
  ),
);
if (affected.length)
  throw new Error(
    `NuGet audit found vulnerable packages: ${affected.map((pkg) => pkg.id).join(", ")}`,
  );
console.log("NuGet vulnerability audit: no reported vulnerable packages.");
