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
const literal = '<img src=x onerror="window.folderInjected=true"> 😀';
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
  return r.status() === 204 ? undefined : r.json();
}
async function selectOrg(page: Page, id: string) {
  const picker = page.getByRole("combobox", {
    name: "ORGANIZATION",
    exact: true,
  });
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
    name: `ZZZ Folder browser ${Date.now()}`,
  });
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await selectOrg(page, row.id);
  return row.id as string;
}
async function folder(
  request: APIRequestContext,
  org: string,
  name: string,
  parentId: string | null = null,
) {
  return write(request, `/organizations/${org}/folders`, { name, parentId });
}
async function createDocument(
  request: APIRequestContext,
  org: string,
  title = `Library procedure ${Date.now()}`,
) {
  const asset = await write(request, `/organizations/${org}/assets`, {
    name: `Library asset ${Date.now()}`,
    kind: "Server",
  });
  return write(request, `/organizations/${org}/documents`, {
    assetId: asset.id,
    title,
    content: "Exact synthetic library procedure.",
  });
}
async function library(page: Page) {
  await page
    .getByRole("button", { name: "Document library", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Document library",
    exact: true,
  });
  await expect(
    panel.getByRole("button", { name: "Refresh document library" }),
  ).toBeEnabled();
  await expect(
    panel.getByRole("heading", { name: "Library root", exact: true }),
  ).toBeVisible();
  return panel;
}
async function openFolder(panel: Locator, name: string) {
  const list = panel.getByRole("list", { name: "Child folders", exact: true });
  await expect(
    panel.getByRole("button", { name: "Refresh document library" }),
  ).toBeEnabled();
  await expect(list).toBeVisible();
  for (
    let i = 0;
    i < 24 &&
    !(await list
      .getByRole("button", { name: `Open folder ${name}`, exact: true })
      .count());
    i++
  ) {
    const before = await list.getByRole("listitem").count();
    await panel
      .getByRole("button", { name: "More child folders", exact: true })
      .click();
    await expect
      .poll(() => list.getByRole("listitem").count())
      .toBeGreaterThan(before);
  }
  await list
    .getByRole("button", { name: `Open folder ${name}`, exact: true })
    .click();
  await expect(panel.getByRole("heading", { name, exact: true })).toBeVisible();
}
async function choose(picker: Locator, name: string) {
  await picker
    .getByRole("button", { name: `Choose folder ${name}`, exact: true })
    .click();
  await expect(
    picker.getByText(`Selected destination: ${name}`, { exact: true }),
  ).toBeVisible();
}
async function uiFolder(panel: Locator, name: string) {
  await panel.getByRole("button", { name: "New folder", exact: true }).click();
  const form = panel.getByRole("form", { name: "Edit folder" });
  await form.getByLabel("Folder name", { exact: true }).fill(name);
  await expect(
    form.getByRole("button", { name: "Save folder", exact: true }),
  ).toBeEnabled();
  await form.getByRole("button", { name: "Save folder", exact: true }).click();
  await expect(form).toHaveCount(0);
  await expect(
    panel.getByRole("button", { name: `Open folder ${name}`, exact: true }),
  ).toBeVisible();
}

test("create nested literal folders, file and open a real linked document, and persist after reopening", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page);
  const doc = await createDocument(page.request, org);
  const name = `Runbooks ${literal}`;
  const panel = await library(page);
  await uiFolder(panel, name);
  await openFolder(panel, name);
  await uiFolder(panel, "Synthetic branch");
  await panel
    .getByRole("navigation", { name: "Library path" })
    .getByRole("button", { name: "Library root", exact: true })
    .click();
  await panel
    .getByRole("button", { name: `Move document ${doc.title}`, exact: true })
    .click();
  const form = panel.getByRole("form", { name: "Move document", exact: true }),
    picker = form.getByRole("region", {
      name: "Destination folder",
      exact: true,
    });
  await choose(picker, name);
  await choose(picker, "Synthetic branch");
  await form
    .getByRole("button", { name: "Save document location", exact: true })
    .click();
  await expect(form).toHaveCount(0);
  await openFolder(panel, name);
  await openFolder(panel, "Synthetic branch");
  await expect(
    panel.getByRole("list", { name: "Library documents" }),
  ).toContainText(doc.title);
  await expect(panel.locator("img,a")).toHaveCount(0);
  expect(
    await page.evaluate(
      () => (window as Window & { folderInjected?: boolean }).folderInjected,
    ),
  ).toBeUndefined();
  await panel
    .getByRole("button", { name: `Open document ${doc.title}`, exact: true })
    .click();
  await expect(page.getByRole("article")).toContainText(
    "Exact synthetic library procedure.",
  );
  await expect(
    page.getByRole("heading", { name: new RegExp("Library asset") }),
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await selectOrg(page, org);
  const reopened = await library(page);
  await openFolder(reopened, name);
  await openFolder(reopened, "Synthetic branch");
  await expect(
    reopened.getByRole("list", { name: "Library documents" }),
  ).toContainText(doc.title);
  const saved = await (
    await page.request.get(`/api/v1/organizations/${org}/documents/${doc.id}`)
  ).json();
  expect(saved.version).toBe(1);
  expect(saved.assetId).toBe(doc.assetId);
});

