import { test, expect, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { startInsightsHarness, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { checkAxeInThemes } from "./support/axe.js";
import { assertNotRealStackProject } from "./support/isolation.js";

/**
 * T070: the Insights trend section in real Chromium, against the loopback stub API and the fictional fixture only
 * (never the developer's stack, never a database; the isolation guard from T050 runs in beforeAll).
 * Set INSIGHTS_SCREENSHOT_DIR to also write review screenshots.
 */

const SESSION_ID = "e2e-insights-trends-session";
const AS_OF = loadSnapshot().asOf;

let harness: InsightsHarness;

function trackCsp(page: Page): string[] {
  const violations: string[] = [];
  page.on("console", (msg) => {
    const text = msg.text().toLowerCase();
    if (text.includes("content security policy") || text.includes("violates the following")) {
      violations.push(msg.text());
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

function trends(page: Page) {
  return page.getByRole("region", { name: "Trends" });
}

test.describe("Insights trends (real Chromium, fictional loopback stub)", () => {
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
    harness.setStatsMode("ok");
    harness.requests.length = 0;
    harness.unexpectedFilters.length = 0;
  });

  test("shows the median, the weekly column chart and the run-yield sparkline for the 90 day window", async ({ page }) => {
    const csp = trackCsp(page);
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { level: 2, name: "Trends" })).toBeVisible();
    await expect(page.getByText("Median response time", { exact: true })).toBeVisible();
    await expect(page.getByText("10 days", { exact: true })).toBeVisible();
    await expect(page.getByText("14 responses", { exact: true })).toBeVisible();

    await expect(trends(page).getByRole("group", { name: "Applications per week" })).toBeVisible();
    await expect(trends(page).getByRole("img", { name: /new postings per search run/i })).toBeVisible();

    // One stats request, with the enum window and the as-of date on screen, in addition to the 22 counts.
    const stats = harness.requests.filter((r) => r.pathname === "/api/v1/insights/stats");
    expect(stats).toHaveLength(1);
    expect(harness.requests.filter((r) => r.pathname.startsWith("/odata/"))).toHaveLength(22);
    expect(csp).toEqual([]);

    await shot(page, "insights-trends-90d-desktop");
  });

  test("the all-time toggle asks for allTime and shows the longer weekly series", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
    harness.requests.length = 0;

    await page.getByRole("link", { name: "All time" }).click();
    await expect(page).toHaveURL(new RegExp(`window=all&asOf=${AS_OF}`));
    await expect(page.getByText("15 responses", { exact: true })).toBeVisible();

    const stats = harness.requests.filter((r) => r.pathname === "/api/v1/insights/stats");
    expect(stats).toHaveLength(1);
    expect(harness.requests.filter((r) => r.pathname.startsWith("/odata/"))).toHaveLength(20);

    await trends(page).getByRole("button", { name: /table view/i }).first().click();
    const rows = trends(page).getByRole("table", { name: "Applications per week" }).getByRole("row");
    await expect(rows).toHaveCount(16); // header + 15 weeks
  });

  test("both trend charts open their table view from the keyboard and close it again", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    const weeklyToggle = trends(page).getByRole("button", { name: /^table view$/i }).first();
    await weeklyToggle.focus();
    await expect(weeklyToggle).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(weeklyToggle).toHaveAttribute("aria-pressed", "true");
    const weeklyTable = trends(page).getByRole("table", { name: "Applications per week" });
    await expect(weeklyTable).toBeVisible();
    await expect(weeklyTable.getByRole("row")).toHaveCount(15); // header + 14 weeks, zero weeks included
    await page.keyboard.press("Space");
    await expect(weeklyTable).toHaveCount(0);

    const runToggle = trends(page).getByRole("button", { name: /table view of search runs/i });
    await runToggle.focus();
    await expect(runToggle).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(runToggle).toHaveAttribute("aria-pressed", "true");
    const runTable = trends(page).getByRole("table", { name: /new postings per search run/i });
    await expect(runTable.getByRole("row")).toHaveCount(7); // header + 6 runs
    await page.keyboard.press("Space");
    await expect(runTable).toHaveCount(0);
    await expect(trends(page).getByRole("img", { name: /new postings per search run/i })).toBeVisible();
  });

  test("the weekly columns can be reached and read with the keyboard (roving focus, arrow keys)", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    const columns = trends(page).getByRole("group", { name: "Applications per week" }).getByRole("img");
    await expect(columns).toHaveCount(14);
    const first = columns.first();
    await first.focus();
    await expect(first).toBeFocused();
    await expect(first).toHaveAccessibleName("Week of 2026-06-29: 1");
    await page.keyboard.press("ArrowRight");
    await expect(columns.nth(1)).toBeFocused();
    await expect(columns.nth(1)).toHaveAccessibleName("Week of 2026-07-06: 2");
    await page.keyboard.press("End");
    await expect(columns.last()).toBeFocused();
    await page.keyboard.press("Home");
    await expect(first).toBeFocused();
  });

  test("labels a capped run series 'showing latest 120 runs'", async ({ page }) => {
    harness.setStatsMode("capped");
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(trends(page).getByText(/showing latest 120 runs/i)).toBeVisible();
  });

  test("says 'No data' and shows empty messages when nothing has a response, an application or a run", async ({ page }) => {
    harness.setStatsMode("empty");
    await page.goto(`${harness.baseUrl}/insights?window=all&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(page.getByText("No data", { exact: true }).first()).toBeVisible();
    await expect(trends(page).getByText(/no applications in this window/i)).toBeVisible();
    await expect(trends(page).getByText(/no search runs in this window/i)).toBeVisible();
    await checkAxeInThemes(page);
  });

  test("a stats failure degrades only the trend and median tiles; the funnels and rates stay up", async ({ page }) => {
    harness.setStatsMode("fail");
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await expect(page.getByText(/trends are unavailable/i)).toBeVisible();
    await expect(page.getByText("Median response time", { exact: true })).toBeVisible();
    await expect(page.getByText("Unavailable").first()).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Postings first seen in window" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Applications applied in window" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Rates" })).toBeVisible();
    await expect(page.getByRole("link", { name: /^Reviewed: \d+$/ })).toBeVisible();
    await checkAxeInThemes(page);
  });

  test("passes axe in light and dark at desktop and phone width, with no horizontal page scroll on a phone", async ({ page }) => {
    for (const size of [{ width: 1280, height: 900 }, { width: 360, height: 800 }]) {
      await page.setViewportSize(size);
      await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
      await expect(trends(page).getByRole("group", { name: "Applications per week" })).toBeVisible();
      await checkAxeInThemes(page);

      // Also with both table views open: tables must not break the layout or axe.
      for (const toggle of await trends(page).getByRole("button", { name: /table view/i }).all()) {
        await toggle.click();
      }
      await checkAxeInThemes(page);

      if (size.width === 360) {
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow, "horizontal page scroll at 360px").toBeLessThanOrEqual(0);
        await shot(page, "insights-trends-phone");
      }
    }
  });

  test("dark theme renders the trend section (review screenshot)", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce", colorScheme: "dark" });
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
    await expect(trends(page).getByRole("group", { name: "Applications per week" })).toBeVisible();
    await shot(page, "insights-trends-dark");
  });
});
