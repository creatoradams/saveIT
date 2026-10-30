import {
  test,
  expect,
  type Page,
  type APIRequestContext,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { reserveDevelopmentLogin } from "./helpers/development-login";
const org = "10000000-0000-0000-0000-000000000010";
async function login(page: Page, name = "Alice Chen") {
  await page.goto("/");
  await reserveDevelopmentLogin();
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}
async function put(request: APIRequestContext, path: string, data: object) {
  const csrf = await (await request.get("/api/v1/csrf")).json();
  return request.put(`/api/v1${path}`, {
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
}

test("create locations and contacts, place an asset, inspect history and restore its metadata", async ({
  page,
}) => {
  await login(page);
  const unique = Date.now();
  const name = `Branch ${unique}`;
  await page
    .getByRole("button", { name: "Locations & contacts", exact: true })
    .click();
  const directory = page.getByRole("region", {
    name: "Locations and contacts",
  });
  await directory.getByRole("button", { name: "New location" }).click();
  await directory.getByRole("textbox", { name: "Location name" }).fill(name);
  await directory
    .getByRole("textbox", { name: "Address", exact: true })
    .fill("123 Synthetic Street");
  await directory
    .getByRole("button", { name: "Save location", exact: true })
    .click();
  await expect(directory.getByRole("status")).toContainText("version 1");
  const locations = await (
    await page.request.get(`/api/v1/organizations/${org}/locations`)
  ).json();
  const location = locations.items.find(
    (item: { name: string }) => item.name === name,
  );
  await directory
    .getByRole("combobox", { name: "Directory", exact: true })
    .selectOption("contacts");
  await directory.getByRole("button", { name: "New contact" }).click();
  await directory
    .getByRole("textbox", { name: "Contact name" })
    .fill(`Site manager ${unique}`);
  await directory
    .getByRole("textbox", { name: "Email", exact: true })
    .fill("manager@example.invalid");
  await directory
    .getByRole("textbox", { name: "Phone", exact: true })
    .fill("555-0100");
  await directory
    .getByRole("textbox", { name: "Notes", exact: true })
    .fill('<img src=x onerror="window.directoryInjected=true">');
  await directory
    .getByRole("button", { name: "Save contact", exact: true })
    .click();
  await expect(directory.getByRole("list")).toContainText(
    "manager@example.invalid",
  );
  await expect(directory.locator("img")).toHaveCount(0);
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await page
    .getByRole("button", { name: "Assets & docs", exact: true })
    .click();
  await page.getByRole("button", { name: "New asset" }).click();
  const assetName = `Placed server ${unique}`;
  await page
    .getByRole("textbox", { name: "Asset name", exact: true })
    .fill(assetName);
  await page.getByRole("button", { name: "Create asset", exact: true }).click();
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await metadata
    .getByRole("button", { name: "Edit asset details", exact: true })
    .click();
  await metadata
    .getByRole("textbox", { name: "Asset type", exact: true })
    .fill("Server");
  await metadata
    .getByRole("combobox", { name: "Asset location" })
    .selectOption(location.id);
  let releasePlacement!: () => void;
  const placementGate = new Promise<void>((resolve) => {
    releasePlacement = resolve;
  });
  await page.route(`**/locations/${location.id}`, async (route) => {
    const response = await route.fetch();
    await placementGate;
    await route.fulfill({ response });
  });
  await metadata
    .getByRole("button", { name: "Save asset details", exact: true })
    .click();
  await expect(metadata).toContainText("Asset version 2");
  await expect(metadata).toContainText("Loading location…");
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await expect(
    metadata.getByRole("region", { name: "Asset revisions" }),
  ).toBeVisible();
  releasePlacement();
  await expect(metadata).toContainText(`Location: ${name}`);
  await page.unroute(`**/locations/${location.id}`);
  await metadata
    .locator("summary")
    .filter({ hasText: "Asset revision 1" })
    .click();
  await metadata
    .getByRole("button", { name: "Restore asset revision 1…", exact: true })
    .click();
  const dialog = page.getByRole("dialog", {
    name: "Restore asset revision 1?",
  });
  await dialog
    .getByRole("textbox", { name: "Reason for asset restore" })
    .fill("Reinstate original inventory placement");
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await dialog
    .getByRole("button", { name: "Create restored asset version" })
    .click();
  await expect(metadata).toContainText("Asset version 3");
  await expect(metadata).toContainText("No location assigned");
  await metadata
    .getByRole("button", { name: "Edit asset details", exact: true })
    .click();
  await page.getByRole("button", { name: "Add document" }).click();
  await expect(
    metadata.getByRole("textbox", { name: "Asset type", exact: true }),
  ).toBeDisabled();
  await expect(
    metadata.getByRole("button", { name: "Save asset details", exact: true }),
  ).toBeDisabled();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({ path: "test-results/saveit-directory-mobile.png" });
});

test("archive, reactivate and resolve directory edit conflicts without silently overwriting", async ({
  page,
}) => {
  await login(page);
  const name = `Archive branch ${Date.now()}`;
  await page
    .getByRole("button", { name: "Locations & contacts", exact: true })
    .click();
  const directory = page.getByRole("region", {
    name: "Locations and contacts",
  });
  await directory.getByRole("button", { name: "New location" }).click();
  await directory.getByRole("textbox", { name: "Location name" }).fill(name);
  await directory
    .getByRole("button", { name: "Save location", exact: true })
    .click();
  await directory
    .getByRole("button", { name: `Edit location ${name}`, exact: true })
    .click();
  await directory
    .getByRole("checkbox", { name: /Archived — retained/ })
    .check();
  await directory
    .getByRole("button", { name: "Save location", exact: true })
    .click();
  await expect(directory.getByRole("list")).not.toContainText(name);
  await directory
    .getByRole("checkbox", { name: "Include archived records" })
    .check();
  await directory
    .getByRole("button", { name: `Edit location ${name}`, exact: true })
    .click();
  await directory
    .getByRole("checkbox", { name: /Archived — retained/ })
    .uncheck();
  await directory
    .getByRole("button", { name: "Save location", exact: true })
    .click();
  await expect(directory.getByRole("status")).toContainText("version 3");
  const rows = await (
    await page.request.get(
      `/api/v1/organizations/${org}/locations?includeArchived=true`,
    )
  ).json();
  const row = rows.items.find((item: { name: string }) => item.name === name);
  await directory
    .getByRole("button", { name: `Edit location ${name}`, exact: true })
    .click();
  await directory
    .getByRole("textbox", { name: "Notes", exact: true })
    .fill("My unsaved notes");
  expect(
    (
      await put(page.request, `/organizations/${org}/locations/${row.id}`, {
        ...row,
        notes: "Another editor's notes",
        expectedVersion: 3,
      })
    ).status(),
  ).toBe(200);
  await directory
    .getByRole("button", { name: "Save location", exact: true })
    .click();
  await expect(directory.getByRole("alert")).toContainText("changed");
  await expect(
    directory.getByRole("textbox", { name: "Notes", exact: true }),
  ).toHaveValue("My unsaved notes");
  await directory.getByRole("button", { name: "Reload directory" }).click();
  await expect(directory.getByRole("list")).toContainText(
    "Another editor's notes",
  );
  await page
    .getByRole("combobox", { name: "ORGANIZATION", exact: true })
    .selectOption({ label: "Harbor Clinic" });
  await expect(directory.getByRole("list")).not.toContainText(name);
});

test("viewer has directory reads and asset history without editing or restore controls", async ({
  page,
}) => {
  await login(page, "Sam Rivera");
  await page
    .getByRole("button", { name: "Locations & contacts", exact: true })
    .click();
  const directory = page.getByRole("region", {
    name: "Locations and contacts",
  });
  await expect(
    directory.getByRole("button", { name: /New location|Edit location/ }),
  ).toHaveCount(0);
  await page
    .getByRole("button", { name: "Assets & docs", exact: true })
    .click();
  await page.getByRole("button", { name: /Core switch/ }).click();
  const metadata = page.getByRole("region", {
    name: "Asset placement and history",
  });
  await expect(
    metadata.getByRole("button", { name: "Edit asset details", exact: true }),
  ).toHaveCount(0);
  await metadata.getByRole("button", { name: "View asset history" }).click();
  await expect(
    metadata.getByRole("region", { name: "Asset revisions" }),
  ).toContainText("Asset revision 1");
  await expect(
    metadata.getByRole("button", { name: /Restore asset revision/ }),
  ).toHaveCount(0);
});
