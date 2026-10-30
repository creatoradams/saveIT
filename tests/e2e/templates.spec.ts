import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";
const org = "10000000-0000-0000-0000-000000000010";
const base = `/organizations/${org}`;
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
  return request[put ? "put" : "post"](`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
}
async function openAsset(page: Page, name: string) {
  // API setup may insert before an already-loaded cursor; start a fresh live list.
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Assets & docs", exact: true })
    .click();
  const button = page.getByRole("button", { name: new RegExp(name) });
  const inventory = page.getByRole("region", { name: "Asset inventory" });
  await expect(inventory.getByRole("list")).toBeVisible();
  for (let i = 0; i < 30 && !(await button.count()); i++) {
    const count = await inventory.getByRole("listitem").count();
    await inventory.getByRole("button", { name: "Load more assets" }).click();
    await expect
      .poll(() => inventory.getByRole("listitem").count())
      .toBeGreaterThan(count);
  }
  await button.click();
}
const def = [
  {
    key: "serial",
    label: "Serial",
    type: "text",
    required: true,
    defaultValue: "Synthetic default",
  },
  { key: "managed", label: "Managed", type: "boolean", required: true },
];

test("template controls honor a pending workspace operation", async ({
  page,
}) => {
  await page.route("**/api/v1/organizations", async (route) => {
    if (route.request().method() !== "GET") return route.continue();
    const response = await route.fetch();
    if (response.status() !== 200) return route.fulfill({ response });
    const body = await response.json();
    return route.fulfill({
      response,
      json: { ...body, nextCursor: "busy-fixture" },
    });
  });
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(
    "**/api/v1/organizations?after=busy-fixture",
    async (route) => {
      await gate;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ items: [], nextCursor: null }),
      });
    },
  );
  await login(page);
  await page
    .getByRole("button", { name: "Asset templates", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Asset templates",
    exact: true,
  });
  await panel
    .getByRole("button", { name: "New template", exact: true })
    .click();
  await panel
    .getByLabel("Template name", { exact: true })
    .fill("Retained template draft");
  const pending = page.waitForRequest((r) =>
    r.url().includes("after=busy-fixture"),
  );
  await page
    .getByRole("button", { name: "Load more organization choices" })
    .click();
  await pending;
  try {
    await expect(
      panel.getByRole("button", { name: "Publish template version" }),
    ).toBeDisabled();
    await expect(
      panel.getByRole("button", { name: "New template", exact: true }),
    ).toBeDisabled();
    await expect(
      panel.getByLabel("Template name", { exact: true }),
    ).toBeDisabled();
  } finally {
    release();
  }
  await expect(
    panel.getByLabel("Template name", { exact: true }),
  ).toBeEnabled();
  await expect(panel.getByLabel("Template name", { exact: true })).toHaveValue(
    "Retained template draft",
  );
});
async function setup(page: Page) {
  const name = `Typed-${Date.now()}`;
  const tr = await write(page.request, `${base}/asset-types`, {
    name,
    definition: def,
  });
  expect(tr.status()).toBe(201);
  const type = await tr.json();
  const ar = await write(page.request, `${base}/assets`, {
    name,
    kind: "Server",
    assetTypeId: type.id,
    assetTypeVersion: 1,
    fields: { serial: "Original", managed: false },
  });
  expect(ar.status()).toBe(201);
  return { type, asset: await ar.json(), name };
}

test("publish a template, create typed inventory, upgrade explicitly and restore exact custom values", async ({
  page,
}) => {
  await login(page);
  const unique = Date.now();
  const name = `Appliance ${unique}`,
    assetName = `Typed appliance ${unique}`;
  await page
    .getByRole("button", { name: "Asset templates", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Asset templates",
    exact: true,
  });
  await panel
    .getByRole("button", { name: "New template", exact: true })
    .click();
  const editor = panel.getByRole("form", { name: "Template editor" });
  await editor.getByLabel("Template name", { exact: true }).fill(name);
  const first = editor.getByRole("group", { name: "Field 1", exact: true });
  await first.getByLabel("Field key", { exact: true }).fill("serial");
  await first.getByLabel("Field label", { exact: true }).fill("Serial");
  await first.getByLabel("Required field", { exact: true }).check();
  const marker = '<img src=x onerror="window.templateInjected=true">';
  await first
    .getByLabel("Default value (optional)", { exact: true })
    .fill(marker);
  await editor.getByRole("button", { name: "Add field", exact: true }).click();
  const second = editor.getByRole("group", { name: "Field 2", exact: true });
  await second.getByLabel("Field key", { exact: true }).fill("managed");
  await second.getByLabel("Field label", { exact: true }).fill("Managed");
  await second
    .getByRole("combobox", { name: "Field type", exact: true })
    .selectOption("boolean");
  await second.getByLabel("Required field", { exact: true }).check();
  const published = page.waitForResponse(
    (r) => r.url().endsWith("/asset-types") && r.request().method() === "POST",
  );
  await editor
    .getByRole("button", { name: "Publish template version" })
    .click();
  const type = await (await published).json();
  await expect(panel.getByRole("status")).toContainText("version 1");
  await panel
    .getByRole("button", { name: `Open template ${name}`, exact: true })
    .click();
  await panel
    .getByRole("button", { name: "Create asset from template", exact: true })
    .click();
  const create = panel.getByRole("form", { name: "Create typed asset" });
  await create.getByLabel("Asset name", { exact: true }).fill(assetName);
  await expect(
    create.getByLabel("Serial (required)", { exact: true }),
  ).toHaveValue(marker);
  await create
    .getByRole("combobox", { name: "Managed (required)", exact: true })
    .selectOption("false");
  await create
    .getByRole("button", { name: "Create typed asset", exact: true })
    .click();
  const fields = page.getByRole("region", { name: "Asset custom fields" });
  await fields.getByRole("button", { name: "View custom fields" }).click();
  await expect(fields).toContainText(marker);
  await expect(fields.locator("img")).toHaveCount(0);
  await expect(fields).toContainText("false");
  await fields.getByRole("button", { name: "Edit custom fields" }).click();
  await fields
    .getByLabel("Serial (required)", { exact: true })
    .fill("Revised serial");
  await fields.getByRole("button", { name: "Save custom fields" }).click();
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await expect(metadata).toContainText("Asset version 2");
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await metadata.getByText(/Asset revision 1 ·/).click();
  await metadata
    .getByRole("button", { name: "Restore asset revision 1…", exact: true })
    .click();
  const dialog = page.getByRole("dialog");
  await dialog
    .getByLabel("Reason for asset restore")
    .fill("Recover the original typed snapshot");
  await dialog
    .getByRole("button", { name: "Create restored asset version" })
    .click();
  await expect(metadata).toContainText("Asset version 3");
  const evolved = [
    { key: "serial", label: "Serial", type: "text", required: true },
    { key: "managed", label: "Managed", type: "boolean", required: true },
    {
      key: "rack",
      label: "Rack",
      type: "text",
      required: true,
      defaultValue: "Do not apply on upgrade",
    },
  ];
  expect(
    (
      await write(
        page.request,
        `${base}/asset-types/${type.id}`,
        { name, definition: evolved, expectedVersion: 1 },
        true,
      )
    ).status(),
  ).toBe(200);
  await fields.getByRole("button", { name: "View custom fields" }).click();
  await expect(fields).toContainText("schema v1");
  await expect(fields).toContainText(marker);
  await fields.getByRole("button", { name: "Edit custom fields" }).click();
  await fields
    .getByRole("combobox", { name: "Assign or upgrade template", exact: true })
    .selectOption(type.id);
  await fields
    .getByLabel(
      "Review this schema change; remove keys absent from the selected schema.",
    )
    .check();
  await fields
    .getByRole("button", { name: "Review selected template version" })
    .click();
  await expect(
    fields.getByLabel("Rack (required)", { exact: true }),
  ).toHaveValue("");
  await fields.getByLabel("Rack (required)", { exact: true }).fill("R12");
  await fields.getByRole("button", { name: "Save custom fields" }).click();
  await expect(metadata).toContainText("Asset version 4");
  await fields.getByRole("button", { name: "View custom fields" }).click();
  await expect(fields).toContainText("schema v2");
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
});

test("field conflicts retain drafts after failed reload and real-server request size is bounded", async ({
  page,
}) => {
  await login(page);
  const { type, asset, name } = await setup(page);
  await openAsset(page, name);
  const fields = page.getByRole("region", { name: "Asset custom fields" });
  await fields.getByRole("button", { name: "View custom fields" }).click();
  await fields.getByRole("button", { name: "Edit custom fields" }).click();
  await fields
    .getByLabel("Serial (required)", { exact: true })
    .fill("Unsaved draft");
  expect(
    (
      await write(
        page.request,
        `${base}/assets/${asset.id}/fields`,
        {
          expectedVersion: 1,
          assetTypeId: type.id,
          assetTypeVersion: 1,
          fields: { serial: "Concurrent winner", managed: true },
        },
        true,
      )
    ).status(),
  ).toBe(200);
  await fields.getByRole("button", { name: "Save custom fields" }).click();
  await expect(fields.getByRole("alert")).toContainText("changed");
  await fields.getByRole("button", { name: "Cancel field edit" }).click();
  await fields.getByRole("button", { name: "Edit custom fields" }).click();
  await expect(
    fields.getByRole("button", { name: "Save custom fields" }),
  ).toBeDisabled();
  await expect(
    fields.getByRole("button", { name: "Reload custom fields" }),
  ).toBeVisible();
  await fields
    .getByLabel("Serial (required)", { exact: true })
    .fill("Unsaved draft");
  await page.route(`**/assets/${asset.id}/fields`, (route) =>
    route.request().method() === "GET"
      ? route.fulfill({
          status: 503,
          contentType: "application/problem+json",
          body: JSON.stringify({ title: "Synthetic field reload failure" }),
        })
      : route.continue(),
  );
  await fields.getByRole("button", { name: "Reload custom fields" }).click();
  await expect(fields.getByRole("alert")).toContainText(
    "Synthetic field reload failure",
  );
  await expect(
    fields.getByLabel("Serial (required)", { exact: true }),
  ).toHaveValue("Unsaved draft");
  await page.unroute(`**/assets/${asset.id}/fields`);
  await fields.getByRole("button", { name: "Reload custom fields" }).click();
  await fields.getByRole("button", { name: "View custom fields" }).click();
  await expect(fields).toContainText("Concurrent winner");
  const large = await write(page.request, `${base}/asset-types`, {
    name: "Oversized",
    definition: [],
    padding: "x".repeat(1_100_000),
  });
  expect(large.status()).toBe(413);
});

test("template archive, immutable history, mobile layout and Viewer controls", async ({
  page,
  browser,
}) => {
  await login(page);
  const { type, asset, name } = await setup(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page
    .getByRole("button", { name: "Asset templates", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Asset templates",
    exact: true,
  });
  let open = panel.getByRole("button", {
    name: `Open template ${name}`,
    exact: true,
  });
  const templateList = panel.locator(".template-list");
  await expect(templateList).toBeVisible();
  for (let i = 0; i < 30 && !(await open.count()); i++) {
    const previous = await templateList.getByRole("listitem").count();
    await panel.getByRole("button", { name: "Load more templates" }).click();
    await expect
      .poll(() => templateList.getByRole("listitem").count())
      .toBeGreaterThan(previous);
  }
  await open.click();
  await panel
    .getByRole("button", { name: "View template history", exact: true })
    .click();
  await panel
    .getByRole("button", { name: "Inspect template version 1" })
    .click();
  await expect(panel.locator("pre")).toContainText("Synthetic default");
  await panel
    .getByRole("button", { name: "Edit template", exact: true })
    .click();
  await panel
    .getByLabel("Archive template (existing pins remain usable)")
    .check();
  await panel.getByRole("button", { name: "Publish template version" }).click();
  await panel.getByLabel("Include archived templates").check();
  await panel
    .getByRole("button", { name: `Open template ${name}`, exact: true })
    .click();
  await expect(
    panel.getByRole("button", { name: "Create asset from template" }),
  ).toHaveCount(0);
  await panel
    .getByRole("button", { name: "Edit template", exact: true })
    .click();
  await panel
    .getByLabel("Archive template (existing pins remain usable)")
    .uncheck();
  await panel.getByRole("button", { name: "Publish template version" }).click();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await page.screenshot({
    path: "test-results/templates-mobile.png",
    fullPage: true,
  });
  const context = await browser.newContext();
  const viewer = await context.newPage();
  await login(viewer, "Sam Rivera");
  await viewer
    .getByRole("button", { name: "Asset templates", exact: true })
    .click();
  await expect(
    viewer.getByRole("button", { name: "New template", exact: true }),
  ).toHaveCount(0);
  await openAsset(viewer, name);
  const vf = viewer.getByRole("region", { name: "Asset custom fields" });
  await vf.getByRole("button", { name: "View custom fields" }).click();
  await expect(vf).toContainText("Original");
  await expect(
    vf.getByRole("button", { name: "Edit custom fields" }),
  ).toHaveCount(0);
  expect(
    (
      await write(
        viewer.request,
        `${base}/assets/${asset.id}/fields`,
        {
          expectedVersion: 1,
          assetTypeId: type.id,
          assetTypeVersion: 1,
          fields: { serial: "No", managed: true },
        },
        true,
      )
    ).status(),
  ).toBe(403);
  await context.close();
});

test("template list retry resets failed filter cursors and page merges keep one created row", async ({
  page,
}) => {
  await login(page);
  const { type } = await setup(page);
  let created: typeof type | null = null,
    failFilter = false;
  // Deterministic page fixture exercises the cursor/merge recovery branch; records and writes are real API data.
  await page.route(`**/organizations/${org}/asset-types?*`, async (route) => {
    const url = new URL(route.request().url());
    if (route.request().method() !== "GET") return route.continue();
    if (
      failFilter &&
      url.searchParams.get("includeArchived") === "true" &&
      !url.searchParams.has("after")
    )
      return route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Synthetic template list failure" }),
      });
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(
        url.searchParams.has("after")
          ? { items: created ? [created] : [], nextCursor: null }
          : {
              items: [type],
              nextCursor: "00000000-0000-0000-0000-000000000001",
            },
      ),
    });
  });
  await page
    .getByRole("button", { name: "Asset templates", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Asset templates",
    exact: true,
  });
  await expect(
    panel.getByRole("button", { name: "Load more templates" }),
  ).toBeVisible();
  failFilter = true;
  await panel.getByLabel("Include archived templates").check();
  await expect(panel.getByRole("alert")).toContainText(
    "Synthetic template list failure",
  );
  await expect(
    panel.getByRole("button", { name: "Load more templates" }),
  ).toHaveCount(0);
  failFilter = false;
  await panel.getByRole("button", { name: "Reload template list" }).click();
  await expect(
    panel.getByRole("button", { name: "Load more templates" }),
  ).toBeVisible();
  await panel
    .getByRole("button", { name: "New template", exact: true })
    .click();
  const editor = panel.getByRole("form", { name: "Template editor" });
  const name = `Merged template ${Date.now()}`;
  await editor.getByLabel("Template name", { exact: true }).fill(name);
  await editor.getByLabel("Field key", { exact: true }).fill("tag");
  await editor.getByLabel("Field label", { exact: true }).fill("Tag");
  const response = page.waitForResponse(
    (r) => r.url().endsWith("/asset-types") && r.request().method() === "POST",
  );
  await editor
    .getByRole("button", { name: "Publish template version" })
    .click();
  created = await (await response).json();
  await expect(panel.getByRole("status")).toContainText("version 1");
  await panel.getByRole("button", { name: "Load more templates" }).click();
  await expect(
    panel.getByRole("button", { name: `Open template ${name}`, exact: true }),
  ).toHaveCount(1);
});
