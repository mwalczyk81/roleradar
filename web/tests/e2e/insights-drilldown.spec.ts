import { type Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startInsightsHarness, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { assertNotRealStackProject } from "./support/isolation.js";

/**
 * T047: every number on the Insights page is a link, and following it lands on a list whose displayed total equals
 * that number. Covered: all 7 funnel bars, the offers-without-interview note, the rate numerators and denominators,
 * the open count and the rejection split, in both windows. Loopback stub and fictional fixture only.
 */

const SESSION_ID = "e2e-insights-drilldown-session";
const AS_OF = loadSnapshot().asOf;

let harness: InsightsHarness;

interface DashboardLink {
  lens: string;
  role: string;
  href: string;
  number: number;
  text: string;
}

async function dashboardLinks(page: Page): Promise<DashboardLink[]> {
  return page.$$eval("a[data-lens]", (anchors) =>
    anchors.map((a) => {
      const text = (a.textContent ?? "").trim();
      const match = /\d+/.exec(text);
      return {
        lens: a.getAttribute("data-lens") ?? "",
        role: a.getAttribute("data-role") ?? "",
        href: a.getAttribute("href") ?? "",
        number: match ? Number(match[0]) : Number.NaN,
        text,
      };
    })
  );
}

/** The list's displayed total: "Showing a-b of N", or "No results" for an empty list. */
async function listTotal(page: Page): Promise<number> {
  const summary = page.getByText(/Showing \d+–\d+ of \d+|No results/).first();
  await expect(summary).toBeVisible();
  const text = (await summary.textContent()) ?? "";
  const match = /of (\d+)/.exec(text);
  return match ? Number(match[1]) : 0;
}

test.describe("Insights drill-down parity (real Chromium, fictional loopback stub)", () => {
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
    harness.unexpectedFilters.length = 0;
  });

  for (const window of ["90d", "all"] as const) {
    test(`every dashboard number equals the total of the list it links to (${window})`, async ({ page }) => {
      test.setTimeout(120_000);
      await page.goto(`${harness.baseUrl}/insights?window=${window}&asOf=${AS_OF}`, { waitUntil: "networkidle" });

      const links = await dashboardLinks(page);

      // 7 funnel bars + the offers note + rate numerators and denominators + the open count and the rejection split.
      expect(links.filter((l) => l.role === "stage")).toHaveLength(7);
      expect(links.filter((l) => l.role === "note")).toHaveLength(1);
      expect(links.filter((l) => l.role === "numerator").length).toBeGreaterThanOrEqual(8);
      expect(links.filter((l) => l.role === "denominator").length).toBeGreaterThanOrEqual(8);
      expect(links.filter((l) => l.role === "count")).toHaveLength(3);

      const checked = new Set<string>();
      for (const link of links) {
        expect(Number.isNaN(link.number), `no number in link "${link.text}"`).toBe(false);
        // The same lens and window can appear more than once (a funnel bar and a rate); check each distinct pair once.
        const id = `${link.href}|${link.number}`;
        if (checked.has(id)) continue;
        checked.add(id);

        const expectedNumber = link.role === "note" ? link.number : link.number;
        await page.goto(`${harness.baseUrl}${link.href}`, { waitUntil: "networkidle" });
        await expect(page.getByText(/Insights:|Remove filter Insights/).first()).toBeVisible();

        const total = await listTotal(page);
        expect(total, `${link.role} ${link.lens} (${link.href}) says ${expectedNumber}, its list says ${total}`).toBe(expectedNumber);
      }

      // The stub answers only filters it was given as lens filters, so every list request used a lens filter verbatim.
      expect(harness.unexpectedFilters).toEqual([]);
    });
  }

  test("clicking a funnel bar's link opens the matching list with a removable lens chip", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=${AS_OF}`, { waitUntil: "networkidle" });

    await page.getByRole("link", { name: /^Responded: \d+$/ }).click();
    await expect(page).toHaveURL(new RegExp(`/applications\\?lens=responded&window=90d&asOf=${AS_OF}`));
    await expect(page.getByText(/Insights: Responded/)).toBeVisible();

    await page.getByRole("button", { name: /Remove filter Insights/ }).click();
    await expect(page).not.toHaveURL(/lens=/);
  });
});
