import { createHash } from "node:crypto";
import {
  chmodSync,
  existsSync,
  mkdirSync,
  readFileSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

const tools = JSON.parse(
  readFileSync(new URL("./security-tools.json", import.meta.url), "utf8"),
);
export function securityTool(name) {
  const tool = tools[name];
  if (
    !Object.hasOwn(tools, name) ||
    process.arch !== "x64" ||
    !["linux", "win32"].includes(process.platform)
  )
    throw new Error("Unsupported pinned security tool/platform.");
  const platform = tool[process.platform];
  return {
    ...tool,
    ...platform,
    directory: resolve(".local/tools", `${name}-${tool.version}`),
    path: resolve(".local/tools", `${name}-${tool.version}`, platform.binary),
  };
}
export async function installSecurityTool(name) {
  const tool = securityTool(name);
  if (!/^[0-9a-f]{64}$/.test(tool.sha256))
    throw new Error("Invalid security tool checksum.");
  mkdirSync(tool.directory, { recursive: true });
  const archive = resolve(tool.directory, tool.asset);
  if (!existsSync(archive)) {
    const response = await fetch(
      `https://github.com/${tool.repository}/releases/download/v${tool.version}/${tool.asset}`,
      { signal: AbortSignal.timeout(60_000) },
    );
    if (!response.ok) throw new Error("Security tool download failed.");
    const chunks = [];
    let size = 0;
    for await (const chunk of response.body) {
      size += chunk.length;
      if (size > 64 * 1024 * 1024)
        throw new Error("Security tool archive is too large.");
      chunks.push(chunk);
    }
    writeFileSync(archive, Buffer.concat(chunks));
  }
  if (statSync(archive).size > 64 * 1024 * 1024)
    throw new Error("Cached security tool archive is too large.");
  const checksum = createHash("sha256")
    .update(readFileSync(archive))
    .digest("hex");
  if (checksum !== tool.sha256)
    throw new Error(
      "Security tool archive checksum mismatch; nothing was executed.",
    );
  // Extract only the named executable from the verified archive; never run a downloaded installer.
  execFileSync("tar", ["-xf", archive, "-C", tool.directory, tool.binary], {
    stdio: "pipe",
    windowsHide: true,
  });
  chmodSync(tool.path, 0o755);
  return tool;
}
if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const tool = await installSecurityTool(process.argv[2]);
  console.log(
    `Verified ${process.argv[2]} ${tool.version} for ${process.platform}/x64.`,
  );
}
