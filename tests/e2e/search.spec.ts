import { test, expect, type APIRequestContext } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

async function mutate(request: APIRequestContext, path: string, body: object) {
  const csrf = await (await request.get("/api/v1/csrf")).json();
  return request.post(`/api/v1${path}`, {
    data: body,
    headers: { "X-CSRF-TOKEN": csrf.token },
  });
}

test("search indexes real changes, filters tenants and types, and opens current documentation", async ({
  page,
  playwright,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: /Alice Chen/ }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  const term = `vegae2e${Date.now()}`;
  const orgA = "10000000-0000-0000-0000-000000000010";
  const orgB = "20000000-0000-0000-0000-000000000010";
  const asset = await (
    await mutate(page.request, `/organizations/${orgA}/assets`, {
      name: `${term} Router`,
      kind: "Network",
    })
  ).json();
  const content = `${term} Standby recovery. <img src=x onerror="window.searchInjected=true">`;
  const doc = await (
    await mutate(page.request, `/organizations/${orgA}/documents`, {
      assetId: asset.id,
      title: `${term} Recovery procedure`,
      content,
    })
  ).json();

  const bob = await playwright.request.newContext({
    baseURL: "http://127.0.0.1:3000",
  });
  try {
    expect((await mutate(bob, "/dev/login", { profile: "bob" })).status()).toBe(
      204,
    );
    const otherAsset = await (
      await mutate(bob, `/organizations/${orgB}/assets`, {
        name: `${term} Router`,
        kind: "Network",
      })
    ).json();
    await mutate(bob, `/organizations/${orgB}/documents`, {
      assetId: otherAsset.id,
      title: `${term} ForeignCanary`,
      content: "Must stay in Southbank",
    });
    await expect
      .poll(
        async () =>
          (
            await (await page.request.get(`/api/v1/search?q=${term}`)).json()
          ).items.some((item: { id: string }) => item.id === doc.id),
        { timeout: 15_000 },
      )
      .toBe(true);
    await page.getByRole("button", { name: "Search", exact: true }).click();
    const form = page.getByRole("search", {
      name: "Find assets and documents",
    });
    await form
      .getByRole("searchbox", { name: "Search assets and documents" })
      .fill(term);
    await form
      .getByRole("combobox", { name: "Search scope" })
      .selectOption("all");
    await form.getByRole("button", { name: "Search", exact: true }).click();
    const results = page.getByRole("list", { name: "Search results" });
    await expect(results.getByRole("listitem")).toHaveCount(2);
    await expect(results).not.toContainText("ForeignCanary");
    await expect(results.locator("img")).toHaveCount(0);
    await form
      .getByRole("combobox", { name: "Record type" })
      .selectOption("document");
    await form.getByRole("button", { name: "Search", exact: true }).click();
    await expect(results.getByRole("listitem")).toHaveCount(1);
    expect(
      (
        await new AxeBuilder({ page })
          .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
          .analyze()
      ).violations,
    ).toEqual([]);
    await page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .selectOption({ label: "Harbor Clinic" });
    await expect(
      page.getByRole("list", { name: "Search results" }),
    ).toHaveCount(0);
    await page
      .getByRole("combobox", { name: "ORGANIZATION", exact: true })
      .selectOption({ label: "Acme Manufacturing" });
    await form.getByRole("button", { name: "Search", exact: true }).click();
    await expect(results.getByRole("listitem")).toHaveCount(1);
    await page.setViewportSize({ width: 390, height: 844 });
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.screenshot({
      path: "test-results/saveit-search.png",
      fullPage: true,
    });
    await results
      .getByRole("button", { name: "Open document", exact: true })
      .click();
    await expect(page.getByRole("article")).toContainText(content);
    await expect(
      page.getByRole("button", { name: "Edit document" }),
    ).toBeVisible();
    await expect(page.getByRole("article").locator("img")).toHaveCount(0);
    expect(
      await page.evaluate(
        () => (window as Window & { searchInjected?: boolean }).searchInjected,
      ),
    ).toBeUndefined();
    await expect
      .poll(
        async () =>
          (
            await (
              await bob.get(`/api/v1/search?q=${term}&type=document`)
            ).json()
          ).items.length,
        { timeout: 15_000 },
      )
      .toBe(1);
    expect(
      JSON.stringify(await (await bob.get(`/api/v1/search?q=${term}`)).json()),
    ).not.toContain("Standby recovery");
  } finally {
    await bob.dispose();
  }
});

test("read-only search clears stale previews on failure, retries, and handles empty results", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: /Sam Rivera/ }).click();
  await expect(
    page.getByRole("heading", { name: "Everything has a home." }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Search", exact: true }).click();
  const form = page.getByRole("search");
  await form.getByRole("searchbox").fill("Core switch");
  await form.getByRole("button", { name: "Search", exact: true }).click();
  await expect(
    page.getByRole("list", { name: "Search results" }).getByRole("listitem"),
  ).toHaveCount(1);
  await page.route("**/api/v1/search?**", (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({
        title: "Search is temporarily unavailable. Try again shortly.",
      }),
    }),
  );
  await form.getByRole("button", { name: "Search", exact: true }).click();
  await expect(
    page
      .getByRole("alert")
      .filter({ hasText: "Search is temporarily unavailable" }),
  ).toBeVisible();
  await expect(page.getByRole("list", { name: "Search results" })).toHaveCount(
    0,
  );
  await page.unroute("**/api/v1/search?**");
  await page.getByRole("button", { name: "Retry search" }).click();
  await expect(
    page.getByRole("list", { name: "Search results" }).getByRole("listitem"),
  ).toHaveCount(1);
  await form.getByRole("searchbox").fill("noresultquasarcanary");
  await form.getByRole("button", { name: "Search", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "No matching records" }),
  ).toBeVisible();
});
