import { test, expect } from "@playwright/test";
import { execFileSync } from "node:child_process";
const compose = [
  "compose",
  "-p",
  "saveit-release",
  "-f",
  "tests/production/compose.yml",
];

test("production images use real HTTPS OIDC and PKCE, scoped persistence and protected keys across restart", async ({
  page,
}) => {
  const failures: string[] = [];
  page.on("console", (m) => {
    if (
      m.type() === "error" &&
      /Content Security Policy|Refused to/.test(m.text())
    )
      failures.push(m.text());
  });
  await page.goto("/");
  await expect(
    page.getByRole("button", { name: "Sign in with Okta" }),
  ).toBeVisible();
  expect(await (await page.request.get("/api/v1/auth/options")).json()).toEqual(
    { development: false, okta: true },
  );
  expect((await page.request.get("/api/v1/dev/profiles")).status()).toBe(404);
  await page.getByRole("button", { name: "Sign in with Okta" }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  const session = await (await page.request.get("/api/v1/session")).json();
  expect(session.tenantId).toBe("30000000-0000-0000-0000-000000000001");
  const csrf = await (await page.request.get("/api/v1/csrf")).json();
  const root = "/api/v1/organizations/30000000-0000-0000-0000-000000000010";
  const created = await page.request.post(root + "/assets", {
    headers: { "X-CSRF-TOKEN": csrf.token },
    data: { name: "Packaged persistent asset", kind: "Server" },
  });
  expect(created.status()).toBe(201);
  expect(
    (
      await page.request.get(
        "/api/v1/organizations/20000000-0000-0000-0000-000000000010/assets",
        { headers: { "X-Tenant-Id": "20000000-0000-0000-0000-000000000001" } },
      )
    ).status(),
  ).toBe(404);
  const before = await page.context().cookies();
  const cookie = before.find((x) => x.name === "SaveIT.Session");
  expect(cookie?.secure).toBe(true);
  expect(cookie?.httpOnly).toBe(true);
  execFileSync("bash", ["scripts/rotate-test-key.sh"], { stdio: "pipe" });
  execFileSync("docker", [...compose, "restart", "api"], { stdio: "pipe" });
  await expect
    .poll(async () => (await page.request.get("/health/ready")).status(), {
      timeout: 30000,
    })
    .toBe(200);
  const after = await page.request.get("/api/v1/session");
  expect(after.status()).toBe(200);
  expect((await after.json()).tenantId).toBe(session.tenantId);
  expect(
    (await (await page.request.get(root + "/assets")).json()).items,
  ).toEqual(
    expect.arrayContaining([
      expect.objectContaining({ name: "Packaged persistent asset" }),
    ]),
  );
  expect(
    (await (await page.request.get("https://idp.localhost:8443/status")).json())
      .pkce,
  ).toBe(true);
  expect(failures).toEqual([]);
});

test("production CSP nonces are unique, script unsafe-inline is absent and readiness follows API failure", async ({
  request,
}) => {
  const a = await request.get("/"),
    b = await request.get("/");
  expect(a.status()).toBe(200);
  expect(a.headers()["cache-control"]).toContain("no-store");
  const policy = a.headers()["content-security-policy"];
  const script = policy
    .split(";")
    .find((x) => x.trim().startsWith("script-src"))!;
  expect(script).toContain("'nonce-");
  expect(script).not.toContain("'unsafe-inline'");
  expect(script).not.toContain("'unsafe-eval'");
  expect(policy).not.toBe(b.headers()["content-security-policy"]);
  expect(a.headers()["strict-transport-security"]).toContain("max-age");
  execFileSync("docker", [...compose, "stop", "api"], { stdio: "pipe" });
  try {
    await expect
      .poll(async () => (await request.get("/health/ready")).status(), {
        timeout: 15000,
      })
      .toBe(503);
  } finally {
    execFileSync("docker", [...compose, "start", "api"], { stdio: "pipe" });
  }
  await expect
    .poll(async () => (await request.get("/health/ready")).status(), {
      timeout: 30000,
    })
    .toBe(200);
});
