import {
  test,
  expect,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const org = "10000000-0000-0000-0000-000000000010";
const asset = "10000000-0000-0000-0000-000000001000";
async function write(
  request: APIRequestContext,
  path: string,
  data: object,
  method = "POST",
) {
  const csrf = await (await request.get("/api/v1/csrf")).json();
  const result = await request.fetch(`/api/v1${path}`, {
    method,
    data,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
  expect(result.ok(), await result.text()).toBe(true);
  return result.json();
}
async function login(page: Page, name = "Alice Chen") {
  await page.goto("/");
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
}
async function seed(
  request: APIRequestContext,
  oldContent = "Original recovery steps",
  newContent = "Changed recovery steps",
) {
  const title = `Recovery ${Date.now()}`;
  const first = await write(request, `/organizations/${org}/documents`, {
    assetId: asset,
    title: `${title} original`,
    content: oldContent,
  });
  const current = await write(
    request,
    `/organizations/${org}/documents/${first.id}`,
    { title, content: newContent, expectedVersion: 1 },
    "PUT",
  );
  return { current, title, oldContent, newContent };
}
async function open(page: Page, title: string) {
  // Refresh metadata after API setup; the browser still uses its real authenticated session.
  await page.reload();
  await page.getByRole("button", { name: /Core switch/ }).click();
  const list = page.locator(".document-list"),
    target = list.getByRole("button", { name: new RegExp(title) });
  await expect(list).toBeVisible();
  for (let i = 0; i < 30 && !(await target.count()); i++) {
    const count = await list.getByRole("button").count();
    await page
      .getByRole("button", { name: "Load more documents", exact: true })
      .click();
    await expect
      .poll(() => list.getByRole("button").count())
      .toBeGreaterThan(count);
  }
  await target.click();
  await page.getByRole("button", { name: "View revisions" }).click();
  await page
    .getByRole("button", { name: "Compare revisions", exact: true })
    .click();
  await expect(
    page.getByRole("region", { name: "Revision comparison" }),
  ).toBeVisible();
}

test("compare, confirm and persist an audited restore without losing history; search catches up", async ({
  page,
}) => {
  await login(page);
  const term = `recovere2e${Date.now()}`;
  const doc = await seed(
    page.request,
    `${term} Original recovery steps`,
    "Changed recovery steps",
  );
  await open(page, doc.title);
  const comparison = page.getByRole("region", { name: "Revision comparison" });
  await expect(comparison).toContainText(
    "1 lines added · 1 lines removed · Title changed",
  );
  await expect(comparison.locator(".diff-removed")).toContainText(
    doc.oldContent,
  );
  await expect(comparison.locator(".diff-added")).toContainText(doc.newContent);
  const restore = page.getByRole("button", {
    name: "Restore revision 1…",
    exact: true,
  });
  await restore.click();
  const dialog = page.getByRole("dialog", { name: "Restore revision 1?" });
  await expect(
    dialog.getByRole("textbox", { name: "Reason for restore" }),
  ).toBeFocused();
  await expect(
    dialog.getByRole("button", { name: "Create restored revision" }),
  ).toBeDisabled();
  await page.keyboard.press("Escape");
  await expect(dialog).not.toBeVisible();
  await expect(restore).toBeFocused();
  await restore.click();
  await dialog
    .getByRole("textbox", { name: "Reason for restore" })
    .fill("Reinstate approved recovery procedure");
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await dialog
    .getByRole("button", { name: "Create restored revision" })
    .click();
  await expect(page.getByRole("status")).toContainText(
    "Revision 3 created from revision 1",
  );
  await expect(page.getByRole("article")).toContainText(doc.oldContent);
  await page.getByRole("button", { name: "View revisions" }).click();
  const history = page.getByRole("region", { name: "Document revisions" });
  await history.locator("summary").filter({ hasText: "Revision 3" }).click();
  await expect(history).toContainText(
    "Restored from revision 1 · Reinstate approved recovery procedure",
  );
  await history.locator("summary").filter({ hasText: "Revision 2" }).click();
  await expect(history).toContainText(doc.newContent);
  await page.getByRole("button", { name: "Audit history" }).click();
  await expect(
    page.getByRole("row").filter({ hasText: "Document.Restored" }).first(),
  ).toContainText("Revision 1 → 3");
  await page.reload();
  await page.getByRole("button", { name: /Core switch/ }).click();
  await page
    .getByRole("button", { name: new RegExp(`${doc.title} original`) })
    .click();
  await expect(page.getByRole("article")).toContainText("Revision 3");
  await expect(page.getByRole("article")).toContainText(doc.oldContent);
  await expect
    .poll(
      async () => {
        const result = await (
          await page.request.get(`/api/v1/search?q=${term}&type=document`)
        ).json();
        return result.items.find(
          (item: { id: string }) => item.id === doc.current.id,
        )?.version;
      },
      { timeout: 15_000 },
    )
    .toBe(3);
});

test("a viewer can compare revisions without restore controls", async ({
  page,
  playwright,
}) => {
  const editor = await playwright.request.newContext({
    baseURL: "http://127.0.0.1:3000",
  });
  let doc;
  try {
    const csrf = await (await editor.get("/api/v1/csrf")).json();
    expect(
      (
        await editor.post("/api/v1/dev/login", {
          data: { profile: "alice" },
          headers: { "X-CSRF-TOKEN": csrf.token },
        })
      ).status(),
    ).toBe(204);
    doc = await seed(editor);
  } finally {
    await editor.dispose();
  }
  await login(page, "Sam Rivera");
  await open(page, doc.title);
  await expect(
    page.getByRole("region", { name: "Revision comparison" }),
  ).toContainText(doc.oldContent);
  await expect(
    page.getByRole("button", { name: /Restore revision/ }),
  ).toHaveCount(0);
});

test("a concurrent edit blocks restore, keeps the reason and offers review before retry", async ({
  page,
}) => {
  await login(page);
  const doc = await seed(page.request);
  await open(page, doc.title);
  await page
    .getByRole("button", { name: "Restore revision 1…", exact: true })
    .click();
  const dialog = page.getByRole("dialog");
  const reason = dialog.getByRole("textbox", { name: "Reason for restore" });
  await reason.fill("Keep this reason while resolving the conflict");
  await write(
    page.request,
    `/organizations/${org}/documents/${doc.current.id}`,
    {
      title: doc.title,
      content: "A concurrent writer won",
      expectedVersion: 2,
    },
    "PUT",
  );
  await dialog
    .getByRole("button", { name: "Create restored revision" })
    .click();
  await expect(dialog.getByRole("alert")).toContainText("changed");
  await expect(reason).toHaveValue(
    "Keep this reason while resolving the conflict",
  );
  await expect(
    dialog.getByRole("button", { name: "Create restored revision" }),
  ).toBeDisabled();
  await dialog.getByRole("button", { name: "Reload current document" }).click();
  await expect(page.getByRole("article")).toContainText(
    "A concurrent writer won",
  );
  await expect(page.getByRole("article")).toContainText("Revision 3");
  await page.getByRole("button", { name: "View revisions" }).click();
  await page.getByRole("spinbutton", { name: "From revision" }).fill("1");
  await page
    .getByRole("button", { name: "Compare revisions", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Restore revision 1…", exact: true })
    .click();
  await dialog
    .getByRole("textbox", { name: "Reason for restore" })
    .fill("Reviewed concurrent edit; restore approved original");
  await dialog
    .getByRole("button", { name: "Create restored revision" })
    .click();
  await expect(page.getByRole("status")).toContainText(
    "Revision 4 created from revision 1",
  );
});

test("large plain-text comparisons have bounded pages and accessible mobile layout", async ({
  page,
}) => {
  await login(page);
  const literal = '<img src=x onerror="window.recoveryInjected=true">';
  const doc = await seed(
    page.request,
    Array.from({ length: 600 }, (_, i) => `${literal} old ${i}`).join("\n"),
    Array.from({ length: 600 }, (_, i) => `new ${i}`).join("\n"),
  );
  await open(page, doc.title);
  const comparison = page.getByRole("region", { name: "Revision comparison" });
  await expect(comparison).toContainText(
    "Large change: showing replaced sections in full.",
  );
  await expect(comparison.locator("tbody tr")).toHaveCount(200);
  await expect(comparison).toContainText("1–200 of 1200 rows");
  await expect(comparison.locator("img")).toHaveCount(0);
  expect(
    await page.evaluate(
      () =>
        (window as Window & { recoveryInjected?: boolean }).recoveryInjected,
    ),
  ).toBeUndefined();
  await page.getByRole("button", { name: "Next changes" }).click();
  await expect(comparison).toContainText("201–400 of 1200 rows");
  await page.getByRole("button", { name: "Previous changes" }).click();
  await expect(comparison).toContainText("1–200 of 1200 rows");
  expect(
    (
      await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
        .analyze()
    ).violations,
  ).toEqual([]);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page
    .getByRole("button", { name: "Restore revision 1…", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: "test-results/saveit-recovery-mobile.png",
    fullPage: false,
  });
  await page.getByRole("button", { name: "Cancel restore" }).click();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await comparison.scrollIntoViewIfNeeded();
  await page.screenshot({
    path: "test-results/saveit-recovery.png",
    fullPage: false,
  });
});
