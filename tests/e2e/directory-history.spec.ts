import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";

const org = "10000000-0000-0000-0000-000000000010";
const literal = '<img src=x onerror="window.directoryHistoryInjected=true">';
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
  isArchived = false,
) {
  return {
    name,
    details: "Synthetic address / department",
    notes: `${literal}\r\nExact 😀\n`,
    email: resource === "contacts" ? "history@example.invalid" : "",
    phone: resource === "contacts" ? "+1 555 0100" : "",
    expectedVersion: version,
    isArchived,
  };
}
async function seed(page: Page, resource: string, count = 3) {
  const original = input(resource, `Directory original ${Date.now()}`),
    root = `/organizations/${org}/${resource}`;
  let current = await write(page.request, root, original);
  const id = current.id,
    path = `${root}/${id}`;
  for (let version = 1; version < count; version++)
    current = await write(
      page.request,
      path,
      input(
        resource,
        `Directory changed ${id} v${version + 1}`,
        version,
        version === 1,
      ),
      true,
    );
  return { original, current, path, id };
}
async function open(page: Page, resource: string, name: string) {
  await page
    .getByRole("button", { name: "Locations & contacts", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Locations and contacts",
    exact: true,
  });
  await panel
    .getByRole("combobox", { name: "Directory", exact: true })
    .selectOption(resource);
  const list = panel.getByRole("list", {
    name: resource === "contacts" ? "Contact records" : "Location records",
  });
  const target = list.getByRole("button", {
    name: `View ${resource === "contacts" ? "contact" : "location"} history ${name}`,
    exact: true,
  });
  await expect(list.getByRole("listitem").first()).toBeVisible();
  for (let i = 0; i < 40 && !(await target.count()); i++) {
    const previous = await list.getByRole("listitem").count();
    await panel.getByRole("button", { name: `Load more ${resource}` }).click();
    await expect
      .poll(() => list.getByRole("listitem").count())
      .toBeGreaterThan(previous);
  }
  await target.click();
  const history = panel.getByRole("region", {
    name: "Directory revision history",
    exact: true,
  });
  await expect(
    history.getByRole("button", { name: /Review directory revision/ }).first(),
  ).toBeVisible();
  return { panel, history };
}

test("location history restores exact archive/reactivation snapshots and retains prior versions", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, "locations");
  const { panel, history } = await open(page, "locations", data.current.name);
  await history
    .getByRole("button", { name: "Review directory revision 2", exact: true })
    .click();
  const snapshot = history.getByRole("region", {
    name: "Historical directory snapshot",
  });
  await expect(snapshot.getByText("Archived", { exact: true })).toBeVisible();
  await expect(snapshot.locator("img, script")).toHaveCount(0);
  const restore = history.getByRole("button", {
    name: "Restore directory revision 2",
    exact: true,
  });
  await restore.click();
  const dialog = page.getByRole("dialog", {
    name: "Confirm location restoration",
  });
  await expect(dialog).toContainText("Archived state");
  await dialog
    .getByLabel("Reason for directory restoration")
    .fill("Restore the archived location");
  await page.keyboard.press("Escape");
  await expect(dialog).not.toBeVisible();
  await expect(restore).toBeFocused();
  await restore.click();
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(
    history
      .getByRole("status")
      .filter({ hasText: "Location restored as version 4" }),
  ).toBeVisible();
  await expect(
    history.getByRole("button", {
      name: "Close directory history",
      exact: true,
    }),
  ).toBeFocused();
  await history
    .getByRole("button", { name: "Review directory revision 4", exact: true })
    .click();
  await expect(snapshot).toContainText("Restore the archived location");
  await expect(snapshot).toContainText("Version 2");
  await expect(
    panel
      .getByRole("list", { name: "Location records" })
      .getByText(`Directory changed ${data.id} v2`, { exact: true }),
  ).toHaveCount(0);
  await history
    .getByRole("button", { name: "Review directory revision 1", exact: true })
    .click();
  await expect(snapshot).toContainText(data.original.name);
  await expect(snapshot).toContainText(literal);
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  await dialog
    .getByLabel("Reason for directory restoration")
    .fill("Restore active original location");
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(
    history
      .getByRole("status")
      .filter({ hasText: "Location restored as version 5" }),
  ).toBeVisible();
  const head = await (await page.request.get(`/api/v1${data.path}`)).json();
  expect(head).toMatchObject({
    name: data.original.name,
    notes: data.original.notes,
    isArchived: false,
    version: 5,
  });
  const first = await (
    await page.request.get(`/api/v1${data.path}/revisions/1`)
  ).json();
  expect(first.notes).toBe(data.original.notes);
  expect(
    await page.evaluate(
      () =>
        (window as unknown as { directoryHistoryInjected?: boolean })
          .directoryHistoryInjected,
    ),
  ).toBeUndefined();
});

