import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";

const org = "10000000-0000-0000-0000-000000000010";
const literal = '<img src=x onerror="window.directoryComparisonInjected=true">';
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
function input(
  resource: string,
  name: string,
  version = 0,
  changed = false,
  archived = false,
) {
  return {
    name,
    details: changed ? "" : "Synthetic address / department",
    email:
      resource === "contacts"
        ? changed
          ? "new@example.invalid"
          : "old@example.invalid"
        : "",
    phone: resource === "contacts" ? (changed ? "" : "+1 555 0100") : "",
    notes: changed ? `${literal}\nExact 😀\n` : `${literal}\r\nExact 😀\n`,
    expectedVersion: version,
    isArchived: archived,
  };
}
async function seed(page: Page, resource: string) {
  const original = input(resource, `Original ${literal} ${Date.now()}`),
    root = `/organizations/${org}/${resource}`;
  const row = await write(page.request, root, original),
    path = `${root}/${row.id}`,
    name = `Current ${resource} ${row.id}`;
  await write(
    page.request,
    path,
    input(resource, `Archived ${resource} ${row.id}`, 1, true, true),
    true,
  );
  const current = await write(
    page.request,
    path,
    input(resource, name, 2, true),
    true,
  );
  return { original, current, name, path, id: row.id };
}
async function open(page: Page, resource: string, name: string) {
  await page
    .getByRole("button", { name: "Locations & contacts", exact: true })
    .click();
  const directory = page.getByRole("region", {
    name: "Locations and contacts",
    exact: true,
  });
  await directory
    .getByRole("combobox", { name: "Directory", exact: true })
    .selectOption(resource);
  const list = directory.getByRole("list", {
    name: resource === "contacts" ? "Contact records" : "Location records",
  });
  await expect(list.getByRole("listitem").first()).toBeVisible();
  const target = list.getByRole("button", {
    name: `View ${resource === "contacts" ? "contact" : "location"} history ${name}`,
    exact: true,
  });
  for (let i = 0; i < 40 && !(await target.count()); i++) {
    const count = await list.getByRole("listitem").count();
    await directory
      .getByRole("button", { name: `Load more ${resource}` })
      .click();
    await expect
      .poll(() => list.getByRole("listitem").count())
      .toBeGreaterThan(count);
  }
  await target.click();
  const history = directory.getByRole("region", {
    name: "Directory revision history",
    exact: true,
  });
  await expect(
    history.getByRole("button", {
      name: "Review directory revision 3",
      exact: true,
    }),
  ).toBeVisible();
  return { directory, history };
}
async function compare(
  history: ReturnType<Page["getByRole"]>,
  from = "1",
  to = "2",
) {
  await history
    .getByRole("button", { name: "Compare directory revisions", exact: true })
    .click();
  const panel = history.getByRole("region", {
    name: "Directory revision comparison",
    exact: true,
  });
  await panel.getByLabel("From directory revision", { exact: true }).fill(from);
  await panel.getByLabel("To directory revision", { exact: true }).fill(to);
  await panel
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(panel.getByRole("table")).toBeVisible();
  return panel;
}

test("location comparison shows exact literal fields, empty text, line endings and lifecycle, then real restore compares equal", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, "locations");
  const { history } = await open(page, "locations", data.name);
  const panel = await compare(history);
  await expect(panel).toContainText("4 changed fields");
  await expect(panel).toContainText(literal);
  await expect(panel).toContainText('Empty text ("")');
  await expect(panel).toContainText("Line endings differ");
  const lifecycle = panel.getByRole("row").filter({ hasText: "Lifecycle" });
  await expect(lifecycle.getByRole("cell").nth(0)).toHaveText("Active");
  await expect(lifecycle.getByRole("cell").nth(1)).toHaveText("Archived");
  await expect(panel.locator("img, a")).toHaveCount(0);
  expect(
    await page.evaluate(
      () =>
        (window as Window & { directoryComparisonInjected?: boolean })
          .directoryComparisonInjected,
    ),
  ).toBeUndefined();
  await panel.getByLabel("From directory revision", { exact: true }).fill("2");
  await expect(panel.getByRole("table")).toHaveCount(0);
  await panel.getByLabel("To directory revision", { exact: true }).fill("1");
  await panel
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(lifecycle.getByRole("cell").nth(0)).toHaveText("Archived");
  await panel
    .getByRole("button", { name: "Close directory comparison" })
    .click();
  await expect(
    history.getByRole("button", {
      name: "Compare directory revisions",
      exact: true,
    }),
  ).toBeFocused();
  await history
    .getByRole("button", { name: "Review directory revision 1", exact: true })
    .click();
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "Confirm location restoration",
  });
  const reason = "Private comparison recovery reason";
  await dialog.getByLabel("Reason for directory restoration").fill(reason);
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(history).toContainText("Current version 4");
  await history
    .getByRole("button", { name: "Compare directory revisions", exact: true })
    .click();
  const equal = history.getByRole("region", {
    name: "Directory revision comparison",
    exact: true,
  });
  await equal.getByLabel("From directory revision", { exact: true }).fill("1");
  await equal
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(equal).toContainText(
    "No differences in recorded fields or lifecycle",
  );
  await expect(equal).toContainText("Restored from directory revision 1");
  await expect(equal).not.toContainText(reason);
});

