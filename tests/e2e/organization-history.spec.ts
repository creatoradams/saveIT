import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";

const org = "10000000-0000-0000-0000-000000000010",
  other = "10000000-0000-0000-0000-000000000011";
const literal = '<img src=x onerror="window.organizationHistoryInjected=true">';
async function login(page: Page, profile = "Alice Chen") {
  await page.goto("/");
  await reserveDevelopmentLogin();
  await page.getByRole("button", { name: new RegExp(profile) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}
async function write(
  request: APIRequestContext,
  path: string,
  data: object,
  put = false,
) {
  const csrf = await (await request.get("/api/v1/csrf")).json();
  const r = await request[put ? "put" : "post"](`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
  expect(r.ok(), await r.text()).toBe(true);
  return r.json();
}
async function select(page: Page, id: string) {
  const picker = page.getByRole("combobox", {
    name: "ORGANIZATION",
    exact: true,
  });
  for (
    let i = 0;
    i < 40 && !(await picker.locator(`option[value="${id}"]`).count());
    i++
  ) {
    const count = await picker.getByRole("option").count();
    await page
      .getByRole("button", {
        name: "Load more organization choices",
        exact: true,
      })
      .click();
    await expect
      .poll(() => picker.getByRole("option").count())
      .toBeGreaterThan(count);
  }
  await picker.selectOption(id);
}
async function history(page: Page) {
  await page
    .getByRole("button", { name: "Organization history", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Organization name history",
    exact: true,
  });
  await expect(panel.getByRole("listitem").first()).toBeVisible();
  return panel;
}

test("administrator create and rename retain exact literal names and persist across reopening", async ({
  page,
}) => {
  await login(page);
  await page
    .getByRole("button", { name: "Manage organizations", exact: true })
    .click();
  const admin = page.getByRole("region", {
    name: "Organization administration",
  });
  const original = `ZZZ ${literal} ${Date.now()}`,
    current = `ZZZ Renamed history ${Date.now()}`;
  await admin
    .getByRole("button", { name: "New organization", exact: true })
    .click();
  await admin.getByLabel("Organization name", { exact: true }).fill(original);
  await admin
    .getByRole("button", { name: "Create organization", exact: true })
    .click();
  await expect(admin.getByRole("status")).toContainText("Organization created");
  const id = await page
    .getByRole("combobox", { name: "ORGANIZATION", exact: true })
    .inputValue();
  await admin
    .getByRole("button", {
      name: `Rename organization ${original}`,
      exact: true,
    })
    .click();
  await admin.getByLabel("Organization name", { exact: true }).fill(current);
  await admin
    .getByRole("button", { name: "Save organization name", exact: true })
    .click();
  await expect(admin.getByRole("status")).toContainText("version 2");
  const panel = await history(page);
  const list = panel.getByRole("list", { name: "Organization revisions" });
  await expect(list.getByRole("listitem")).toHaveCount(2);
  await expect(list.getByRole("listitem").nth(0)).toContainText(current);
  await expect(list.getByRole("listitem").nth(0)).toContainText(
    "Renamed · alice",
  );
  await expect(list.getByRole("listitem").nth(1)).toContainText(original);
  await expect(list.getByRole("listitem").nth(1)).toContainText(
    "Created · alice",
  );
  await expect(panel.locator("img, a")).toHaveCount(0);
  expect(
    await page.evaluate(
      () =>
        (window as Window & { organizationHistoryInjected?: boolean })
          .organizationHistoryInjected,
    ),
  ).toBeUndefined();
  await expect(
    panel.getByRole("button", { name: /Restore|Rename/ }),
  ).toHaveCount(0);
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await select(page, id);
  const reopened = await history(page);
  await expect(reopened.getByRole("listitem")).toHaveCount(2);
  await expect(reopened).toContainText(original);
});

test("bounded history paging clears on failure, retries from the head and locks concurrent workspace actions", async ({
  page,
}) => {
  await login(page);
  const row = await write(page.request, "/organizations", {
    name: `ZZZ Paged history ${Date.now()}`,
  });
  for (let version = 1; version < 56; version++)
    await write(
      page.request,
      `/organizations/${row.id}`,
      { name: `ZZZ Paged ${row.id} v${version + 1}`, expectedVersion: version },
      true,
    );
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await select(page, row.id);
  const panel = await history(page);
  await expect(panel.getByRole("listitem")).toHaveCount(50);
  await expect(panel.getByRole("listitem").first()).toContainText(
    "Organization revision 56",
  );
  await page.route(`**/organizations/${row.id}/revisions?**`, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({
        title: "Organization history source unavailable",
      }),
    }),
  );
  await panel
    .getByRole("button", { name: "Older organization revisions" })
    .click();
  await expect(panel.getByRole("alert")).toContainText(
    "Organization history source unavailable",
  );
  await expect(panel.getByRole("listitem")).toHaveCount(0);
  await expect(
    panel.getByRole("button", { name: "Older organization revisions" }),
  ).toHaveCount(0);
  await page.unroute(`**/organizations/${row.id}/revisions?**`);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(`**/organizations/${row.id}/revisions`, async (route) => {
    const response = await route.fetch();
    await gate;
    await route.fulfill({ response });
  });
  await panel
    .getByRole("button", { name: "Retry organization history" })
    .click();
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole("button", { name: "Assets & docs", exact: true }),
  ).toBeDisabled();
  release();
  await expect(panel.getByRole("listitem")).toHaveCount(50);
  await page.unroute(`**/organizations/${row.id}/revisions`);
  await panel
    .getByRole("button", { name: "Older organization revisions" })
    .click();
  await expect(panel.getByRole("listitem")).toHaveCount(56);
  await expect(panel.getByRole("listitem").last()).toContainText(
    "Organization revision 1",
  );
  await expect(
    panel.getByRole("button", { name: "Older organization revisions" }),
  ).toHaveCount(0);
});

test("Viewer history is accessible on mobile and denied refresh clears organization context with explicit reload recovery", async ({
  page,
}) => {
  await login(page, "Sam Rivera");
  await page.setViewportSize({ width: 390, height: 844 });
  await select(page, org);
  const panel = await history(page);
  await expect(panel).toContainText("Acme Manufacturing");
  await expect(
    page.getByRole("button", { name: "Manage organizations", exact: true }),
  ).toHaveCount(0);
  await expect(
    panel.getByRole("button", { name: /Rename|Restore/ }),
  ).toHaveCount(0);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await panel.screenshot({
    path: "test-results/organization-history-mobile.png",
  });
  await page.route(`**/organizations/${org}/revisions`, (route) =>
    route.fulfill({
      status: 403,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Organization history access removed" }),
    }),
  );
  await panel
    .getByRole("button", { name: "Refresh organization history" })
    .click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "Organization history access removed",
  );
  await expect(
    page.getByRole("region", {
      name: "Organization name history",
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .getByRole("option"),
  ).toHaveCount(1);
  await expect(page.getByRole("main")).not.toContainText("Acme Manufacturing");
  await page.unroute(`**/organizations/${org}/revisions`);
  await page
    .getByRole("button", { name: "Reload organization choices", exact: true })
    .click();
  await expect(
    page.getByRole("region", {
      name: "Organization name history",
      exact: true,
    }),
  ).toContainText("Acme Manufacturing");
});

test("switching organization cancels an initial history request and a late denial cannot clear the new context", async ({
  page,
}) => {
  await login(page);
  await select(page, org);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  let observed!: () => void;
  const started = new Promise<void>((resolve) => {
    observed = resolve;
  });
  let complete!: () => void;
  const finished = new Promise<void>((resolve) => {
    complete = resolve;
  });
  await page.route(`**/organizations/${org}/revisions`, async (route) => {
    observed();
    await gate;
    try {
      await route.fulfill({
        status: 403,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Late old organization denial" }),
      });
    } catch {
      /* The browser may already have cancelled the owned request. */
    } finally {
      complete();
    }
  });
  await page
    .getByRole("button", { name: "Organization history", exact: true })
    .click();
  await started;
  const cancelled = page.waitForEvent("requestfailed", (request) =>
    request.url().endsWith(`/organizations/${org}/revisions`),
  );
  await select(page, other);
  await cancelled;
  const panel = page.getByRole("region", {
    name: "Organization name history",
    exact: true,
  });
  await expect(panel).toContainText("Harbor Clinic");
  release();
  await finished;
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toHaveValue(other);
  await expect(panel).toContainText("Harbor Clinic");
  await expect(page.getByRole("main")).not.toContainText(
    "Late old organization denial",
  );
  await expect(panel).not.toContainText("Acme Manufacturing");
});
