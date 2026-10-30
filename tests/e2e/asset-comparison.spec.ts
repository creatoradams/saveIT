import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
const org = "10000000-0000-0000-0000-000000000010",
  base = `/organizations/${org}`;
async function login(page: Page, profile = "Alice Chen") {
  await page.goto("/");
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
  const response = await request[put ? "put" : "post"](`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
  expect(response.ok(), await response.text()).toBe(true);
  return response.json();
}
async function open(page: Page, name: string) {
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  const list = page.getByRole("region", { name: "Asset inventory" }),
    target = list.getByRole("button", { name: new RegExp(name) });
  await expect(list.getByRole("list")).toBeVisible();
  for (let i = 0; i < 40 && !(await target.count()); i++) {
    const count = await list.getByRole("listitem").count();
    await list.getByRole("button", { name: "Load more assets" }).click();
    await expect
      .poll(() => list.getByRole("listitem").count())
      .toBeGreaterThan(count);
  }
  await target.click();
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await metadata
    .getByRole("button", { name: "Compare asset revisions", exact: true })
    .click();
  return page.getByRole("region", {
    name: "Asset revision comparison",
    exact: true,
  });
}
const literal = '<img src=x onerror="window.assetComparisonInjected=true">';
const fields = [
  {
    key: "serial",
    label: "Original serial",
    type: "text",
    required: true,
    defaultValue: "Historical default",
  },
  { key: "managed", label: "Managed", type: "boolean", required: true },
  { key: "watts", label: "Watts", type: "number", required: false },
  { key: "empty", label: "Empty note", type: "text", required: false },
  { key: "unset", label: "Unconfigured", type: "text", required: false },
];
async function typed(page: Page) {
  const name = `Comparison-${Date.now()}`;
  const type = await write(page.request, `${base}/asset-types`, {
    name: `Template ${name}`,
    definition: fields,
  });
  const asset = await write(page.request, `${base}/assets`, {
    name,
    kind: "Server",
    assetTypeId: type.id,
    assetTypeVersion: 1,
    fields: { serial: literal, managed: false, watts: 0, empty: "" },
  });
  const changed = fields.map((f) =>
    f.key === "serial"
      ? { ...f, label: "Renamed serial", defaultValue: "New unused default" }
      : f,
  );
  await write(
    page.request,
    `${base}/asset-types/${type.id}`,
    { name: `Second ${name}`, definition: changed, expectedVersion: 1 },
    true,
  );
  await write(
    page.request,
    `${base}/assets/${asset.id}/fields`,
    {
      expectedVersion: 1,
      assetTypeId: type.id,
      assetTypeVersion: 2,
      fields: { serial: "Changed serial", managed: false, watts: 0, empty: "" },
    },
    true,
  );
  await write(
    page.request,
    `${base}/asset-types/${type.id}`,
    {
      name: "Future template label",
      definition: changed.map((f) =>
        f.key === "serial"
          ? { ...f, label: "future-label", defaultValue: "future-default" }
          : f,
      ),
      expectedVersion: 2,
      isArchived: true,
    },
    true,
  );
  return { asset, type, name };
}

test("compare historical schema labels and literal values, including false, zero, empty and unset", async ({
  page,
}) => {
  await login(page);
  const { name } = await typed(page);
  const panel = await open(page, name);
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel).toContainText("2 changed rows");
  await expect(panel).toContainText("Current asset version: 2");
  const serial = panel.getByRole("row").filter({ hasText: "Original serial" });
  await expect(serial).toContainText(literal);
  await expect(serial).toContainText("Renamed serial");
  await expect(serial).toContainText("Definition changed");
  const old = serial.getByRole("cell").nth(0),
    current = serial.getByRole("cell").nth(1);
  await old.getByText("Historical field rules", { exact: true }).click();
  await current.getByText("Historical field rules", { exact: true }).click();
  await expect(old).toContainText("Historical default");
  await expect(current).toContainText("New unused default");
  await expect(panel).not.toContainText("future-label");
  await expect(panel).not.toContainText("future-default");
  await expect(panel.locator("img")).toHaveCount(0);
  expect(
    await page.evaluate(
      () =>
        (window as Window & { assetComparisonInjected?: boolean })
          .assetComparisonInjected,
    ),
  ).toBeUndefined();
  await panel.getByLabel("Show unchanged rows", { exact: true }).check();
  await expect(panel.getByRole("table")).toHaveCount(0);
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  const row = (key: string) =>
    panel.getByRole("row").filter({
      has: page.getByRole("rowheader", {
        name: new RegExp(`^${key} unchanged`),
      }),
    });
  await expect(
    row("managed").getByRole("cell").nth(0).getByText("false", { exact: true }),
  ).toBeVisible();
  await expect(
    row("watts").getByRole("cell").nth(0).getByText("0", { exact: true }),
  ).toBeVisible();
  await expect(row("empty")).toContainText('Empty text ("")');
  await expect(
    row("unset").getByRole("cell").nth(0).getByText("Not set", { exact: true }),
  ).toBeVisible();
  await panel.getByLabel("From asset revision", { exact: true }).fill("2");
  await panel.getByLabel("To asset revision", { exact: true }).fill("1");
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(
    panel.getByRole("region", { name: "From asset snapshot" }),
  ).toContainText("Second ");
  await panel.getByLabel("To asset revision", { exact: true }).fill("2");
  await panel.getByLabel("Show unchanged rows", { exact: true }).uncheck();
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel.getByRole("status")).toContainText("No differences");
});

test("bounded comparison pages clear on failure, retry safely and lock concurrent workspace actions", async ({
  page,
}) => {
  await login(page);
  const name = `PagedComparison-${Date.now()}`;
  const definition = (prefix: string) =>
    Array.from({ length: 20 }, (_, i) => ({
      key: prefix + i.toString().padStart(2, "0"),
      label: `Field ${i}`,
      type: "text",
      required: false,
    }));
  const values = (prefix: string) =>
    Object.fromEntries(
      Array.from({ length: 20 }, (_, i) => [
        prefix + i.toString().padStart(2, "0"),
        `${prefix}-private-${i}`,
      ]),
    );
  const type = await write(page.request, `${base}/asset-types`, {
    name,
    definition: definition("old"),
  });
  const asset = await write(page.request, `${base}/assets`, {
    name,
    kind: "Server",
    assetTypeId: type.id,
    assetTypeVersion: 1,
    fields: values("old"),
  });
  await write(
    page.request,
    `${base}/asset-types/${type.id}`,
    { name, definition: definition("new"), expectedVersion: 1 },
    true,
  );
  await write(
    page.request,
    `${base}/assets/${asset.id}/fields`,
    {
      assetTypeId: type.id,
      assetTypeVersion: 2,
      expectedVersion: 1,
      fields: values("new"),
    },
    true,
  );
  const panel = await open(page, name);
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel).toContainText("1–20 of 41");
  await expect(panel.locator("tbody tr")).toHaveCount(20);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(`**/assets/${asset.id}/compare?*`, async (route) => {
    if (new URL(route.request().url()).searchParams.get("offset") === "20") {
      await gate;
      await route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Synthetic comparison failure" }),
      });
    } else await route.continue();
  });
  await panel
    .getByRole("button", { name: "Next asset changes", exact: true })
    .click();
  try {
    await expect(
      panel.getByText("Loading asset comparison…", { exact: true }),
    ).toBeVisible();
    await expect(panel.locator("tbody tr")).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: "Assets & docs", exact: true }),
    ).toBeDisabled();
    await expect(
      panel.getByRole("button", {
        name: "Compare asset versions",
        exact: true,
      }),
    ).toBeDisabled();
  } finally {
    release();
  }
  await expect(panel.getByRole("alert")).toContainText(
    "Synthetic comparison failure",
  );
  await expect(panel.getByRole("table")).toHaveCount(0);
  await expect(panel).not.toContainText("new-private-");
  await page.unroute(`**/assets/${asset.id}/compare?*`);
  await panel.getByRole("button", { name: "Retry asset comparison" }).click();
  await expect(panel).toContainText("1–20 of 41");
  await panel
    .getByRole("button", { name: "Next asset changes", exact: true })
    .click();
  await expect(panel).toContainText("21–40 of 41");
  await panel
    .getByRole("button", { name: "Next asset changes", exact: true })
    .click();
  await expect(panel).toContainText("41–41 of 41");
  await expect(panel.locator("tbody tr")).toHaveCount(1);
  await expect(
    panel.getByRole("button", { name: "Next asset changes", exact: true }),
  ).toBeDisabled();
  await panel
    .getByRole("button", { name: "Previous asset changes", exact: true })
    .click();
  await expect(panel).toContainText("21–40 of 41");
});

test("Viewer comparison is read only and accessible on mobile, and denied refresh withholds prior content", async ({
  page,
  browser,
}) => {
  await login(page);
  const { name, asset } = await typed(page);
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
  });
  const viewer = await context.newPage();
  await login(viewer, "Sam Rivera");
  const panel = await open(viewer, name);
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel).toContainText(literal);
  await expect(
    viewer.getByRole("button", { name: /Restore asset revision/ }),
  ).toHaveCount(0);
  await expect(panel.getByRole("button", { name: /restore/i })).toHaveCount(0);
  await panel.getByLabel("Show unchanged rows", { exact: true }).check();
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  const table = panel.getByRole("region", {
    name: "Asset comparison table",
    exact: true,
  });
  await table.focus();
  await expect(table).toBeFocused();
  expect(
    await viewer.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  expect(
    (
      await new AxeBuilder({ page: viewer })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await table.scrollIntoViewIfNeeded();
  await viewer.screenshot({ path: "test-results/asset-comparison-mobile.png" });
  await viewer.route(`**/assets/${asset.id}/compare?*`, (route) =>
    route.fulfill({
      status: 403,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Synthetic access removed" }),
    }),
  );
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel.getByRole("alert")).toContainText(
    "Synthetic access removed",
  );
  await expect(panel.getByRole("table")).toHaveCount(0);
  await expect(panel).not.toContainText(literal);
  await expect(
    panel.getByRole("button", { name: "Next asset changes" }),
  ).toHaveCount(0);
  await context.close();
});

test("comparison stays separate from stale restore confirmation and exact restored snapshots compare equal", async ({
  page,
}) => {
  await login(page);
  const name = `RestoreComparison-${Date.now()}`;
  const asset = await write(page.request, `${base}/assets`, {
    name,
    kind: "Network",
  });
  await write(
    page.request,
    `${base}/assets/${asset.id}`,
    {
      name: `Changed ${name}`,
      kind: "Server",
      locationId: null,
      expectedVersion: 1,
    },
    true,
  );
  const panel = await open(page, `Changed ${name}`);
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel).toContainText("Current asset version: 2");
  await write(
    page.request,
    `${base}/assets/${asset.id}`,
    {
      name: `Concurrent ${name}`,
      kind: "Server",
      locationId: null,
      expectedVersion: 2,
    },
    true,
  );
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await metadata.getByText(/Asset revision 1 ·/).click();
  await metadata
    .getByRole("button", { name: "Restore asset revision 1…", exact: true })
    .click();
  const dialog = page.getByRole("dialog");
  const reason = "Confidential comparison restore rationale";
  await dialog.getByLabel("Reason for asset restore").fill(reason);
  await dialog
    .getByRole("button", { name: "Create restored asset version" })
    .click();
  await expect(dialog.getByRole("alert")).toContainText("changed");
  await expect(dialog.getByLabel("Reason for asset restore")).toHaveValue(
    reason,
  );
  await dialog.getByRole("button", { name: "Reload asset details" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(metadata).toContainText("Asset version 3");
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await metadata.getByText(/Asset revision 1 ·/).click();
  await metadata
    .getByRole("button", { name: "Restore asset revision 1…", exact: true })
    .click();
  await dialog.getByLabel("Reason for asset restore").fill(reason);
  await dialog
    .getByRole("button", { name: "Create restored asset version" })
    .click();
  await expect(metadata).toContainText("Asset version 4");
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await metadata
    .getByRole("button", { name: "Compare asset revisions", exact: true })
    .click();
  const equal = page.getByRole("region", {
    name: "Asset revision comparison",
    exact: true,
  });
  await equal.getByLabel("From asset revision", { exact: true }).fill("1");
  await equal
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(equal.getByRole("status")).toContainText("No differences");
  await expect(equal).toContainText("Restored from asset revision 1");
  await expect(equal).not.toContainText(reason);
});
