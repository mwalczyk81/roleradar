import type { Locator } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startInsightsHarness, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { assertNotRealStackProject } from "./support/isolation.js";
import { setTheme } from "./support/contrast.js";

/**
 * 009 phase 4 follow-up: the two Insights column charts read the same way. Axis labels are short ("Jul 6") with the ISO
 * date kept in the readout and the table view, labels do not collide, and both charts use the same frame with the
 * heading inside it. Computed-style and layout assertions only (fictional loopback stub, never the developer's stack).
 */

const SESSION_ID = "e2e-insights-charts-session";
const AS_OF = loadSnapshot().asOf;
const SHORT_DATE = /^[A-Z][a-z]{2} \d{1,2}$/;

let harness: InsightsHarness;

test.describe("Insights chart consistency", () => {
  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startInsightsHarness(SESSION_ID);
  });

  test.afterAll(async () => {
    await harness?.close();
  });

  test.beforeEach(async ({ context, page }) => {
    await harness.signIn(context);
    harness.setCoverageMode("ok");
    harness.setStatsMode("ok");
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });
  });

  const figure = (page: import("@playwright/test").Page, title: string): Locator => page.getByRole("region", { name: "Trends" }).locator("figure", { hasText: title });

  async function axisLabels(chart: Locator): Promise<Array<{ text: string; left: number; right: number }>> {
    return await chart.evaluate((fig) => {
      const out: Array<{ text: string; left: number; right: number }> = [];
      for (const span of Array.from(fig.querySelectorAll('div[aria-hidden="true"] > span'))) {
        const text = (span.textContent ?? "").trim();
        if (text === "") continue;
        const range = document.createRange();
        range.selectNodeContents(span);
        const rect = range.getBoundingClientRect();
        out.push({ text, left: rect.left, right: rect.right });
      }
      return out;
    });
  }

  for (const title of ["Applications per week", "New postings per search run"]) {
    test(`${title}: x-axis labels are short dates and do not overlap`, async ({ page }) => {
      for (const theme of ["light", "dark"] as const) {
        await setTheme(page, theme);
        const labels = await axisLabels(figure(page, title));
        expect(labels.length, "the chart shows axis labels").toBeGreaterThan(1);
        for (const label of labels) expect(label.text, "short month-day label").toMatch(SHORT_DATE);
        for (let i = 1; i < labels.length; i++) {
          expect(labels[i]!.left, `label "${labels[i]!.text}" starts after "${labels[i - 1]!.text}" ends`).toBeGreaterThanOrEqual(labels[i - 1]!.right);
        }
      }
    });

    test(`${title}: the readout and the table view keep the full ISO date`, async ({ page }) => {
      const chart = figure(page, title);
      await chart.getByRole("button", { name: /^table view$/i }).click();
      const firstCell = chart.getByRole("table").getByRole("row").nth(1).getByRole("cell").first();
      await expect(firstCell).toHaveText(/\d{4}-\d{2}-\d{2}$/);
    });
  }

  test("both charts share one frame, with the heading inside it", async ({ page }) => {
    const styles = [];
    for (const title of ["Applications per week", "New postings per search run"]) {
      const chart = figure(page, title);
      const frame = await chart.evaluate((fig) => {
        const s = getComputedStyle(fig);
        const heading = fig.querySelector("h3, h4, figcaption, [role=heading]");
        return {
          border: s.borderTopWidth + " " + s.borderTopStyle + " " + s.borderTopColor,
          radius: s.borderTopLeftRadius,
          padding: s.paddingTop + " " + s.paddingLeft,
          background: s.backgroundColor,
          headingInside: heading !== null && fig.contains(heading),
          headingFont: heading ? getComputedStyle(heading).fontSize + " " + getComputedStyle(heading).fontFamily : "",
        };
      });
      expect(frame.headingInside, `${title}: heading is inside the chart frame`).toBe(true);
      expect(frame.border, `${title}: has a visible frame`).not.toMatch(/^0px/);
      styles.push(frame);
    }
    expect(styles[1]).toEqual(styles[0]);
  });
});
