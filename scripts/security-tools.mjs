import { createHash } from "node:crypto";
import { chmodSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { withBoundedFile } from "./read-bounded-file.mjs";

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
  const extract = (bytes, fd) => {
    if (createHash("sha256").update(bytes).digest("hex") !== tool.sha256)
      throw new Error(
        "Security tool archive checksum mismatch; nothing was executed.",
      );
    // Inherit the verified, still-open seekable file as stdin. ZIP readers need
    // seeking; a byte pipe is insufficient. Never reopen the cache path for tar.
    execFileSync(
      "tar",
      [
        tool.asset.endsWith(".tar.gz") ? "-xzf" : "-xf",
        "-",
        "-C",
        tool.directory,
        tool.binary,
      ],
      {
        stdio: [fd, "pipe", "pipe"],
        windowsHide: true,
        timeout: 15_000,
        maxBuffer: 2 * 1024 * 1024,
      },
    );
    chmodSync(tool.path, 0o755);
  };
  try {
    withBoundedFile(archive, 64 * 1024 * 1024, extract);
    return tool;
  } catch (error) {
    if (error.code !== "ENOENT" || error.path !== archive) throw error;
  }
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
  const bytes = Buffer.concat(chunks);
  if (createHash("sha256").update(bytes).digest("hex") !== tool.sha256)
    throw new Error(
      "Security tool archive checksum mismatch; nothing was executed.",
    );
  // Atomic create; another writer winning this race fails closed instead of overwriting.
  writeFileSync(archive, bytes, { flag: "wx", mode: 0o600 });
  withBoundedFile(archive, 64 * 1024 * 1024, extract);
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
