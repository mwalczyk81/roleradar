import { test, expect, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { startInsightsHarness, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { checkAxeInThemes } from "./support/axe.js";
import { assertNotRealStackProject } from "./support/isolation.js";

/**
 * T046: the Insights page in real Chromium, against the loopback stub API and the fictional fixture only
 * (never the developer's stack, never a database). Set INSIGHTS_SCREENSHOT_DIR to also write review screenshots.
 */

const SESSION_ID = "e2e-insights-session";
const OWNER = "insights-owner-aaa";
const snapshot = loadSnapshot();
const AS_OF = snapshot.asOf;

let harness: InsightsHarness;

const expected = (key: string, window: "90d" | "all") => snapshot.lenses[key].expected?.[OWNER]?.[window] as number;

function trackCsp(page: Page): string[] {
  const violations: string[] = [];
  page.on("console", (msg) => {
    const text = msg.text();
    if (text.toLowerCase().includes("content security policy") || text.toLowerCase().includes("violates the following")) {
      violations.push(text);
    }
  });
  return violations;
}

async function shot(page: Page, name: string) {
  const dir = process.env.INSIGHTS_SCREENSHOT_DIR;
  if (dir) {
    fs.mkdirSync(dir, { recursive: true });
    await page.screenshot({ path: path.join(dir, `${name}.png`), fullPage: true });
  }
}

test.describe("Insights page (real Chromium, fictional loopback stub)", () => {
  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startInsightsHarness(SESSION_ID);
  });

  test.afterAll(async () => {
    await harness?.close();
  });

  test.beforeEach(async ({ context }) => {
    await harness.signIn(context);
    harness.setCoverageMode("ok");
    harness.requests.length = 0;
    harness.unexpectedFilters.length = 0;
  });

  test("renders both funnels, the rates and every drill-down link for the 90 day window", async ({ page }) => {
    const csp = trackCsp(page);
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { level: 1, name: "Insights" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Postings first seen in window" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Applications applied in window" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Rates" })).toBeVisible();

    await expect(page.getByRole("link", { name: `Leads: ${expected("leads", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Reviewed: ${expected("reviewed", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Applied to: ${expected("applied-to", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Applied: ${expected("applied", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Responded: ${expected("responded", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Interviewed: ${expected("interviewed", "90d")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: `Offers: ${expected("offers-interviewed", "90d")}` })).toBeVisible();

    await expect(page.getByText(`${expected("applied-to", "90d")} of ${expected("reviewed", "90d")}`, { exact: true })).toBeVisible();
    await expect(page.getByText(`${expected("ghosted", "90d")} of ${expected("ghost-eligible", "90d")}`, { exact: true })).toBeVisible();

    // The wording the spec requires (FR-008, FR-008a).
    await expect(page.getByText(/first recorded a posting/i)).toBeVisible();
    await expect(page.getByText(/imported in bulk share one first-seen date/i)).toBeVisible();

    // 18 lens counts + 2 coverage checks + the 2 all-time interview counts, all count-only.
    const counts = harness.requests.filter((r) => r.pathname.startsWith("/odata/"));
    expect(counts).toHaveLength(22);
    expect(counts.every((r) => r.top === "0" && r.count === "true")).toBe(true);
    expect(harness.unexpectedFilters).toEqual([]);
    expect(csp).toEqual([]);

    await shot(page, "insights-90d-desktop");
  });

  test("the all-time toggle recomputes every count and keeps asOf in the address", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
    harness.requests.length = 0;

    await page.getByRole("link", { name: "All time" }).click();
    await expect(page).toHaveURL(new RegExp(`window=all&asOf=${AS_OF}`));
    await expect(page.getByRole("link", { name: `Leads: ${expected("leads", "all")}` })).toBeVisible();
    await expect(page.getByRole("link", { name: "All time" })).toHaveAttribute("aria-current", "page");

    const counts = harness.requests.filter((r) => r.pathname.startsWith("/odata/"));
    expect(counts).toHaveLength(20);
    expect(harness.unexpectedFilters).toEqual([]);
  });

  test("works with the keyboard: the table view opens from the keyboard and a drill-down link is followed with Enter", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    const tableViewButtons = page.getByRole("button", { name: /table view/i });
    await expect(tableViewButtons).toHaveCount(4); // two funnels, the weekly chart and the search-run yield
    await tableViewButtons.first().focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("table").first()).toBeVisible();

    const reviewed = page.getByRole("link", { name: `Reviewed: ${expected("reviewed", "90d")}` });
    await reviewed.focus();
    await expect(reviewed).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL(new RegExp(`/postings\\?lens=reviewed&window=90d&asOf=${AS_OF}`));
  });

  test("passes axe in light and dark at desktop and phone width, with no horizontal page scroll on a phone", async ({ page }) => {
    for (const size of [{ width: 1280, height: 900 }, { width: 360, height: 800 }]) {
      await page.setViewportSize(size);
      await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
      await checkAxeInThemes(page);

      if (size.width === 360) {
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow, "horizontal page scroll at 360px").toBeLessThanOrEqual(0);
        await shot(page, "insights-90d-phone");
      }
    }
  });

  test("shows 'Needs backfill' with the command instead of numbers that depend on the missing flags", async ({ page }) => {
    harness.setCoverageMode("needs-backfill");
    const csp = trackCsp(page);
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(page.getByText("Some numbers need a one-time backfill")).toBeVisible();
    await expect(page.getByText("roleradar reconcile-read-models").first()).toBeVisible();
    await expect(page.getByText("Needs backfill").first()).toBeVisible();
    // A number that does not depend on the flags is still shown.
    await expect(page.getByRole("link", { name: `Leads: ${expected("leads", "90d")}` })).toBeVisible();
    // The application rate's "x of y" must not appear while the posting flag is incomplete.
    await expect(page.getByText(`${expected("applied-to", "90d")} of ${expected("reviewed", "90d")}`, { exact: true })).toHaveCount(0);
    expect(csp).toEqual([]);

    await checkAxeInThemes(page);
    await shot(page, "insights-needs-backfill");
  });

  test("dark theme renders the page and charts (review screenshot)", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce", colorScheme: "dark" });
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
    await expect(page.getByRole("heading", { level: 1, name: "Insights" })).toBeVisible();
    await shot(page, "insights-90d-dark");
  });
});
