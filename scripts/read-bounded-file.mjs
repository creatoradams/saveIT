import { closeSync, constants, fstatSync, openSync, readSync } from "node:fs";

// Check/read the same open file description, rather than stat a path then reopen it.
// Bound reads even if a file grows after fstat; do not follow symlinks where supported.
export function readBoundedFile(path, maximum) {
  return withBoundedFile(path, maximum, (bytes) => bytes);
}
export function withBoundedFile(path, maximum, use) {
  if (!Number.isSafeInteger(maximum) || maximum < 0)
    throw new Error("Invalid file read limit.");
  const fd = openSync(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0));
  try {
    const stat = fstatSync(fd);
    if (!stat.isFile() || stat.size > maximum)
      throw new Error("Expected a bounded regular file.");
    const chunks = [];
    let total = 0;
    for (;;) {
      const buffer = Buffer.alloc(Math.min(64 * 1024, maximum - total + 1));
      const size = readSync(fd, buffer, 0, buffer.length, total);
      if (size === 0) return use(Buffer.concat(chunks, total), fd);
      total += size;
      if (total > maximum) throw new Error("File exceeds read limit.");
      chunks.push(buffer.subarray(0, size));
    }
  } finally {
    closeSync(fd);
  }
}
