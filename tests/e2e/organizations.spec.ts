import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";

async function login(page: Page, name = "Alice Chen") {
  await page.goto("/");
  await reserveDevelopmentLogin();
  await page.getByRole("button", { name: new RegExp(name) }).click();
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

test("administrator creates an organization, resolves rename conflict and preserves edits after failed reload", async ({
  page,
}) => {
  await login(page);
  await page
    .getByRole("button", { name: "Manage organizations", exact: true })
    .click();
  const panel = page.getByRole("region", {
    name: "Organization administration",
  });
  const name = `ZZZ Branch ${Date.now()}`;
  await panel
    .getByRole("button", { name: "New organization", exact: true })
    .click();
  await panel
    .getByRole("textbox", { name: "Organization name", exact: true })
    .fill(name);
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await panel
    .getByRole("button", { name: "Create organization", exact: true })
    .click();
  await expect(panel.getByRole("status")).toContainText("Editor access");
  const id = await page
    .getByRole("combobox", { name: "ORGANIZATION", exact: true })
    .inputValue();
  await panel
    .getByRole("button", { name: `Rename organization ${name}`, exact: true })
    .click();
  await panel
    .getByRole("textbox", { name: "Organization name", exact: true })
    .fill("ZZZ My unsaved organization name");
  const concurrent = `ZZZ Concurrent branch ${Date.now()}`;
  await write(
    page.request,
    `/organizations/${id}`,
    { name: concurrent, expectedVersion: 1 },
    true,
  );
  await panel
    .getByRole("button", { name: "Save organization name", exact: true })
    .click();
  await expect(panel.getByRole("alert")).toContainText("changed");
  await expect(
    panel.getByRole("textbox", { name: "Organization name", exact: true }),
  ).toHaveValue("ZZZ My unsaved organization name");
  await page.route("**/api/v1/organizations", (route) =>
    route.request().method() === "GET"
      ? route.fulfill({
          status: 503,
          contentType: "application/problem+json",
          body: JSON.stringify({
            title: "Organization list temporarily unavailable",
          }),
        })
      : route.continue(),
  );
  await panel
    .getByRole("button", { name: "Reload organizations", exact: true })
    .click();
  await expect(
    page
      .getByRole("alert")
      .filter({ hasText: "Organization list temporarily unavailable" }),
  ).toBeVisible();
  await expect(
    panel.getByRole("textbox", { name: "Organization name", exact: true }),
  ).toHaveValue("ZZZ My unsaved organization name");
  await page.unroute("**/api/v1/organizations");
  await panel
    .getByRole("button", { name: "Reload organizations", exact: true })
    .click();
  await panel
    .getByRole("button", {
      name: `Rename organization ${concurrent}`,
      exact: true,
    })
    .click();
  const finalName = `ZZZ Reviewed branch ${Date.now()}`;
  await panel
    .getByRole("textbox", { name: "Organization name", exact: true })
    .fill(finalName);
  await panel
    .getByRole("button", { name: "Save organization name", exact: true })
    .click();
  await expect(panel.getByRole("status")).toContainText("version 3");
  await page.setViewportSize({ width: 390, height: 844 });
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
    path: "test-results/saveit-organizations-mobile.png",
  });
  await panel
    .getByRole("button", {
      name: `Open organization ${finalName}`,
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("heading", { name: "A clean slate", exact: true }),
  ).toBeVisible();
  await page.reload();
  expect(
    (await (await page.request.get(`/api/v1/organizations/${id}`)).json()).name,
  ).toBe(finalName);
});

test("organization choices load further pages and search opens an authorized organization beyond the loaded page", async ({
  page,
}) => {
  test.setTimeout(120_000);
  await login(page);
  const unique = Date.now();
  let last!: { id: string; name: string };
  for (let i = 0; i < 53; i++)
    last = await write(page.request, "/organizations", {
      name: `ZZZ Pagination ${unique} ${String(i).padStart(3, "0")}`,
    });
  const term = `orgnavigation${unique}`;
  const asset = await write(page.request, `/organizations/${last.id}/assets`, {
    name: term,
    kind: "Network",
  });
  await page.reload();
  const picker = page.getByRole("combobox", {
    name: "ORGANIZATION",
    exact: true,
  });
  await expect(picker).toBeVisible();
  await expect(picker.locator(`option[value="${last.id}"]`)).toHaveCount(0);
  await expect
    .poll(
      async () =>
        (
          await (await page.request.get(`/api/v1/search?q=${term}`)).json()
        ).items.some((row: { id: string }) => row.id === asset.id),
      { timeout: 15_000 },
    )
    .toBe(true);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  const search = page.getByRole("search", {
    name: "Find assets and documents",
  });
  await search
    .getByRole("searchbox", { name: "Search assets and documents" })
    .fill(term);
  await search
    .getByRole("combobox", { name: "Search scope" })
    .selectOption("all");
  await search.getByRole("button", { name: "Search", exact: true }).click();
  await page
    .getByRole("list", { name: "Search results" })
    .getByRole("button", { name: new RegExp(term) })
    .click();
  await expect(
    page.getByRole("heading", { name: term, exact: true }),
  ).toBeVisible();
  await expect(picker).toHaveValue(last.id);
  await expect(page.getByText("Editor access", { exact: true })).toBeVisible();
  await page.reload();
  await expect(picker).toBeVisible();
  await expect(picker.locator(`option[value="${last.id}"]`)).toHaveCount(0);
  for (
    let i = 0;
    i < 30 &&
    (await picker.locator(`option[value="${last.id}"]`).count()) === 0;
    i++
  ) {
    const previous = await picker.locator("option").count();
    await page
      .getByRole("button", {
        name: "Load more organization choices",
        exact: true,
      })
      .click();
    await expect
      .poll(() => picker.locator("option").count())
      .toBeGreaterThan(previous);
  }
  await picker.selectOption(last.id);
  await expect(
    page.getByRole("heading", { name: term, exact: true }),
  ).toBeVisible();
});

test("viewer has a bounded organization picker and no administration controls", async ({
  page,
}) => {
  await login(page, "Sam Rivera");
  await expect(
    page.getByRole("button", { name: "Manage organizations", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", {
      name: "Load more organization choices",
      exact: true,
    }),
  ).toHaveCount(0);
  const response = await page.request.get("/api/v1/organizations");
  const result = await response.json();
  expect(result.items).toHaveLength(1);
  expect(result.nextCursor).toBeNull();
});
