import {
  test,
  expect,
  type Page,
  type Locator,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";

const orgA = "10000000-0000-0000-0000-000000000010";
const literal =
  '<img src=x onerror="window.templateComparisonInjected=true"> 😀';
async function write(
  r: APIRequestContext,
  path: string,
  data: object,
  put = false,
) {
  const csrf = await (await r.get("/api/v1/csrf")).json();
  const response = await r[put ? "put" : "post"](`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
  expect(response.ok(), await response.text()).toBe(true);
  return response.json();
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
async function seed(page: Page, shared = false, large = false) {
  let org = orgA;
  if (!shared) {
    org = (
      await write(page.request, "/organizations", {
        name: `ZZZ Template comparison ${Date.now()}`,
      })
    ).id;
    await page.reload();
    await selectOrg(page, org);
  }
  const root = `/organizations/${org}/document-templates`;
  const from = large
    ? "prefix\n" +
      Array.from({ length: 550 }, (_, i) => `old ${i + 1}`).join("\n") +
      "\nsuffix"
    : `Exact\r\n${literal}\r\n`;
  const to = large
    ? "prefix\n" +
      Array.from({ length: 550 }, (_, i) => `new ${i + 1}`).join("\n") +
      "\nsuffix"
    : `Exact\n${literal}\n`;
  const t = await write(page.request, root, {
    name: `Original ${literal} ${Date.now()}`,
    title: "Original saved title",
    content: from,
  });
  const path = `${root}/${t.id}`;
  await write(
    page.request,
    path,
    {
      name: "Archived saved name",
      title: "Archived saved title",
      content: to,
      expectedVersion: 1,
      isArchived: true,
    },
    true,
  );
  const current = await write(
    page.request,
    path,
    {
      name: `Current starter ${t.id}`,
      title: "Current saved title",
      content: "Current body is separate",
      expectedVersion: 2,
      isArchived: false,
    },
    true,
  );
  return {
    org,
    path,
    original: t.name as string,
    name: current.name as string,
  };
}
async function open(page: Page, name: string) {
  await page
    .getByRole("button", { name: "Document templates", exact: true })
    .click();
  const workspace = page.getByRole("region", {
    name: "Document templates",
    exact: true,
  });
  await expect(
    workspace.getByRole("button", { name: "Refresh document templates" }),
  ).toBeEnabled();
  const list = workspace.getByRole("list", {
    name: "Published document templates",
    exact: true,
  });
  const target = list.getByRole("button", {
    name: `Inspect template ${name}`,
    exact: true,
  });
  for (let i = 0; i < 64 && !(await target.count()); i++) {
    const count = await list.getByRole("listitem").count();
    await workspace
      .getByRole("button", { name: "More document templates", exact: true })
      .click();
    await expect
      .poll(() => list.getByRole("listitem").count())
      .toBeGreaterThan(count);
  }
  await target.click();
  const opener = workspace.getByRole("button", {
    name: "Compare template revisions",
    exact: true,
  });
  await expect(opener).toBeEnabled();
  await opener.click();
  const panel = workspace.getByRole("region", {
    name: "Template revision comparison",
    exact: true,
  });
  await expect(
    panel.getByLabel("From template revision", { exact: true }),
  ).toBeFocused();
  return { workspace, panel, opener };
}
async function compare(panel: Locator, from = "1", to = "2") {
  await panel.getByLabel("From template revision", { exact: true }).fill(from);
  await panel.getByLabel("To template revision", { exact: true }).fill(to);
  await panel
    .getByRole("button", { name: "Compare template versions", exact: true })
    .click();
  await expect(
    panel.getByRole("heading", { name: "Template comparison results" }),
  ).toBeFocused();
}

test("template comparison preserves historical metadata, lifecycle and exact line-ending meaning with same/reverse inputs and literal rendering", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page);
  const { workspace, panel, opener } = await open(page, data.name);
  await compare(panel);
  await expect(
    panel.getByRole("region", { name: "From template snapshot" }),
  ).toContainText(data.original);
  await expect(
    panel.getByRole("region", { name: "From template snapshot" }),
  ).toContainText("Original saved title");
  await expect(
    panel.getByRole("region", { name: "To template snapshot" }),
  ).toContainText("Archived saved title");
  await expect(
    panel.getByRole("region", { name: "To template snapshot" }),
  ).toContainText("Archived");
  await expect(panel).toContainText("Only content line endings differ");
  await expect(panel).toContainText("No line differences on this view");
  await expect(panel).not.toContainText("Current body is separate");
  await expect(panel.locator("img, a")).toHaveCount(0);
  expect(
    await page.evaluate(
      () =>
        (window as Window & { templateComparisonInjected?: boolean })
          .templateComparisonInjected,
    ),
  ).toBeUndefined();
  await panel
    .getByLabel("Show unchanged template lines", { exact: true })
    .check();
  await expect(
    panel.getByRole("region", { name: "From template snapshot" }),
  ).toHaveCount(0);
  await panel
    .getByRole("button", { name: "Compare template versions", exact: true })
    .click();
  await expect(panel.getByRole("table")).toContainText(literal);
  await expect(panel.getByRole("table")).toContainText('Empty line ("")');
  await compare(panel, "2", "1");
  await expect(
    panel.getByRole("region", { name: "From template snapshot" }),
  ).toContainText("Archived");
  await compare(panel, "1", "1");
  await expect(panel).toContainText("Content: unchanged");
  await panel
    .getByLabel("From template revision", { exact: true })
    .press("Escape");
  await expect(panel).toHaveCount(0);
  await expect(opener).toBeFocused();
  await expect(
    workspace.getByRole("button", {
      name: "Edit document template",
      exact: true,
    }),
  ).toBeEnabled();
});

test("bounded template pages clear stale output, own global busy, restore keyboard focus after delay, and recover from source failure", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, false, true);
  const { panel } = await open(page, data.name);
  await compare(panel);
  await expect(panel.getByRole("table").getByRole("row")).toHaveCount(201);
  await expect(panel).toContainText("1100 changed lines");
  await expect(panel).toContainText("Large replacement");
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  let arrived!: () => void;
  const started = new Promise<void>((resolve) => {
    arrived = resolve;
  });
  const route = `**${data.path}/compare?**offset=200`;
  await page.route(route, async (r) => {
    arrived();
    await gate;
    await r.continue();
  });
  const next = panel.getByRole("button", {
    name: "Next template differences",
    exact: true,
  });
  await next.focus();
  await next.press("Enter");
  await started;
  try {
    await expect(panel.getByRole("table")).toHaveCount(0);
    await expect(
      panel.getByRole("region", { name: "From template snapshot" }),
    ).toHaveCount(0);
    await expect(
      page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
    ).toBeDisabled();
    await expect(
      panel.getByRole("button", { name: "Close template comparison" }),
    ).toBeDisabled();
  } finally {
    release();
  }
  await expect(
    panel.getByRole("heading", { name: "Template comparison results" }),
  ).toBeFocused();
  await expect(panel.getByRole("table").getByRole("row").nth(1)).toContainText(
    "old 201",
  );
  await page.unroute(route);
  const failure = `**${data.path}/compare?**offset=400`;
  await page.route(failure, (r) =>
    r.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({
        title: "Synthetic template comparison unavailable",
      }),
    }),
  );
  await next.click();
  await expect(panel.getByRole("alert")).toBeFocused();
  await expect(panel.getByRole("table")).toHaveCount(0);
  await expect(panel).not.toContainText("1100 changed lines");
  await page.unroute(failure);
  await panel
    .getByRole("button", { name: "Retry template comparison" })
    .click();
  await expect(
    panel.getByRole("heading", { name: "Template comparison results" }),
  ).toBeFocused();
  await expect(panel.getByRole("table").getByRole("row").nth(1)).toContainText(
    "old 1",
  );
  await panel
    .getByRole("button", { name: "Next template differences", exact: true })
    .click();
  await expect(
    panel.getByRole("button", { name: "Previous template differences" }),
  ).toBeEnabled();
  await panel
    .getByRole("button", { name: "Previous template differences" })
    .click();
  await expect(panel.getByRole("table").getByRole("row").nth(1)).toContainText(
    "old 1",
  );
});

test("comparison observes newer publication without changing creation authority and denied comparison clears current organization", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page);
  const { workspace, panel } = await open(page, data.name);
  await write(
    page.request,
    data.path,
    {
      name: "New independent publication",
      title: "New title",
      content: "New source",
      expectedVersion: 3,
      isArchived: false,
    },
    true,
  );
  await compare(panel);
  await expect(panel).toContainText("Observed current template version: 4");
  await expect(panel).toContainText("A newer template publication exists");
  await expect(workspace).toContainText("Current publication v3");
  await page.route(`**${data.path}/compare?**`, (r) =>
    r.fulfill({
      status: 404,
      contentType: "application/problem+json",
      body: JSON.stringify({ title: "Synthetic current grant revoked" }),
    }),
  );
  await panel
    .getByRole("button", { name: "Compare template versions", exact: true })
    .click();
  await expect(workspace).toHaveCount(0);
  await expect(page.getByRole("main")).not.toContainText(data.original);
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION", exact: true }),
  ).toHaveValue("");
});

test("Viewer can compare template history on mobile with literal accessible content and no edit controls", async ({
  page,
}) => {
  await login(page);
  const data = await seed(page, true);
  await page.getByRole("button", { name: /Sign out/ }).click();
  await login(page, "Sam Rivera");
  await page.setViewportSize({ width: 390, height: 844 });
  const { workspace, panel } = await open(page, data.name);
  await compare(panel);
  await panel
    .getByLabel("Show unchanged template lines", { exact: true })
    .check();
  await panel
    .getByRole("button", { name: "Compare template versions", exact: true })
    .click();
  await expect(panel.getByRole("table")).toContainText(literal);
  await expect(
    workspace.getByRole("button", {
      name: "Edit document template",
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    workspace.getByRole("button", {
      name: "Create document from template",
      exact: true,
    }),
  ).toHaveCount(0);
  const results = await new AxeBuilder({ page })
    .include('[aria-label="Template revision comparison"]')
    .analyze();
  expect(results.violations).toEqual([]);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: "test-results/document-template-comparison-mobile.png",
    fullPage: true,
  });
});