test("contact comparison pages clear on failure and retry from the first page while shared busy state locks the workspace", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, "contacts");
  const { directory, history } = await open(page, "contacts", data.name);
  const panel = await compare(history);
  await expect(panel).toContainText("6 changed fields");
  await expect(panel).toContainText("old@example.invalid");
  await expect(panel.getByRole("row")).toHaveCount(5);
  await page.route(`**/contacts/${data.id}/compare?**`, async (route) => {
    if (new URL(route.request().url()).searchParams.get("offset") === "4")
      await route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Comparison source unavailable" }),
      });
    else await route.continue();
  });
  await panel
    .getByRole("button", { name: "Next directory differences" })
    .click();
  await expect(panel.getByRole("alert")).toContainText(
    "Comparison source unavailable",
  );
  await expect(panel.getByRole("table")).toHaveCount(0);
  await expect(panel).not.toContainText("changed fields");
  await expect(
    panel.getByRole("button", { name: /directory differences/ }),
  ).toHaveCount(0);
  await page.unroute(`**/contacts/${data.id}/compare?**`);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(`**/contacts/${data.id}/compare?**`, async (route) => {
    const response = await route.fetch();
    await gate;
    await route.fulfill({ response });
  });
  await panel
    .getByRole("button", { name: "Retry directory comparison" })
    .click();
  await expect(
    directory.getByRole("combobox", { name: "Directory", exact: true }),
  ).toBeDisabled();
  await expect(
    history.getByRole("button", { name: "Close directory history" }),
  ).toBeDisabled();
  await expect(
    panel.getByRole("button", { name: "Close directory comparison" }),
  ).toBeDisabled();
  release();
  await expect(panel.getByRole("table")).toBeVisible();
  await page.unroute(`**/contacts/${data.id}/compare?**`);
  await expect(panel).toContainText("old@example.invalid");
  await panel
    .getByRole("button", { name: "Next directory differences" })
    .click();
  await expect(panel).toContainText("Line endings differ");
  await expect(panel.getByRole("row")).toHaveCount(3);
  await panel
    .getByRole("button", { name: "Previous directory differences" })
    .click();
  await expect(panel).toContainText("old@example.invalid");
  await panel
    .getByLabel("Show unchanged directory fields", { exact: true })
    .check();
  await expect(panel.getByRole("table")).toHaveCount(0);
  await panel.getByLabel("From directory revision", { exact: true }).fill("2");
  await panel
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(panel).toContainText("0 changed fields");
  await expect(panel).toContainText("Showing 6 total fields");
});

test("Viewer comparison renders personal fields literally on mobile and clears the whole directory after denied page access", async ({
  page,
  browser,
}) => {
  await login(page);
  const data = await seed(page, "contacts");
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
  });
  const viewer = await context.newPage();
  try {
    await login(viewer, "Sam Rivera");
    const { directory, history } = await open(viewer, "contacts", data.name);
    const panel = await compare(history);
    await expect(panel).toContainText("old@example.invalid");
    await expect(panel.locator("img, a")).toHaveCount(0);
    await expect(
      history.getByRole("button", { name: /Restore directory revision/ }),
    ).toHaveCount(0);
    await panel
      .getByRole("button", { name: "Next directory differences" })
      .click();
    await expect(panel).toContainText(literal);
    await expect(panel).toContainText("Line endings differ");
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
    await panel.screenshot({
      path: "test-results/directory-comparison-mobile.png",
    });
    await viewer.route(`**/contacts/${data.id}/compare?**`, (route) =>
      route.fulfill({
        status: 403,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Directory access removed" }),
      }),
    );
    await panel
      .getByRole("button", { name: "Previous directory differences" })
      .click();
    await expect(directory.getByRole("alert")).toContainText(
      "Directory access removed",
    );
    await expect(
      directory.getByRole("region", {
        name: "Directory revision comparison",
        exact: true,
      }),
    ).toHaveCount(0);
    await expect(directory).not.toContainText(data.name);
    await expect(directory).not.toContainText("old@example.invalid");
    await expect(
      directory.getByRole("combobox", { name: "Directory", exact: true }),
    ).toBeEnabled();
  } finally {
    await context.close();
  }
});

test("comparison current version stays informational and a stale restore still requires reload and renewed confirmation", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, "contacts");
  const { history } = await open(page, "contacts", data.name);
  await history
    .getByRole("button", { name: "Review directory revision 1", exact: true })
    .click();
  const panel = await compare(history);
  await write(page.request, data.path, input("contacts", data.name, 3), true);
  await panel
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(panel).toContainText("Observed current directory version: 4");
  await expect(panel).toContainText("The current record has changed");
  await panel
    .getByRole("button", { name: "Close directory comparison" })
    .click();
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "Confirm contact restoration",
  });
  const reason = "Retained stale comparison reason";
  await dialog.getByLabel("Reason for directory restoration").fill(reason);
  const sent = page.waitForRequest(
    (r) => r.method() === "POST" && r.url().endsWith(`${data.id}/restore`),
  );
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  expect((await sent).postDataJSON().expectedVersion).toBe(3);
  await expect(dialog.getByRole("alert")).toContainText("This record changed");
  await expect(
    dialog.getByLabel("Reason for directory restoration"),
  ).toHaveValue(reason);
  await dialog.getByRole("button", { name: "Reload current contact" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(history).toContainText("Current version 4");
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  await expect(
    dialog.getByLabel("Reason for directory restoration"),
  ).toHaveValue(reason);
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(history).toContainText("Current version 5");
  await history
    .getByRole("button", { name: "Compare directory revisions", exact: true })
    .click();
  const equal = history.getByRole("region", {
    name: "Directory revision comparison",
    exact: true,
  });
  await equal.getByLabel("From directory revision", { exact: true }).fill("1");
  await equal
    .getByRole("button", { name: "Compare directory versions", exact: true })
    .click();
  await expect(equal).toContainText(
    "No differences in recorded fields or lifecycle",
  );
  await expect(equal).not.toContainText(reason);
});