test("folder cycles retain choices, conflicts stay blocked through failed reload and show current saved values before retry", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    root = await folder(page.request, org, "Original root"),
    child = await folder(page.request, org, "Child destination", root.id),
    other = await folder(page.request, org, "Other parent");
  const panel = await library(page);
  await openFolder(panel, root.name);
  const opener = panel.getByRole("button", {
    name: "Rename or move this folder",
  });
  await opener.click();
  const form = panel.getByRole("form", { name: "Edit folder" }),
    picker = form.getByRole("region", {
      name: "Destination folder",
      exact: true,
    });
  await choose(picker, root.name);
  await choose(picker, child.name);
  await form.getByLabel("Folder name").fill("Retained draft");
  await form.getByRole("button", { name: "Save folder", exact: true }).click();
  await expect(panel.getByRole("alert")).toContainText("without cycles");
  await expect(form.getByLabel("Folder name")).toHaveValue("Retained draft");
  await picker
    .getByRole("navigation", { name: "Destination path" })
    .getByRole("button", { name: "Library root", exact: true })
    .click();
  await expect(
    picker.getByText("Selected destination: Library root (unfiled)", {
      exact: true,
    }),
  ).toBeVisible();
  await write(
    page.request,
    `/organizations/${org}/folders/${root.id}`,
    { name: "Concurrent winner", parentId: other.id, expectedVersion: 1 },
    true,
  );
  await form.getByRole("button", { name: "Save folder", exact: true }).click();
  await expect(panel.getByRole("alert")).toContainText("changed");
  await expect(
    form.getByRole("button", { name: "Save folder", exact: true }),
  ).toBeDisabled();
  await page.route(`**/organizations/${org}/folders/${root.id}`, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Current folder unavailable" }),
    }),
  );
  await form.getByRole("button", { name: "Reload current folder" }).click();
  await expect(panel.getByRole("alert")).toContainText(
    "Current folder unavailable",
  );
  await expect(
    form.getByRole("button", { name: "Save folder", exact: true }),
  ).toBeDisabled();
  await expect(form.getByLabel("Folder name")).toHaveValue("Retained draft");
  await page.unroute(`**/organizations/${org}/folders/${root.id}`);
  await form.getByRole("button", { name: "Reload current folder" }).click();
  const review = form.getByRole("complementary", {
    name: "Reloaded current state",
  });
  await expect(review).toContainText("Concurrent winner");
  await expect(review).toContainText(other.name);
  await expect(form.getByLabel("Folder name")).toHaveValue("Retained draft");
  await expect(
    form.getByRole("button", { name: "Save folder", exact: true }),
  ).toBeEnabled();
  await form.getByRole("button", { name: "Save folder", exact: true }).click();
  await expect(form).toHaveCount(0);
  await expect(
    panel.getByRole("heading", { name: "Retained draft", exact: true }),
  ).toBeVisible();
  await expect(
    panel.getByRole("heading", { name: "Document library", exact: true }),
  ).toBeFocused();
  await panel
    .getByRole("button", { name: "Rename or move this folder" })
    .click();
  await panel
    .getByRole("form", { name: "Edit folder" })
    .getByLabel("Folder name")
    .press("Escape");
  await expect(
    panel.getByRole("button", { name: "Rename or move this folder" }),
  ).toBeFocused();
});

