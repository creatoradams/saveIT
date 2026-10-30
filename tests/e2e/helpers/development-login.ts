import { test } from "@playwright/test";

// All browser/API sign-ins share one loopback address and one Playwright worker.
// Stay below the application's unchanged 30/minute limit, including extra contexts.
const attempts: number[] = [];
const windowMs = 61_000;
const permits = 25;
let ready: Promise<void> | undefined;

function pause(waitMs: number) {
  test.setTimeout(test.info().timeout + waitMs);
  return new Promise<void>((resolve) => setTimeout(resolve, waitMs));
}

export async function reserveDevelopmentLogin() {
  if (test.info().config.workers !== 1)
    throw new Error("The shared development login budget requires one worker.");
  // Replacement workers and local reruns can inherit a live server's old bucket.
  // A fresh CI server needs no initial cooldown; reservations then bound each run.
  ready ??=
    test.info().workerIndex > 0 ||
    test.info().config.webServer?.reuseExistingServer !== false
      ? pause(windowMs)
      : Promise.resolve();
  await ready;
  for (;;) {
    const now = performance.now();
    while (attempts.length && attempts[0] <= now - windowMs) attempts.shift();
    if (attempts.length < permits) {
      attempts.push(now);
      return;
    }
    const waitMs = Math.ceil(attempts[0] + windowMs - now) + 1;
    await pause(waitMs);
  }
}