test("contact restore conflict keeps the reason through a failed reload and requires renewed review", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, "contacts");
  const { history } = await open(page, "contacts", data.current.name);
  await history
    .getByRole("button", { name: "Review directory revision 1", exact: true })
    .click();
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
      name: "Confirm contact restoration",
    }),
    reason = dialog.getByLabel("Reason for directory restoration");
  await reason.fill("Keep this restore reason");
  await write(
    page.request,
    data.path,
    input("contacts", "Concurrent contact", 3),
    true,
  );
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(dialog.getByRole("alert")).toContainText("record changed");
  await expect(reason).toHaveValue("Keep this restore reason");
  await expect(
    dialog.getByRole("button", {
      name: "Confirm directory restore",
      exact: true,
    }),
  ).toBeDisabled();
  const route = `**/api/v1${data.path}`;
  await page.route(route, (r) =>
    r.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Synthetic failed current reload" }),
    }),
  );
  await dialog
    .getByRole("button", { name: "Reload current contact", exact: true })
    .click();
  await expect(dialog.getByRole("alert")).toContainText(
    "Synthetic failed current reload",
  );
  await expect(reason).toHaveValue("Keep this restore reason");
  await page.unroute(route);
  await dialog
    .getByRole("button", { name: "Reload current contact", exact: true })
    .click();
  await expect(dialog).not.toBeVisible();
  await expect(history).toContainText("Current version 4");
  await expect(
    history.getByRole("button", {
      name: "Restore directory revision 1",
      exact: true,
    }),
  ).toBeFocused();
  await expect(
    history.getByRole("region", { name: "Historical directory snapshot" }),
  ).toContainText(data.original.name);
  await history
    .getByRole("button", { name: "Restore directory revision 1", exact: true })
    .click();
  await expect(reason).toHaveValue("Keep this restore reason");
  await dialog
    .getByRole("button", { name: "Confirm directory restore", exact: true })
    .click();
  await expect(
    history
      .getByRole("status")
      .filter({ hasText: "Contact restored as version 5" }),
  ).toBeVisible();
  await expect(
    history.getByRole("button", {
      name: "Close directory history",
      exact: true,
    }),
  ).toBeFocused();
  const head = await (await page.request.get(`/api/v1${data.path}`)).json();
  expect(head).toMatchObject({
    email: data.original.email,
    phone: data.original.phone,
    notes: data.original.notes,
    version: 5,
  });
});

test("Viewer history is literal, mobile accessible and cleared after denied source access", async ({
  page,
  browser,
}) => {
  await login(page);
  const data = await seed(page, "contacts");
  const viewerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
  });
  const viewer = await viewerContext.newPage();
  try {
    await login(viewer, "Sam Rivera");
    const { panel, history } = await open(
      viewer,
      "contacts",
      data.current.name,
    );
    await history
      .getByRole("button", { name: "Review directory revision 1", exact: true })
      .click();
    const snapshot = history.getByRole("region", {
      name: "Historical directory snapshot",
    });
    await expect(snapshot).toContainText(literal);
    await expect(snapshot).toContainText(data.original.email);
    await expect(
      history.getByRole("button", { name: /Restore directory revision/ }),
    ).toHaveCount(0);
    await expect(
      panel.getByRole("button", { name: /^Edit contact/ }),
    ).toHaveCount(0);
    await expect(snapshot.locator("img, script, a")).toHaveCount(0);
    expect(
      await viewer.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    const scan = await new AxeBuilder({ page: viewer })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(scan.violations).toEqual([]);
    await snapshot.scrollIntoViewIfNeeded();
    await viewer.screenshot({
      path: "test-results/directory-history-mobile.png",
    });
    await viewer.route(`**/api/v1${data.path}/revisions/1`, (route) =>
      route.fulfill({
        status: 403,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Synthetic revoked history access" }),
      }),
    );
    await history
      .getByRole("button", { name: "Review directory revision 1", exact: true })
      .click();
    await expect(panel.getByRole("alert")).toContainText(
      "Synthetic revoked history access",
    );
    await expect(snapshot).toHaveCount(0);
    await expect(history).toHaveCount(0);
    await expect(panel.getByRole("listitem")).toHaveCount(0);
  } finally {
    await viewerContext.close();
  }
});

test("bounded history retry clears prior pages and directory controls honor workspace operations", async ({
  page,
}) => {
  test.setTimeout(150_000);
  await login(page);
  const data = await seed(page, "locations", 55);
  const { panel, history } = await open(page, "locations", data.current.name);
  await expect(history.getByRole("listitem")).toHaveCount(50);
  await expect(
    history.getByRole("button", {
      name: "Review directory revision 1",
      exact: true,
    }),
  ).toHaveCount(0);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route("**/api/v1/logout", async (route) => {
    await gate;
    await route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Synthetic paused sign-out" }),
    });
  });
  const pending = page.waitForRequest(
    (r) => new URL(r.url()).pathname === "/api/v1/logout",
  );
  await page.getByRole("button", { name: /Alice Chen Sign out/ }).click();
  await pending;
  try {
    await expect(
      history.getByRole("button", {
        name: "Review directory revision 55",
        exact: true,
      }),
    ).toBeDisabled();
    await expect(
      panel.getByRole("combobox", { name: "Directory", exact: true }),
    ).toBeDisabled();
  } finally {
    release();
  }
  await expect(
    history.getByRole("button", {
      name: "Review directory revision 55",
      exact: true,
    }),
  ).toBeEnabled();
  await page.unroute("**/api/v1/logout");
  await page.route(`**/api/v1${data.path}/revisions?before=6`, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Synthetic history page failure" }),
    }),
  );
  await history
    .getByRole("button", { name: "Load more directory revisions", exact: true })
    .click();
  await expect(history.getByRole("alert")).toContainText(
    "Synthetic history page failure",
  );
  await expect(history.getByRole("listitem")).toHaveCount(0);
  await expect(
    history.getByRole("button", {
      name: "Load more directory revisions",
      exact: true,
    }),
  ).toHaveCount(0);
  await page.unroute(`**/api/v1${data.path}/revisions?before=6`);
  await history
    .getByRole("button", { name: "Retry directory history", exact: true })
    .click();
  await expect(history.getByRole("listitem")).toHaveCount(50);
  await history
    .getByRole("button", { name: "Load more directory revisions", exact: true })
    .click();
  await expect(history.getByRole("listitem")).toHaveCount(55);
  await history
    .getByRole("button", { name: "Review directory revision 1", exact: true })
    .click();
  await expect(
    history.getByRole("region", { name: "Historical directory snapshot" }),
  ).toContainText(data.original.name);
});