test("document placement conflict retains destination, shows the saved winner and preserves independent content versions", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    a = await folder(page.request, org, "Chosen destination"),
    b = await folder(page.request, org, "Concurrent destination"),
    doc = await createDocument(page.request, org);
  const panel = await library(page);
  await panel
    .getByRole("button", { name: `Move document ${doc.title}`, exact: true })
    .click();
  const form = panel.getByRole("form", { name: "Move document", exact: true }),
    picker = form.getByRole("region", {
      name: "Destination folder",
      exact: true,
    });
  await choose(picker, a.name);
  await write(
    page.request,
    `/organizations/${org}/documents/${doc.id}/placement`,
    { folderId: b.id, expectedVersion: 0 },
    true,
  );
  await form.getByRole("button", { name: "Save document location" }).click();
  await expect(panel.getByRole("alert")).toContainText("changed");
  await page.route(
    `**/organizations/${org}/documents/${doc.id}/placement`,
    (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Current placement unavailable" }),
      }),
  );
  await form.getByRole("button", { name: "Reload current placement" }).click();
  await expect(panel.getByRole("alert")).toContainText(
    "Current placement unavailable",
  );
  await expect(
    form.getByRole("button", { name: "Save document location" }),
  ).toBeDisabled();
  await expect(picker).toContainText(`Selected destination: ${a.name}`);
  await page.unroute(`**/organizations/${org}/documents/${doc.id}/placement`);
  await form.getByRole("button", { name: "Reload current placement" }).click();
  await expect(
    form.getByRole("complementary", { name: "Reloaded current state" }),
  ).toContainText(b.name);
  await expect(picker).toContainText(`Selected destination: ${a.name}`);
  await form.getByRole("button", { name: "Save document location" }).click();
  await expect(form).toHaveCount(0);
  await openFolder(panel, a.name);
  await expect(
    panel.getByRole("list", { name: "Library documents" }),
  ).toContainText("Content revision 1 · Placement version 2");
  const placed = await (
    await page.request.get(
      `/api/v1/organizations/${org}/documents/${doc.id}/placement`,
    )
  ).json();
  expect(placed.folderId).toBe(a.id);
  expect(placed.version).toBe(2);
  await write(
    page.request,
    `/organizations/${org}/documents/${doc.id}`,
    { title: doc.title, content: "Revised after filing", expectedVersion: 1 },
    true,
  );
  await write(
    page.request,
    `/organizations/${org}/documents/${doc.id}/restore`,
    {
      sourceVersion: 1,
      expectedVersion: 2,
      reason: "Restore exact approved procedure",
    },
  );
  const restoredPlacement = await (
    await page.request.get(
      `/api/v1/organizations/${org}/documents/${doc.id}/placement`,
    )
  ).json();
  expect(restoredPlacement).toEqual(placed);
  await panel.getByRole("button", { name: "Refresh document library" }).click();
  await expect(
    panel.getByRole("list", { name: "Library documents" }),
  ).toContainText("Content revision 3 · Placement version 2");
});

test("bounded document and destination pages clear on failure, retry from the head and coordinate workspace busy controls", async ({
  page,
}) => {
  await login(page);
  const org = await freshOrg(page),
    parent = await folder(page.request, org, "Paged parent");
  for (let i = 0; i < 55; i++)
    await folder(page.request, org, `Child ${i}`, parent.id);
  for (let i = 0; i < 55; i++)
    await createDocument(page.request, org, `Paged document ${i}`);
  const panel = await library(page),
    list = panel.getByRole("list", { name: "Library documents" });
  await expect(list.getByRole("listitem")).toHaveCount(50);
  await page.route(
    `**/organizations/${org}/library/documents?after=**`,
    (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Library continuation unavailable" }),
      }),
  );
  await panel.getByRole("button", { name: "More library documents" }).click();
  await expect(panel.getByRole("alert")).toContainText(
    "Library continuation unavailable",
  );
  await expect(list).toHaveCount(0);
  await expect(panel.getByRole("list", { name: "Child folders" })).toHaveCount(
    0,
  );
  await expect(
    panel.getByRole("button", { name: "More library documents" }),
  ).toHaveCount(0);
  await page.unroute(`**/organizations/${org}/library/documents?after=**`);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(
    `**/organizations/${org}/library/documents`,
    async (route) => {
      const response = await route.fetch();
      await gate;
      await route.fulfill({ response });
    },
  );
  await panel.getByRole("button", { name: "Retry document library" }).click();
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole("button", { name: "Assets & docs", exact: true }),
  ).toBeDisabled();
  release();
  await expect(list.getByRole("listitem")).toHaveCount(50);
  await page.unroute(`**/organizations/${org}/library/documents`);
  await panel.getByRole("button", { name: "More library documents" }).click();
  await expect(list.getByRole("listitem")).toHaveCount(55);
  await expect(
    panel.getByRole("button", { name: "More library documents" }),
  ).toHaveCount(0);
  await list
    .getByRole("button", { name: /^Move document/ })
    .first()
    .click();
  const form = panel.getByRole("form", { name: "Move document", exact: true }),
    picker = form.getByRole("region", {
      name: "Destination folder",
      exact: true,
    });
  await choose(picker, parent.name);
  const choices = picker.getByRole("list", { name: "Destination choices" });
  await expect(choices.getByRole("listitem")).toHaveCount(50);
  await picker
    .getByRole("button", { name: "More destination folders" })
    .click();
  await expect(choices.getByRole("listitem")).toHaveCount(55);
  await choices.getByRole("button").last().click();
  await expect(
    form.getByRole("button", { name: "Save document location" }),
  ).toBeEnabled();
  await form.getByRole("button", { name: "Save document location" }).click();
  await expect(form).toHaveCount(0);
  await openFolder(panel, parent.name);
  const children = panel.getByRole("list", { name: "Child folders" });
  await expect(children.getByRole("listitem")).toHaveCount(50);
  await panel.getByRole("button", { name: "More child folders" }).click();
  await expect(children.getByRole("listitem")).toHaveCount(55);
});

