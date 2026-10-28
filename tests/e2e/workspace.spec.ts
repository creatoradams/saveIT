import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { readFileSync } from "node:fs";

test("live OpenAPI matches the committed shared contract", async ({
  request,
}) => {
  const response = await request.get("http://127.0.0.1:5080/openapi/v1.json");
  expect(response.ok()).toBe(true);
  const actual = await response.json();
  delete actual.servers;
  expect(actual).toEqual(
    JSON.parse(readFileSync("contracts/openapi.json", "utf8")),
  );
});

async function login(page: Page, name = "Alice Chen") {
  await page.goto("/");
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}

test("persistent asset → linked document → revision → audit, plus organization switching", async ({
  page,
}) => {
  await login(page);
  const assetName = `Smoke firewall ${Date.now()}`;
  await page.getByRole("button", { name: "New asset" }).click();
  await page.getByRole("textbox", { name: "Asset name" }).fill(assetName);
  await page.getByRole("button", { name: "Create asset", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: assetName, exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Add document" }).click();
  await page
    .getByRole("textbox", { name: "Document title" })
    .fill("Failover procedure");
  await page
    .getByRole("textbox", { name: "Content", exact: true })
    .fill("Disconnect the synthetic uplink.");
  await page.getByRole("button", { name: "Save document" }).click();
  await expect(page.getByRole("status")).toContainText("revision 1");
  await page.getByRole("button", { name: "Edit document" }).click();
  await page
    .getByRole("textbox", { name: "Content", exact: true })
    .fill("Check the synthetic standby uplink first.");
  await page.getByRole("button", { name: "Save document" }).click();
  await expect(page.getByRole("status")).toContainText("revision 2");
  await page.getByRole("button", { name: "View revisions" }).click();
  await expect(
    page.getByRole("region", { name: "Document revisions" }),
  ).toContainText("Revision 1");
  await page.getByText("Revision 1", { exact: true }).click();
  await expect(
    page.getByText("Disconnect the synthetic uplink.", { exact: true }),
  ).toBeVisible();
  await page.reload();
  await page.getByRole("button", { name: new RegExp(assetName) }).click();
  await page.getByRole("button", { name: /Failover procedure/ }).click();
  await expect(
    page.getByText("Check the synthetic standby uplink first.", {
      exact: true,
    }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/saveit-workspace.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "Audit history" }).click();
  await expect(page.getByRole("table")).toContainText("Document.Revised");
  await expect(page.getByRole("table")).toContainText("alice");
  await page
    .getByRole("combobox", { name: "ORGANIZATION" })
    .selectOption({ label: "Harbor Clinic" });
  await page.getByRole("button", { name: "Assets & docs" }).click();
  await expect(
    page.getByRole("heading", { name: "A clean slate" }),
  ).toBeVisible();
  await expect(page.getByRole("heading", { name: assetName })).toHaveCount(0);
  await page
    .getByRole("combobox", { name: "ORGANIZATION" })
    .selectOption({ label: "Acme Manufacturing" });
  await expect(
    page.getByRole("button", { name: new RegExp(assetName) }),
  ).toBeVisible();
});

test("read-only identity has no editing controls and one authorized organization", async ({
  page,
}) => {
  await login(page, "Sam Rivera");
  await expect(page.getByText("Read only", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "New asset" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Add document" })).toHaveCount(
    0,
  );
  await expect(
    page.locator("#organization option:not([disabled])"),
  ).toHaveCount(1);
  await page.getByRole("button", { name: "Audit history" }).click();
  await expect(
    page.getByRole("heading", { name: "Editor access required" }),
  ).toBeVisible();
});

test("a stale revision preserves the editor text and offers an explicit reload", async ({
  page,
}) => {
  await login(page);
  await page.getByRole("button", { name: /Core switch/ }).click();
  await page.getByRole("button", { name: "Add document" }).click();
  const title = `Conflict ${Date.now()}`;
  await page.getByRole("textbox", { name: "Document title" }).fill(title);
  await page
    .getByRole("textbox", { name: "Content", exact: true })
    .fill("Initial text");
  await page.getByRole("button", { name: "Save document" }).click();
  await expect(page.getByRole("status")).toContainText("revision 1");
  const org = await page.locator("#organization").inputValue();
  const docs = await (
    await page.request.get(
      `/api/v1/organizations/${org}/assets/10000000-0000-0000-0000-000000001000/documents`,
    )
  ).json();
  const doc = docs.items.find(
    (item: { title: string }) => item.title === title,
  );
  await page.getByRole("button", { name: "Edit document" }).click();
  await page
    .getByRole("textbox", { name: "Content", exact: true })
    .fill("My unsaved text");
  const csrf = await (await page.request.get("/api/v1/csrf")).json();
  const response = await page.request.put(
    `/api/v1/organizations/${org}/documents/${doc.id}`,
    {
      headers: { "X-CSRF-TOKEN": csrf.token },
      data: { title, content: "Another writer won", expectedVersion: 1 },
    },
  );
  expect(response.status()).toBe(200);
  await page.getByRole("button", { name: "Save document" }).click();
  await expect(
    page.getByRole("alert").filter({ hasText: "This record changed" }),
  ).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "Content", exact: true }),
  ).toHaveValue("My unsaved text");
  await page.getByRole("button", { name: "Reload latest revision" }).click();
  await expect(
    page.getByText("Another writer won", { exact: true }),
  ).toBeVisible();
});

test("keyboard sign-in, responsive layout, and automated WCAG checks", async ({
  page,
}) => {
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: /Good IT starts/ }),
  ).toBeVisible();
  const loginScan = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(loginScan.violations).toEqual([]);
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: /Alice Chen/ })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await page.keyboard.press("Tab");
  await expect(
    page.getByRole("link", { name: "Skip to workspace" }),
  ).toBeFocused();
  const workspaceScan = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(workspaceScan.violations).toEqual([]);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await expect(
    page.getByRole("combobox", { name: "ORGANIZATION" }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/saveit-mobile.png",
    fullPage: true,
  });
});
