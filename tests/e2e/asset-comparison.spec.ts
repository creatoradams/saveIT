import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";
const org = "10000000-0000-0000-0000-000000000010",
  base = `/organizations/${org}`;
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
async function placed(page: Page) {
  const name = `Pinned-site-${Date.now()}`;
  const oldName = `Old ${literal} ${"Z".repeat(65)}`;
  const location = await write(page.request, `${base}/locations`, {
    name: oldName,
    details: "",
    email: "",
    phone: "",
    notes: "",
  });
  const asset = await write(page.request, `${base}/assets`, {
    name,
    kind: "Server",
  });
  await write(
    page.request,
    `${base}/assets/${asset.id}`,
    { name, kind: "Server", locationId: location.id, expectedVersion: 1 },
    true,
  );
  await write(
    page.request,
    `${base}/locations/${location.id}`,
    {
      name: "Current data center",
      details: "",
      email: "",
      phone: "",
      notes: "",
      expectedVersion: 1,
    },
    true,
  );
  await write(
    page.request,
    `${base}/assets/${asset.id}/fields`,
    {
      expectedVersion: 2,
      assetTypeId: null,
      assetTypeVersion: null,
      fields: {},
    },
    true,
  );
  return { asset, location, name, oldName };
}
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

test("snapshot location names survive rename and exact restoration while current placement retains its current name", async ({
  page,
}) => {
  await login(page);
  const { asset, location, name, oldName } = await placed(page);
  const panel = await open(page, name);
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await expect(metadata).toContainText("Location: Current data center");
  await panel.getByLabel("From asset revision", { exact: true }).fill("2");
  await panel.getByLabel("To asset revision", { exact: true }).fill("3");
  await panel
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(panel).toContainText("1 changed rows");
  const row = panel
    .getByRole("row")
    .filter({ hasText: "Snapshot location name" });
  await expect(row.getByRole("cell").nth(0)).toContainText(oldName);
  await expect(row.getByRole("cell").nth(1)).toContainText(
    "Current data center",
  );
  await expect(panel.locator("img")).toHaveCount(0);
  await panel.getByRole("button", { name: "Close asset comparison" }).click();
  await metadata
    .locator("summary")
    .filter({ hasText: /^Asset revision 2 ·/ })
    .click();
  await metadata
    .getByRole("button", { name: "Restore asset revision 2…", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "Restore asset revision 2?",
  });
  await expect(dialog).toContainText(oldName);
  await expect(dialog).toContainText(
    "The current location keeps its current name",
  );
  await page.setViewportSize({ width: 390, height: 844 });
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
  await dialog
    .getByLabel("Reason for asset restore", { exact: true })
    .fill("Restore exact historical placement label");
  await dialog
    .getByRole("button", { name: "Create restored asset version" })
    .click();
  await expect(metadata).toContainText("Asset version 4");
  await expect(metadata).toContainText("Location: Current data center");
  const saved = await (
    await page.request.get(`/api/v1${base}/assets/${asset.id}/revisions`)
  ).json();
  expect(saved.items[0].locationName).toBe(oldName);
  expect(saved.items[0].locationId).toBe(location.id);
  expect(
    saved.items.find((r: { version: number }) => r.version === 2).locationName,
  ).toBe(oldName);
  const same = await open(page, name);
  await same.getByLabel("From asset revision", { exact: true }).fill("2");
  await same.getByLabel("To asset revision", { exact: true }).fill("4");
  await same
    .getByRole("button", { name: "Compare asset versions", exact: true })
    .click();
  await expect(same.getByRole("status")).toContainText("No differences");
  await page.screenshot({
    path: "test-results/historical-location-mobile.png",
    fullPage: true,
  });
});

test("legacy unknown names are distinct from unassigned, remain literal for Viewers and clear on failed history refresh", async ({
  page,
  browser,
}) => {
  await login(page);
  const { asset, name } = await placed(page);
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
  });
  const viewer = await context.newPage();
  try {
    await login(viewer, "Sam Rivera");
    // Explicit legacy response fixture: real predecessor migration/restore is tested against PostgreSQL.
    await viewer.route(`**/assets/${asset.id}/revisions`, async (route) => {
      const response = await route.fetch();
      const data = await response.json();
      data.items = data.items.map(
        (r: { version: number; locationName: string | null }) =>
          r.version === 2 ? { ...r, locationName: null } : r,
      );
      await route.fulfill({ response, json: data });
    });
    await viewer.route(`**/assets/${asset.id}/compare?**`, async (route) => {
      const response = await route.fetch();
      const data = await response.json();
      data.rows = data.rows.map((r: { key: string; from: object }) =>
        r.key === "locationName"
          ? {
              ...r,
              from: {
                present: true,
                value: null,
                definition: null,
                unavailable: true,
              },
            }
          : r,
      );
      await route.fulfill({ response, json: data });
    });
    const panel = await open(viewer, name);
    await panel
      .getByRole("button", { name: "Compare asset versions", exact: true })
      .click();
    await expect(
      panel.getByRole("row").filter({ hasText: "Snapshot location name" }),
    ).toContainText("Historical name unavailable");
    await panel.getByRole("button", { name: "Close asset comparison" }).click();
    const metadata = viewer.getByRole("region", {
      name: "Asset placement and history",
    });
    await metadata
      .locator("summary")
      .filter({ hasText: /^Asset revision 2 ·/ })
      .click();
    await metadata
      .locator("summary")
      .filter({ hasText: /^Asset revision 1 ·/ })
      .click();
    await expect(metadata).toContainText(
      "Snapshot location: Historical name unavailable",
    );
    await expect(metadata).toContainText("Snapshot location: Unassigned");
    await expect(
      metadata.getByRole("button", { name: /Restore asset revision/ }),
    ).toHaveCount(0);
    await expect(metadata.locator("img")).toHaveCount(0);
    expect(
      (
        await new AxeBuilder({ page: viewer })
          .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
          .analyze()
      ).violations,
    ).toEqual([]);
    await viewer.unroute(`**/assets/${asset.id}/revisions`);
    await viewer.route(`**/assets/${asset.id}/revisions`, (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "History temporarily unavailable" }),
      }),
    );
    await metadata.getByRole("button", { name: "View asset history" }).click();
    await expect(metadata.getByRole("alert")).toContainText(
      "History temporarily unavailable",
    );
    await expect(
      metadata.getByRole("region", { name: "Asset revisions", exact: true }),
    ).toHaveCount(0);
    await expect(metadata).not.toContainText("Historical name unavailable");
  } finally {
    await context.close();
  }
});
