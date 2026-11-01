import {
  test,
  expect,
  type Page,
  type APIRequestContext,
  type Locator,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";
const orgA = "10000000-0000-0000-0000-000000000010",
  orgOther = "10000000-0000-0000-0000-000000000011";
const literal = '<img src=x onerror="window.templateInjected=true"> 😀';
type Head = { id: string; name: string; version: number };
async function write(
  r: APIRequestContext,
  path: string,
  data: object,
  put = false,
) {
  const token = await (await r.get("/api/v1/csrf")).json();
  const response = await r[put ? "put" : "post"](`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": token.token },
  });
  expect(response.ok(), await response.text()).toBe(true);
  return response.status() === 204 ? undefined : response.json();
}
async function login(page: Page, profile = "Alice Chen") {
  await page.goto("/");
  await reserveDevelopmentLogin();
  await page.getByRole("button", { name: new RegExp(profile) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}
async function selectOrg(page: Page, id: string) {
  const picker = page.getByRole("combobox", {
    name: "ORGANIZATION",
    exact: true,
  });
  await expect(picker).toBeVisible();
  await expect(picker).toBeEnabled();
  for (
    let i = 0;
    i < 64 && !(await picker.locator(`option[value="${id}"]`).count());
    i++
  ) {
    const before = await picker.getByRole("option").count();
    await page
      .getByRole("button", {
        name: "Load more organization choices",
        exact: true,
      })
      .click();
    await expect
      .poll(() => picker.getByRole("option").count())
      .toBeGreaterThan(before);
  }
  await picker.selectOption(id);
}
async function freshOrg(page: Page) {
  const row = await write(page.request, "/organizations", {
    name: `ZZZ Document template browser ${Date.now()}`,
  });
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await selectOrg(page, row.id);
  return row.id as string;
}
async function template(
  r: APIRequestContext,
  org: string,
  name = `Starter ${Date.now()} ${literal}`,
): Promise<Head> {
  return write(r, `/organizations/${org}/document-templates`, {
    name,
    title: "Exact starter title",
    content: `Exact starter body\r\n${literal}`,
  });
}
async function publish(
  r: APIRequestContext,
  org: string,
  t: Head,
  name: string,
  content: string,
  archived = false,
): Promise<Head> {
  const head = await (
    await r.get(`/api/v1/organizations/${org}/document-templates/${t.id}`)
  ).json();
  return write(
    r,
    `/organizations/${org}/document-templates/${t.id}`,
    {
      name,
      title: "Current winner title",
      content,
      expectedVersion: head.version,
      isArchived: archived,
    },
    true,
  );
}
async function asset(r: APIRequestContext, org: string) {
  return write(r, `/organizations/${org}/assets`, {
    name: `Starter asset ${Date.now()} ${Math.random()}`,
    kind: "Server",
  });
}
async function panel(page: Page) {
  await page
    .getByRole("button", { name: "Document templates", exact: true })
    .click();
  const p = page.getByRole("region", {
    name: "Document templates",
    exact: true,
  });
  await expect(
    p.getByRole("button", { name: "Refresh document templates" }),
  ).toBeEnabled();
  return p;
}
async function inspect(p: Locator, name: string) {
  await expect(
    p.getByRole("button", { name: "Refresh document templates" }),
  ).toBeEnabled();
  const rows = p.getByRole("list", {
    name: "Published document templates",
    exact: true,
  });
  for (
    let i = 0;
    i < 32 &&
    !(await rows
      .getByRole("button", { name: `Inspect template ${name}`, exact: true })
      .count());
    i++
  ) {
    const n = await rows.getByRole("listitem").count();
    await p
      .getByRole("button", { name: "More document templates", exact: true })
      .click();
    await expect
      .poll(() => rows.getByRole("listitem").count())
      .toBeGreaterThan(n);
  }
  await rows
    .getByRole("button", { name: `Inspect template ${name}`, exact: true })
    .click();
  await expect(
    p.getByRole("button", { name: "Edit document template", exact: true }),
  ).toBeEnabled();
}
async function chooseAsset(p: Locator, id: string) {
  const picker = p.getByRole("combobox", { name: "Linked asset", exact: true });
  await expect(picker).toBeVisible();
  for (
    let i = 0;
    i < 32 && !(await picker.locator(`option[value="${id}"]`).count());
    i++
  ) {
    const n = await picker.getByRole("option").count();
    await p
      .getByRole("button", { name: "More linked assets", exact: true })
      .click();
    await expect
      .poll(() => picker.getByRole("option").count())
      .toBeGreaterThan(n);
  }
  await picker.selectOption(id);
}
test("publish an authored SOP, inspect immutable history and create a reviewed document with persistent provenance", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    a = await asset(page.request, org);
  const p = await panel(page);
  await p.getByRole("button", { name: "New document template" }).click();
  await p.getByRole("button", { name: "Use SOP outline" }).click();
  await expect(
    p.getByRole("textbox", { name: "Starter content", exact: true }),
  ).toHaveValue(/Verification/);
  const name = `Authored SOP ${Date.now()} ${literal}`;
  await p.getByLabel("Template name", { exact: true }).fill(name);
  await p
    .getByRole("textbox", { name: "Starter content", exact: true })
    .fill(`First authored version\n${literal}`);
  await p
    .getByRole("button", { name: "Publish template version", exact: true })
    .click();
  await inspect(p, name);
  await p
    .getByRole("button", { name: "Edit document template", exact: true })
    .click();
  await p
    .getByRole("textbox", { name: "Starter content", exact: true })
    .fill("Second authored version");
  await p
    .getByRole("button", { name: "Publish template version", exact: true })
    .click();
  await inspect(p, name);
  await p
    .getByRole("button", { name: `Inspect version 1 · ${name}`, exact: true })
    .click();
  await expect(
    p.getByText(`First authored version\n${literal}`, { exact: true }),
  ).toBeVisible();
  expect(await p.locator("img").count()).toBe(0);
  await p
    .getByRole("button", { name: "Create document from template", exact: true })
    .click();
  await chooseAsset(p, a.id);
  await expect(
    p.getByRole("textbox", { name: "Starter content", exact: true }),
  ).toHaveValue("Second authored version");
  await p
    .getByLabel("Starter document title", { exact: true })
    .fill("Reviewed synthetic runbook");
  await p
    .getByRole("textbox", { name: "Starter content", exact: true })
    .fill("Edited final procedure from starter");
  const create = p.getByRole("button", {
    name: "Create reviewed document",
    exact: true,
  });
  await expect(create).toBeDisabled();
  await p
    .getByRole("checkbox", { name: /I reviewed this template version/ })
    .check();
  const response = page.waitForResponse(
    (r) =>
      r.request().method() === "POST" &&
      r.url().includes("/document-templates/") &&
      r.url().endsWith("/documents"),
  );
  await create.click();
  const doc = await (await response).json();
  await expect(
    page.getByRole("heading", {
      name: "Reviewed synthetic runbook",
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByText("Edited final procedure from starter", { exact: true }),
  ).toBeVisible();
  const provenance = page.getByRole("complementary", {
    name: "Document starter provenance",
  });
  await expect(provenance).toContainText("template version 2");
  await expect(provenance).toContainText(name);
  const origin = await (
    await page.request.get(
      `/api/v1/organizations/${org}/documents/${doc.id}/template-origin`,
    )
  ).json();
  expect(origin.templateVersion).toBe(2);
  let releaseOrganizations!: () => void;
  const organizationGate = new Promise<void>((resolve) => {
    releaseOrganizations = resolve;
  });
  let organizationRequestStarted!: () => void;
  const organizationRequest = new Promise<void>((resolve) => {
    organizationRequestStarted = resolve;
  });
  await page.route("**/api/v1/organizations", async (route) => {
    organizationRequestStarted();
    await organizationGate;
    await route.continue();
  });
  await page.reload();
  await organizationRequest;
  const selectReloadedOrganization = selectOrg(page, org);
  await expect(
    page.getByText("Opening your workspace…", { exact: true }),
  ).toBeVisible();
  releaseOrganizations();
  await selectReloadedOrganization;
  await page.unroute("**/api/v1/organizations");
  await page
    .getByRole("region", { name: "Asset inventory" })
    .getByRole("button", { name: new RegExp(a.name) })
    .click();
  await page
    .getByRole("button", { name: /Reviewed synthetic runbook/ })
    .click();
  await expect(
    page.getByRole("complementary", { name: "Document starter provenance" }),
  ).toContainText("template version 2");
  const restoredOrigin = page.getByRole("complementary", {
    name: "Document starter provenance",
  });
  const originRoute = `**/api/v1/organizations/${org}/documents/${doc.id}/template-origin`;
  await page.route(originRoute, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: '{"title":"Synthetic provenance outage"}',
    }),
  );
  await restoredOrigin
    .getByRole("button", { name: "Reload starter provenance", exact: true })
    .click();
  await expect(restoredOrigin).toContainText("Starter provenance unavailable.");
  await expect(restoredOrigin).not.toContainText(name);
  await expect(restoredOrigin).not.toContainText(
    "No recorded template origin.",
  );
  await page.unroute(originRoute);
  await restoredOrigin
    .getByRole("button", { name: "Reload starter provenance", exact: true })
    .click();
  await expect(restoredOrigin).toContainText("template version 2");
});
test("publication conflicts retain drafts through repeated failed reloads and remove stale current-state reviews", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    t = await template(page.request, org);
  const p = await panel(page);
  await inspect(p, t.name);
  const edit = p.getByRole("button", {
    name: "Edit document template",
    exact: true,
  });
  await edit.click();
  await p
    .getByRole("textbox", { name: "Starter content", exact: true })
    .fill("Retained local draft");
  await publish(page.request, org, t, "Winner two", "Winner two body");
  const save = p.getByRole("button", {
    name: "Publish template version",
    exact: true,
  });
  await save.click();
  await expect(save).toBeDisabled();
  const reload = p.getByRole("button", {
    name: "Reload current template",
    exact: true,
  });
  await reload.click();
  const current = p.getByRole("complementary", {
    name: "Reloaded current template",
  });
  await expect(current).toContainText("Winner two body");
  await publish(page.request, org, t, "Winner three", "Winner three body");
  await save.click();
  await expect(save).toBeDisabled();
  await expect(current).toHaveCount(0);
  const source = `**/api/v1/organizations/${org}/document-templates/${t.id}`;
  await page.route(source, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: '{"title":"Synthetic source outage"}',
    }),
  );
  await reload.click();
  await expect(p.getByRole("alert")).toContainText("Synthetic source outage");
  await expect(save).toBeDisabled();
  await expect(current).toHaveCount(0);
  await expect(
    p.getByRole("textbox", { name: "Starter content", exact: true }),
  ).toHaveValue("Retained local draft");
  await page.unroute(source);
  await reload.click();
  await expect(current).toContainText("Winner three body");
  await expect(save).toBeEnabled();
  await save.click();
  await expect(
    p.getByRole("heading", { name: "Document templates", exact: true }),
  ).toBeFocused();
  await inspect(p, t.name);
  await edit.click();
  await p.getByLabel("Template name", { exact: true }).press("Escape");
  await expect(edit).toBeFocused();
});
test("reviewed creation conflicts with archive, keeps final text and requires renewed review after reactivation", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    t = await template(page.request, org),
    a = await asset(page.request, org);
  const p = await panel(page);
  await inspect(p, t.name);
  await p
    .getByRole("button", { name: "Create document from template", exact: true })
    .click();
  await chooseAsset(p, a.id);
  await p
    .getByRole("textbox", { name: "Starter content", exact: true })
    .fill("Retained creation draft");
  const reviewed = p.getByRole("checkbox", {
    name: /I reviewed this template version/,
  });
  await reviewed.check();
  await publish(
    page.request,
    org,
    t,
    "Archived winner",
    "Archived winner body",
    true,
  );
  const create = p.getByRole("button", {
    name: "Create reviewed document",
    exact: true,
  });
  await create.click();
  await expect(create).toBeDisabled();
  const source = `**/api/v1/organizations/${org}/document-templates/${t.id}`;
  await page.route(source, (route) =>
    route.fulfill({
      status: 503,
      body: '{"title":"Synthetic reload outage"}',
      contentType: "application/problem+json",
    }),
  );
  await p
    .getByRole("button", { name: "Reload current template", exact: true })
    .click();
  await expect(p.getByRole("alert")).toContainText("Synthetic reload outage");
  await expect(create).toBeDisabled();
  await page.unroute(source);
  await p
    .getByRole("button", { name: "Reload current template", exact: true })
    .click();
  await expect(
    p.getByRole("complementary", { name: "Reloaded current template" }),
  ).toContainText("Archived");
  await expect(create).toBeDisabled();
  await expect(reviewed).not.toBeChecked();
  await publish(page.request, org, t, "Active winner", "Active winner body");
  await p
    .getByRole("button", { name: "Reload current template", exact: true })
    .click();
  await expect(
    p.getByRole("complementary", { name: "Reloaded current template" }),
  ).toContainText("Active winner body");
  await expect(
    p.getByRole("textbox", { name: "Starter content", exact: true }),
  ).toHaveValue("Retained creation draft");
  await expect(create).toBeDisabled();
  await reviewed.check();
  await create.click();
  await expect(
    page.getByRole("complementary", { name: "Document starter provenance" }),
  ).toContainText("template version 3");
  await expect(
    page.getByText("Retained creation draft", { exact: true }),
  ).toBeVisible();
});
test("bounded template history and asset pages clear on failures, retry from the head and coordinate global busy", async ({
  page,
}) => {
  test.setTimeout(120_000);
  await login(page);
  const org = await freshOrg(page);
  const all: Head[] = [];
  for (let i = 0; i < 55; i++)
    all.push(await template(page.request, org, `Paged starter ${i}`));
  const t = all[0];
  for (let i = 1; i < 55; i++)
    await publish(page.request, org, t, t.name, `History version ${i + 1}`);
  const assetIds: string[] = [];
  for (let i = 0; i < 55; i++)
    assetIds.push((await asset(page.request, org)).id);
  const firstAssets = await (
    await page.request.get(`/api/v1/organizations/${org}/assets`)
  ).json();
  const target = assetIds.find(
    (id) => !firstAssets.items.some((a: { id: string }) => a.id === id),
  )!;
  const p = await panel(page),
    rows = p.getByRole("list", {
      name: "Published document templates",
      exact: true,
    });
  await expect(rows.getByRole("listitem")).toHaveCount(50);
  await page.route(
    `**/api/v1/organizations/${org}/document-templates?**after=*`,
    (route) =>
      route.fulfill({
        status: 503,
        body: '{"title":"Synthetic template page outage"}',
        contentType: "application/problem+json",
      }),
  );
  await p
    .getByRole("button", { name: "More document templates", exact: true })
    .click();
  await expect(rows.getByRole("listitem")).toHaveCount(0);
  await expect(
    p.getByRole("button", { name: "More document templates", exact: true }),
  ).toHaveCount(0);
  await page.unroute(
    `**/api/v1/organizations/${org}/document-templates?**after=*`,
  );
  await p.getByRole("button", { name: "Refresh document templates" }).click();
  await expect(rows.getByRole("listitem")).toHaveCount(50);
  await p
    .getByRole("button", { name: "More document templates", exact: true })
    .click();
  await expect(rows.getByRole("listitem")).toHaveCount(55);
  await inspect(p, t.name);
  const history = p.getByRole("list", {
    name: "Document template history",
    exact: true,
  });
  await expect(history.getByRole("listitem")).toHaveCount(50);
  await p
    .getByRole("button", { name: "More template history", exact: true })
    .click();
  await expect(history.getByRole("listitem")).toHaveCount(55);
  await p
    .getByRole("button", { name: "Create document from template", exact: true })
    .click();
  const picker = p.getByRole("combobox", { name: "Linked asset", exact: true });
  await expect(picker.getByRole("option")).toHaveCount(51);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let started!: () => void;
  const reached = new Promise<void>((resolve) => (started = resolve));
  await page.route(
    `**/api/v1/organizations/${org}/assets?after=*`,
    async (route) => {
      started();
      await gate;
      await route.fulfill({
        status: 503,
        body: '{"title":"Synthetic asset page outage"}',
        contentType: "application/problem+json",
      });
    },
  );
  await p
    .getByRole("button", { name: "More linked assets", exact: true })
    .click();
  await reached;
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole("button", { name: "Document library", exact: true }),
  ).toBeDisabled();
  release();
  await expect(picker.getByRole("option")).toHaveCount(1);
  await page.unroute(`**/api/v1/organizations/${org}/assets?after=*`);
  await p
    .getByRole("button", { name: "Reload linked assets", exact: true })
    .click();
  await expect(picker.getByRole("option")).toHaveCount(51);
  await chooseAsset(p, target);
  await expect(picker.getByRole("option")).toHaveCount(56);
});
test("historical keyboard inspection retains focus through a delayed version read and clears failed content", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    t = await template(page.request, org);
  await publish(page.request, org, t, t.name, "Second historical text");
  const p = await panel(page);
  await inspect(p, t.name);
  const button = p.getByRole("button", {
    name: `Inspect version 1 · ${t.name}`,
    exact: true,
  });
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(
    `**/api/v1/organizations/${org}/document-templates/${t.id}/revisions/1`,
    async (route) => {
      await gate;
      await route.continue();
    },
  );
  await button.focus();
  await button.press("Enter");
  await expect(button).toBeVisible();
  await expect(
    p.getByText("Loading saved template version…", { exact: true }),
  ).toBeVisible();
  await expect(
    p.getByText("Second historical text", { exact: true }),
  ).toHaveCount(0);
  release();
  await expect(
    p.getByRole("heading", { name: "Saved template version 1", exact: true }),
  ).toBeVisible();
  await expect(button).toBeFocused();
  await page.unroute(
    `**/api/v1/organizations/${org}/document-templates/${t.id}/revisions/1`,
  );
  await page.route(
    `**/api/v1/organizations/${org}/document-templates/${t.id}/revisions/2`,
    (route) =>
      route.fulfill({
        status: 503,
        body: '{"title":"Synthetic history outage"}',
        contentType: "application/problem+json",
      }),
  );
  await p
    .getByRole("button", { name: `Inspect version 2 · ${t.name}`, exact: true })
    .click();
  await expect(p.getByRole("alert")).toContainText("Synthetic history outage");
  await expect(
    p.getByRole("list", { name: "Document template history", exact: true }),
  ).toHaveCount(0);
  await expect(
    p.getByText(`Exact starter body\n${literal}`, { exact: true }),
  ).toHaveCount(0);
});
test("Viewer template reads render literal text on mobile and denied refresh clears the organization", async ({
  page,
  playwright,
}) => {
  const editor = await playwright.request.newContext({
    baseURL: "http://127.0.0.1:3000",
  });
  await reserveDevelopmentLogin();
  await write(editor, "/dev/login", { profile: "alice" });
  const t = await template(
    editor,
    orgA,
    `Viewer starter ${Date.now()} ${literal}`,
  );
  await editor.dispose();
  await login(page, "Sam Rivera");
  await selectOrg(page, orgA);
  await page.setViewportSize({ width: 390, height: 844 });
  const p = await panel(page);
  const rows = p.getByRole("list", {
    name: "Published document templates",
    exact: true,
  });
  for (
    let i = 0;
    i < 32 &&
    !(await rows
      .getByRole("button", { name: `Inspect template ${t.name}`, exact: true })
      .count());
    i++
  ) {
    const n = await rows.getByRole("listitem").count();
    await p
      .getByRole("button", { name: "More document templates", exact: true })
      .click();
    await expect
      .poll(() => rows.getByRole("listitem").count())
      .toBeGreaterThan(n);
  }
  await rows
    .getByRole("button", { name: `Inspect template ${t.name}`, exact: true })
    .click();
  await expect(
    p.getByRole("heading", { name: "Saved template version 1", exact: true }),
  ).toBeVisible();
  await expect(
    p.getByRole("button", {
      name: /New document template|Edit document template|Create document from template/,
    }),
  ).toHaveCount(0);
  expect(await p.locator("img,a").count()).toBe(0);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  expect(
    (
      await new AxeBuilder({ page })
        .include('[aria-label="Document templates"]')
        .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await p.screenshot({ path: "test-results/document-templates-mobile.png" });
  await page.route(
    `**/api/v1/organizations/${orgA}/document-templates?**`,
    (route) =>
      route.fulfill({
        status: 403,
        body: '{"title":"Synthetic template denial"}',
        contentType: "application/problem+json",
      }),
  );
  await p.getByRole("button", { name: "Refresh document templates" }).click();
  await expect(p).toHaveCount(0);
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "Synthetic template denial",
  );
  await expect(
    page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .getByRole("option"),
  ).toHaveCount(1);
  await page.unroute(`**/api/v1/organizations/${orgA}/document-templates?**`);
  await page
    .getByRole("button", { name: "Reload organization choices", exact: true })
    .click();
  await expect(
    page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .locator(`option[value="${orgA}"]`),
  ).toHaveCount(1);
});
test("initial template reads cancel across organization switches and late denial cannot clear the new context", async ({
  page,
}) => {
  await login(page);
  await selectOrg(page, orgA);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let reached!: () => void;
  const started = new Promise<void>((resolve) => (reached = resolve));
  await page.route(
    `**/api/v1/organizations/${orgA}/document-templates?**`,
    async (route) => {
      reached();
      await gate;
      await route
        .fulfill({
          status: 403,
          body: '{"title":"Late template denial"}',
          contentType: "application/problem+json",
        })
        .catch(() => {});
    },
  );
  await page
    .getByRole("button", { name: "Document templates", exact: true })
    .click();
  await started;
  const cancelled = page.waitForEvent("requestfailed", (r) =>
    r.url().includes(`/organizations/${orgA}/document-templates?`),
  );
  await selectOrg(page, orgOther);
  await cancelled;
  release();
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toHaveValue(orgOther);
  const p = page.getByRole("region", {
    name: "Document templates",
    exact: true,
  });
  await expect(
    p.getByRole("button", { name: "Refresh document templates" }),
  ).toBeEnabled();
  await expect(
    page.getByText("Late template denial", { exact: true }),
  ).toHaveCount(0);
});