test("Viewer library is literal and mobile accessible, and denied refresh clears context with explicit recovery", async ({
  page,
  playwright,
}) => {
  const editor = await playwright.request.newContext({
    baseURL: "http://127.0.0.1:3000",
  });
  await reserveDevelopmentLogin();
  await write(editor, "/dev/login", { profile: "alice" });
  const name = `Viewer library ${Date.now()} ${literal}`,
    f = await folder(editor, orgA, name),
    doc = await createDocument(editor, orgA, `Viewer procedure ${Date.now()}`);
  await write(
    editor,
    `/organizations/${orgA}/documents/${doc.id}/placement`,
    { folderId: f.id, expectedVersion: 0 },
    true,
  );
  await editor.dispose();
  await login(page, "Sam Rivera");
  await selectOrg(page, orgA);
  await page.setViewportSize({ width: 390, height: 844 });
  const panel = await library(page);
  await openFolder(panel, name);
  await expect(
    panel.getByRole("list", { name: "Library documents" }),
  ).toContainText(doc.title);
  await expect(
    panel.getByRole("button", {
      name: /New folder|Rename or move|Move document/,
    }),
  ).toHaveCount(0);
  await expect(panel.locator("img,a")).toHaveCount(0);
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
  await panel.screenshot({ path: "test-results/document-library-mobile.png" });
  await page.route(
    `**/organizations/${orgA}/library/documents?folderId=${f.id}`,
    (route) =>
      route.fulfill({
        status: 403,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Library access removed" }),
      }),
  );
  await panel.getByRole("button", { name: "Refresh document library" }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "Library access removed",
  );
  await expect(panel).toHaveCount(0);
  await expect(page.getByRole("main")).not.toContainText(doc.title);
  await expect(page.getByRole("main")).not.toContainText(name);
  await expect(
    page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .getByRole("option"),
  ).toHaveCount(1);
  await page.unroute(
    `**/organizations/${orgA}/library/documents?folderId=${f.id}`,
  );
  await page
    .getByRole("button", { name: "Reload organization choices", exact: true })
    .click();
  await expect(panel).toBeVisible();
  await openFolder(panel, name);
  await expect(
    panel.getByRole("list", { name: "Library documents" }),
  ).toContainText(doc.title);
});

test("switching organization aborts initial library reads and a late denial cannot clear the new selection", async ({
  page,
}) => {
  await login(page);
  await selectOrg(page, orgA);
  const oldName = `Old library context ${Date.now()}`,
    newName = `New library context ${Date.now()}`;
  await folder(page.request, orgA, oldName);
  await folder(page.request, orgOther, newName);
  let release!: () => void, observed!: () => void, complete!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve)),
    started = new Promise<void>((resolve) => (observed = resolve)),
    finished = new Promise<void>((resolve) => (complete = resolve));
  await page.route(
    `**/organizations/${orgA}/library/documents`,
    async (route) => {
      observed();
      await gate;
      try {
        await route.fulfill({
          status: 403,
          contentType: "application/problem+json",
          body: JSON.stringify({ title: "Late old library denial" }),
        });
      } catch {
        /* The owned read may already be cancelled. */
      } finally {
        complete();
      }
    },
  );
  await page
    .getByRole("button", { name: "Document library", exact: true })
    .click();
  await started;
  const cancelled = page.waitForEvent("requestfailed", (request) =>
    request.url().endsWith(`/organizations/${orgA}/library/documents`),
  );
  await selectOrg(page, orgOther);
  await cancelled;
  const panel = page.getByRole("region", {
    name: "Document library",
    exact: true,
  });
  await expect(
    panel.getByRole("button", { name: `Open folder ${newName}`, exact: true }),
  ).toBeVisible();
  release();
  await finished;
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toHaveValue(orgOther);
  await expect(panel).not.toContainText(oldName);
  await expect(page.getByRole("main")).not.toContainText(
    "Late old library denial",
  );
});
