import {
  test,
  expect,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const org = "10000000-0000-0000-0000-000000000010";
async function post(request: APIRequestContext, path: string, data: object) {
  const csrf = await (await request.get("/api/v1/csrf")).json();
  const response = await request.post(`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
  expect(response.ok(), await response.text()).toBe(true);
  return response.status() === 204 ? null : response.json();
}
async function login(page: Page, name = "Alice Chen") {
  await page.goto("/");
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}
async function assets(request: APIRequestContext) {
  const unique = Date.now();
  const source = await post(request, `/organizations/${org}/assets`, {
    name: `Dependency source ${unique}`,
    kind: "Network",
  });
  const target = await post(request, `/organizations/${org}/assets`, {
    name: `Dependency target ${unique}`,
    kind: "Server",
  });
  return { source, target };
}
async function open(page: Page, name: string) {
  const inventory = page.getByRole("region", { name: "Asset inventory" });
  const button = inventory.getByRole("button", { name: new RegExp(name) });
  await expect(inventory.getByRole("list")).toBeVisible();
  for (let i = 0; i < 30 && (await button.count()) === 0; i++) {
    const previous = await inventory.getByRole("listitem").count();
    await page
      .getByRole("button", { name: "Load more assets", exact: true })
      .click();
    await expect
      .poll(() => inventory.getByRole("listitem").count())
      .toBeGreaterThan(previous);
  }
  await button.click();
  await expect(page.getByRole("heading", { name, exact: true })).toBeVisible();
}
async function createInUi(page: Page, targetId: string, kind = "DependsOn") {
  const region = page.getByRole("region", {
    name: "Asset relationships",
    exact: true,
  });
  await region.getByRole("button", { name: "Add relationship" }).click();
  const form = region.getByRole("form", { name: "Create asset relationship" });
  const select = form.getByRole("combobox", { name: "Linked asset" });
  for (
    let i = 0;
    i < 30 &&
    (await select.locator(`option[value="${targetId}"]`).count()) === 0;
    i++
  ) {
    const previous = await select.locator("option").count();
    await form.getByRole("button", { name: "Load more link targets" }).click();
    await expect
      .poll(() => select.locator("option").count())
      .toBeGreaterThan(previous);
  }
  await select.selectOption(targetId);
  await form
    .getByRole("combobox", { name: "Relationship type" })
    .selectOption(kind);
  await form
    .getByRole("button", { name: "Create relationship", exact: true })
    .click();
  await expect(region.getByRole("status")).toContainText(
    "Relationship created",
  );
  return region;
}

test("create a directed link, navigate both ways, persist it, and remove only the link with audit", async ({
  page,
}) => {
  await login(page);
  const { source, target } = await assets(page.request);
  await post(page.request, `/organizations/${org}/documents`, {
    assetId: source.id,
    title: "Relationship preservation guide",
    content: "Keep the source and its documents when removing a link.",
  });
  await page.reload();
  await open(page, source.name);
  const region = await createInUi(page, target.id);
  await expect(
    region.getByRole("list", { name: "Linked assets" }),
  ).toContainText("Depends on");
  await region
    .getByRole("button", {
      name: `Open linked asset ${target.name}`,
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("heading", { name: target.name, exact: true }),
  ).toBeVisible();
  await expect(region.getByRole("list")).toContainText("Required by");
  await region
    .getByRole("button", {
      name: `Open linked asset ${source.name}`,
      exact: true,
    })
    .click();
  await page.reload();
  await open(page, source.name);
  await expect(region.getByRole("list")).toContainText(target.name);
  await page
    .getByRole("combobox", { name: "ORGANIZATION", exact: true })
    .selectOption({ label: "Harbor Clinic" });
  await expect(region).toHaveCount(0);
  await expect(
    page.getByRole("button", {
      name: `Open linked asset ${target.name}`,
      exact: true,
    }),
  ).toHaveCount(0);
  await page
    .getByRole("combobox", { name: "ORGANIZATION", exact: true })
    .selectOption({ label: "Acme Manufacturing" });
  await open(page, source.name);
  const remove = region.getByRole("button", {
    name: `Remove depends on relationship with ${target.name}`,
    exact: true,
  });
  await remove.click();
  const dialog = page.getByRole("dialog", { name: "Remove relationship?" });
  await expect(dialog).toContainText(
    "Both assets and their documents will remain",
  );
  await page.keyboard.press("Escape");
  await expect(dialog).not.toBeVisible();
  await expect(remove).toBeFocused();
  await remove.click();
  await dialog
    .getByRole("button", { name: "Remove relationship", exact: true })
    .click();
  await expect(region.getByRole("status")).toContainText(
    "Relationship removed",
  );
  await expect(region.getByRole("list")).toHaveCount(0);
  await page
    .getByRole("button", { name: /Relationship preservation guide/ })
    .click();
  await expect(page.getByRole("article")).toContainText(
    "Keep the source and its documents",
  );
  expect(
    (
      await page.request.get(`/api/v1/organizations/${org}/assets/${target.id}`)
    ).status(),
  ).toBe(200);
  await page.getByRole("button", { name: "Audit history" }).click();
  await expect(page.getByRole("table")).toContainText(
    "AssetRelationship.Created",
  );
  await expect(page.getByRole("table")).toContainText(
    "AssetRelationship.Removed",
  );
});

test("a viewer follows incoming containment without mutation controls", async ({
  page,
  playwright,
}) => {
  const editor = await playwright.request.newContext({
    baseURL: "http://127.0.0.1:3000",
  });
  let endpoints;
  try {
    await post(editor, "/dev/login", { profile: "alice" });
    endpoints = await assets(editor);
    await post(
      editor,
      `/organizations/${org}/assets/${endpoints.source.id}/relationships`,
      { targetAssetId: endpoints.target.id, kind: "Contains" },
    );
  } finally {
    await editor.dispose();
  }
  await login(page, "Sam Rivera");
  await open(page, endpoints.target.name);
  const region = page.getByRole("region", {
    name: "Asset relationships",
    exact: true,
  });
  await expect(region.getByRole("list")).toContainText("Part of");
  await expect(
    region.getByRole("button", { name: /Add relationship|Remove/ }),
  ).toHaveCount(0);
  await region
    .getByRole("button", {
      name: `Open linked asset ${endpoints.source.name}`,
      exact: true,
    })
    .click();
  await expect(region.getByRole("list")).toContainText("Contains");
});

test("duplicate and concurrent removal conflicts offer reload without changing assets", async ({
  page,
}) => {
  await login(page);
  const { source, target } = await assets(page.request);
  const edge = await post(
    page.request,
    `/organizations/${org}/assets/${source.id}/relationships`,
    { targetAssetId: target.id, kind: "DependsOn" },
  );
  await page.reload();
  await open(page, source.name);
  const region = page.getByRole("region", {
    name: "Asset relationships",
    exact: true,
  });
  await region.getByRole("button", { name: "Add relationship" }).click();
  const form = region.getByRole("form", { name: "Create asset relationship" });
  const select = form.getByRole("combobox", { name: "Linked asset" });
  for (
    let i = 0;
    i < 30 &&
    (await select.locator(`option[value="${target.id}"]`).count()) === 0;
    i++
  ) {
    const previous = await select.locator("option").count();
    await form.getByRole("button", { name: "Load more link targets" }).click();
    await expect
      .poll(() => select.locator("option").count())
      .toBeGreaterThan(previous);
  }
  await select.selectOption(target.id);
  await form
    .getByRole("button", { name: "Create relationship", exact: true })
    .click();
  await expect(region.getByRole("alert")).toContainText(
    "already exists or changed",
  );
  await expect(
    form.getByRole("combobox", { name: "Linked asset" }),
  ).toHaveValue(target.id);
  await form.getByRole("button", { name: "Cancel relationship" }).click();
  await region.getByRole("button", { name: /^Remove depends on/ }).click();
  await post(
    page.request,
    `/organizations/${org}/assets/${target.id}/relationships/${edge.id}/remove`,
    { expectedVersion: 1 },
  );
  const dialog = page.getByRole("dialog");
  await dialog
    .getByRole("button", { name: "Remove relationship", exact: true })
    .click();
  await expect(dialog.getByRole("alert")).toContainText(
    "already exists or changed",
  );
  await expect(
    dialog.getByRole("button", { name: "Remove relationship", exact: true }),
  ).toBeDisabled();
  await dialog.getByRole("button", { name: "Reload relationships" }).click();
  await expect(region).toContainText("No relationships yet");
  await expect(
    page.getByRole("heading", { name: source.name, exact: true }),
  ).toBeVisible();
});

test("relationship errors retry, labels render as text, and forms and confirmation are accessible on mobile", async ({
  page,
}) => {
  await login(page);
  const { source } = await assets(page.request);
  const title = `<img src=x onerror="window.linkInjected=true"> ${Date.now()}`;
  const target = await post(page.request, `/organizations/${org}/assets`, {
    name: title,
    kind: "Synthetic",
  });
  await post(
    page.request,
    `/organizations/${org}/assets/${source.id}/relationships`,
    { targetAssetId: target.id, kind: "Contains" },
  );
  await page.route(`**/assets/${source.id}/relationships`, (route) =>
    route.request().method() === "GET"
      ? route.fulfill({
          status: 503,
          contentType: "application/problem+json",
          body: JSON.stringify({ title: "Synthetic relationship outage" }),
        })
      : route.continue(),
  );
  await page.reload();
  await open(page, source.name);
  const region = page.getByRole("region", {
    name: "Asset relationships",
    exact: true,
  });
  await expect(region.getByRole("alert")).toContainText(
    "Synthetic relationship outage",
  );
  await expect(region.getByRole("list")).toHaveCount(0);
  await page.unroute(`**/assets/${source.id}/relationships`);
  await region.getByRole("button", { name: "Reload relationships" }).click();
  await expect(region.getByRole("list")).toContainText(title);
  await expect(region.locator("img")).toHaveCount(0);
  expect(
    await page.evaluate(
      () => (window as Window & { linkInjected?: boolean }).linkInjected,
    ),
  ).toBeUndefined();
  await region.getByRole("button", { name: "Add relationship" }).click();
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await region.getByRole("button", { name: "Cancel relationship" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await region.getByRole("button", { name: /^Remove contains/ }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: "test-results/saveit-relationships-mobile.png",
  });
  await page.getByRole("button", { name: "Keep relationship" }).click();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await region.scrollIntoViewIfNeeded();
  await page.screenshot({ path: "test-results/saveit-relationships.png" });
});
