import type { Locator } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, defaultStubState, type AppHarness } from "./support/app-harness.js";
import { startInsightsHarness, loadNoteFixture, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { checkAxeInThemes } from "./support/axe.js";
import { assertNotRealStackProject } from "./support/isolation.js";
import { contrastRatio, paintedBackground, parseCssColour, setTheme, settleAnimations, textColour, textContrast, tokenColour } from "./support/contrast.js";
import fs from "node:fs";
import path from "node:path";

/**
 * 009 phase 4 (US3): the polish items as computed-style, contrast and layout assertions, in both themes. Fictional
 * loopback stubs only. Aesthetic calls (J1 to J8) are judged by the owner; what is checked here is what can be measured.
 */

const THEMES = ["light", "dark"] as const;
const AA_TEXT = 4.5;
const AA_NON_TEXT = 3;
const AS_OF = loadSnapshot().asOf;

const app = { state: defaultStubState(), harness: undefined as AppHarness | undefined };
let insights: InsightsHarness;

function appUrl(route: string): string {
  if (!app.harness) throw new Error("app harness not started");
  return `${app.harness.baseUrl}${route}`;
}

/** The visible focus indicator: a real outline (or a box shadow) whose colour has non-text contrast against the page. */
async function expectVisibleFocusIndicator(locator: Locator): Promise<void> {
  const focus = await locator.evaluate((el) => {
    const style = getComputedStyle(el);
    return { visible: el.matches(":focus-visible"), outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth, outlineColor: style.outlineColor, boxShadow: style.boxShadow };
  });
  expect(focus.visible, "the element matches :focus-visible").toBe(true);
  const hasOutline = focus.outlineStyle !== "none" && parseFloat(focus.outlineWidth) > 0;
  expect(hasOutline || focus.boxShadow !== "none", `focus indicator (${JSON.stringify(focus)})`).toBe(true);
  if (hasOutline) {
    const ring = parseCssColour(focus.outlineColor);
    const background = await paintedBackground(locator);
    expect(contrastRatio(ring, background), "focus ring contrast against the page").toBeGreaterThanOrEqual(AA_NON_TEXT);
  }
}

test.describe("polish: postings list and applications list", () => {
  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    app.harness = await startAppHarness(app.state, "e2e-polish-app-session");
  });
  test.afterAll(async () => {
    await app.harness?.stop();
  });
  test.beforeEach(async ({ context }) => {
    Object.assign(app.state, defaultStubState());
    await app.harness?.signIn(context);
  });

  for (const theme of THEMES) {
    test(`role-title link: text colour at rest, bronze on hover and focus, AA and a focus ring (${theme})`, async ({ page }) => {
      await page.goto(appUrl("/postings"), { waitUntil: "networkidle" });
      await setTheme(page, theme);
      const link = page.locator('table a[href^="/postings/"]').first();
      const text = await tokenColour(page, "--ds-text");
      const bronze = await tokenColour(page, "--ds-primary-subtle-text");

      await page.mouse.move(0, 0);

      await settleAnimations(page);
      expect(await textColour(link), "colour at rest equals the text token").toBe(text);
      expect(await textContrast(link), "contrast at rest").toBeGreaterThanOrEqual(AA_TEXT);

      await link.hover();

      await settleAnimations(page);
      expect(await textColour(link), "colour on hover equals the bronze token").toBe(bronze);
      expect(await textContrast(link), "contrast on hover against the hovered row").toBeGreaterThanOrEqual(AA_TEXT);

      await page.mouse.move(0, 0);

      await settleAnimations(page);
      await link.focus();
      await settleAnimations(page);
      expect(await textColour(link), "colour on focus equals the bronze token").toBe(bronze);
      expect(await textContrast(link), "contrast on focus").toBeGreaterThanOrEqual(AA_TEXT);
      await expectVisibleFocusIndicator(link);
    });

    test(`applications list title link follows the same states, and no colour names an undefined token (${theme})`, async ({ page }) => {
      await page.goto(appUrl("/applications"), { waitUntil: "networkidle" });
      await setTheme(page, theme);
      const link = page.locator('table a[href^="/applications/"]').first();
      const text = await tokenColour(page, "--ds-text");
      const bronze = await tokenColour(page, "--ds-primary-subtle-text");
      await page.mouse.move(0, 0);
      await settleAnimations(page);
      expect(await textColour(link)).toBe(text);
      await link.hover();
      await settleAnimations(page);
      expect(await textColour(link)).toBe(bronze);
      expect(await textContrast(link)).toBeGreaterThanOrEqual(AA_TEXT);
      await page.mouse.move(0, 0);
      await settleAnimations(page);
      await link.focus();
      await settleAnimations(page);
      await expectVisibleFocusIndicator(link);
    });

    test(`Min Score placeholder meets 4.5:1 (${theme})`, async ({ page }, testInfo) => {
      await page.goto(appUrl("/postings"), { waitUntil: "networkidle" });
      await setTheme(page, theme);
      const input = page.getByLabel("Filter by Minimum Score");
      const placeholder = await input.evaluate((el) => getComputedStyle(el, "::placeholder").color);
      const background = await paintedBackground(input);
      const foreground = parseCssColour(placeholder);
      const ratio = contrastRatio(foreground, background);
      testInfo.annotations.push({ type: "placeholder-ratio", description: `${theme}: ${placeholder} on ${JSON.stringify(background)} = ${ratio.toFixed(2)}` });
      console.log(`placeholder-ratio ${theme}: ${placeholder} = ${ratio.toFixed(2)}`);
      expect(ratio, `placeholder ${placeholder}`).toBeGreaterThanOrEqual(AA_TEXT);
    });
  }

  test("no app rule names the undefined --ds-text-primary token", async () => {
    const css = fs.readFileSync(path.resolve(process.cwd(), "app", "app.css"), "utf8");
    expect(css).not.toContain("--ds-text-primary");
  });

  test("the timeline note text uses a defined colour token", async ({ page }) => {
    app.state.currentApplicationEvents = [{ eventType: "note", eventDate: "2026-11-02", notes: "A fictional note." }];
    await page.goto(appUrl("/applications/app-e2e-1"), { waitUntil: "networkidle" });
    for (const theme of THEMES) {
      await setTheme(page, theme);
      const notes = page.locator("p.timeline-event-notes");
      expect(await textColour(notes)).toBe(await tokenColour(page, "--ds-text"));
      expect(await textContrast(notes)).toBeGreaterThanOrEqual(AA_TEXT);
    }
  });

  test("identity: Plex Serif headings, Plex Sans body and a kit primary colour on /postings in both themes", async ({ page }) => {
    await page.goto(appUrl("/postings"), { waitUntil: "networkidle" });
    for (const theme of THEMES) {
      await setTheme(page, theme);
      const heading = await page.locator("h1, h2").first().evaluate((el) => getComputedStyle(el).fontFamily);
      expect(heading.replace(/["']/g, "")).toMatch(/^IBM Plex Serif/);
      const body = await page.locator("table td").first().evaluate((el) => getComputedStyle(el).fontFamily);
      expect(body.replace(/["']/g, "")).toMatch(/^IBM Plex Sans/);
      const link = page.locator('table a[href^="/postings/"]').first();
      await link.hover();
      await settleAnimations(page);
      expect(await textColour(link)).toBe(await tokenColour(page, "--ds-primary-subtle-text"));
    }
  });

  test("axe: /postings in both themes", async ({ page }) => {
    await page.goto(appUrl("/postings"), { waitUntil: "networkidle" });
    await checkAxeInThemes(page);
  });

  test("markdown headings h4, h5 and h6 differ from body text and from each other", async ({ page }) => {
    app.state.currentNoteDetail = {
      id: "note-e2e-1",
      heading: "Fictional heading levels",
      content: [
        "Opening paragraph.",
        "",
        "# Heading three",
        "",
        "## Heading four",
        "",
        "Paragraph under four.",
        "",
        "### Heading five",
        "",
        "#### Heading six",
        "",
        "Paragraph under six.",
        "",
      ].join("\n"),
      tags: [],
      linkedEntityType: "",
      linkedEntityId: null,
      createdAt: "2026-10-15T12:00:00Z",
      updatedAt: "2026-10-16T12:00:00Z",
    };
    await page.goto(appUrl("/notes/note-e2e-1"), { waitUntil: "networkidle" });
    const signature = async (selector: string): Promise<string> =>
      await page.locator(selector).first().evaluate((el) => {
        const s = getComputedStyle(el);
        return [s.fontSize, s.fontWeight, s.textTransform, s.fontVariantCaps, s.letterSpacing, s.fontFamily].join("|");
      });
    for (const theme of THEMES) {
      await setTheme(page, theme);
      const p = await signature(".markdown-body > p");
      const h4 = await signature(".markdown-body h4");
      const h5 = await signature(".markdown-body h5");
      const h6 = await signature(".markdown-body h6");
      expect(h4, "h4 differs from body text").not.toBe(p);
      expect(h5, "h5 differs from body text").not.toBe(p);
      expect(h6, "h6 differs from body text").not.toBe(p);
      expect(h4, "h4 differs from h5").not.toBe(h5);
      expect(h5, "h5 differs from h6").not.toBe(h6);
      for (const level of ["h4", "h5", "h6"]) {
        expect(await textContrast(page.locator(`.markdown-body ${level}`).first()), `${level} contrast`).toBeGreaterThanOrEqual(AA_TEXT);
      }
    }
  });
});

test.describe("polish: Insights and the wide-table note", () => {
  test.use({ viewport: { width: 1280, height: 900 } });

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    insights = await startInsightsHarness("e2e-polish-insights-session");
  });
  test.afterAll(async () => {
    await insights?.close();
  });
  test.beforeEach(async ({ context }) => {
    await insights.signIn(context);
    insights.setCoverageMode("ok");
    insights.setStatsMode("ok");
    insights.setZeroedLens(null);
  });

  const insightsUrl = (window: "90d" | "all" = "90d") => `${insights.baseUrl}/insights?window=${window}&asOf=${AS_OF}`;

  for (const theme of THEMES) {
    test(`drill-down links: no underline at rest, underline on hover and focus, AA and a focus ring (${theme})`, async ({ page }) => {
      await page.goto(insightsUrl(), { waitUntil: "networkidle" });
      await setTheme(page, theme);
      for (const selector of [".insights-stage-links a", ".insights-tile-links a"]) {
        const link = page.locator(selector).first();
        await page.mouse.move(0, 0);
        await settleAnimations(page);
        const rest = await link.evaluate((el) => getComputedStyle(el).textDecorationLine);
        expect(rest, `${selector} underline at rest`).toBe("none");
        expect(await textContrast(link), `${selector} contrast at rest`).toBeGreaterThanOrEqual(AA_TEXT);

        await link.hover();

        await settleAnimations(page);
        expect(await link.evaluate((el) => getComputedStyle(el).textDecorationLine), `${selector} underline on hover`).toContain("underline");
        expect(await textContrast(link), `${selector} contrast on hover`).toBeGreaterThanOrEqual(AA_TEXT);

        await page.mouse.move(0, 0);

        await settleAnimations(page);
        await link.focus();
        await settleAnimations(page);
        expect(await link.evaluate((el) => getComputedStyle(el).textDecorationLine), `${selector} underline on focus`).toContain("underline");
        await expectVisibleFocusIndicator(link);
      }
    });

    test(`links inside help prose stay underlined (colour is not the only cue) and meet AA (${theme})`, async ({ page }) => {
      await page.goto(insightsUrl(), { waitUntil: "networkidle" });
      await setTheme(page, theme);
      const link = page.locator(".insights-help a").first();
      expect(await link.evaluate((el) => getComputedStyle(el).textDecorationLine)).toContain("underline");
      expect(await textContrast(link)).toBeGreaterThanOrEqual(AA_TEXT);
    });
  }

  test("tiles and the cards inside them have equal heights per grid row, including a No data tile", async ({ page }) => {
    for (const zeroed of [null, "ghost-eligible"] as const) {
      insights.setZeroedLens(zeroed);
      await page.goto(insightsUrl(), { waitUntil: "networkidle" });
      if (zeroed) await expect(page.getByText("No data", { exact: true }).first()).toBeVisible();
      const grids = await page.locator(".insights-tiles").evaluateAll((nodes) =>
        nodes.map((grid) => {
          const rows = new Map<number, Array<{ tile: number; card: number; links: number; help: number }>>();
          for (const tile of Array.from(grid.children)) {
            const box = tile.getBoundingClientRect();
            const card = tile.firstElementChild?.getBoundingClientRect().height ?? 0;
            const links = tile.querySelector(".insights-tile-links")?.getBoundingClientRect().top ?? -1;
            const help = tile.querySelector(".insights-help")?.getBoundingClientRect().top ?? -1;
            const key = Math.round(box.top);
            const row = rows.get(key) ?? [];
            row.push({ tile: Math.round(box.height), card: Math.round(card), links: Math.round(links - box.top), help: Math.round(help - box.top) });
            rows.set(key, row);
          }
          return Array.from(rows.values());
        })
      );
      for (const rows of grids) {
        for (const row of rows) {
          expect(new Set(row.map((r) => r.tile)).size, `tile heights in a row ${JSON.stringify(row)}`).toBe(1);
          expect(new Set(row.map((r) => r.card)).size, `card heights in a row ${JSON.stringify(row)}`).toBe(1);
          const withLinks = row.filter((r) => r.links >= 0);
          expect(new Set(withLinks.map((r) => r.links)).size, `link rows start at the same offset ${JSON.stringify(row)}`).toBeLessThanOrEqual(1);
        }
      }
      expect(grids.flat().some((row) => row.length > 1), "at least one row has more than one tile").toBe(true);
    }
  });

  test("'See all postings in this window' exists once, inside the postings funnel section", async ({ page }) => {
    await page.goto(insightsUrl(), { waitUntil: "networkidle" });
    const links = page.getByRole("link", { name: "See all postings in this window" });
    await expect(links).toHaveCount(1);
    await expect(page.locator('section[aria-labelledby="funnel-postings"]').getByRole("link", { name: "See all postings in this window" })).toHaveCount(1);
    await expect(page.locator("#how-calculated").getByRole("link", { name: "See all postings in this window" })).toHaveCount(0);
    expect(await links.getAttribute("href")).toContain("/postings");
  });

  test("the window control is a named nav with aria-current on the active link, and stays at the top", async ({ page }) => {
    for (const window of ["90d", "all"] as const) {
      await page.goto(insightsUrl(window), { waitUntil: "networkidle" });
      const nav = page.getByRole("navigation", { name: "Time window" });
      await expect(nav).toBeVisible();
      await expect(nav.locator('a[aria-current="page"]')).toHaveCount(1);
      await expect(nav.locator('a[aria-current="page"]')).toHaveText(window === "90d" ? "Last 90 days" : "All time");
      const navTop = (await nav.boundingBox())?.y ?? Infinity;
      const firstSection = (await page.locator("section.insights-section").first().boundingBox())?.y ?? 0;
      expect(navTop, "the window control sits above the first section").toBeLessThan(firstSection);
    }
  });

  test("search-run yield: text for the latest run and the point count, a named chart, a table view with every run", async ({ page }) => {
    const stats = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), "tests", "fixtures", "insights-stats-fictional.json"), "utf8")) as {
      last90Days: { searchRunYield: { runs: Array<{ startedAt: string; newPostings: number }> } };
    };
    const runs = stats.last90Days.searchRunYield.runs;
    const latest = runs[runs.length - 1];
    if (!latest) throw new Error("fixture has no runs");

    await page.goto(insightsUrl(), { waitUntil: "networkidle" });
    const trends = page.getByRole("region", { name: "Trends" });
    const summary = trends.getByTestId("run-yield-summary");
    await expect(summary).toContainText(`${latest.newPostings} new posting`);
    await expect(summary).toContainText(`${runs.length} run`);

    const chart = trends.getByRole("group", { name: "New postings per search run" });
    await expect(chart).toBeVisible();
    const figure = trends.locator("figure", { hasText: "New postings per search run" });
    console.log("run-chart-height", (await figure.boundingBox())?.height);
    expect((await figure.boundingBox())?.height ?? Infinity, "the run chart is small (about 120 high plus labels)").toBeLessThan(260);
    await figure.getByRole("button", { name: /^table view$/i }).click();
    const table = figure.getByRole("table");
    await expect(table.getByRole("row")).toHaveCount(runs.length + 1);
    await expect(table.getByRole("columnheader", { name: "Run date" })).toBeVisible();
    await expect(table.getByRole("columnheader", { name: "New postings" })).toBeVisible();

    await expect(page.locator(".insights-run-table, .insights-run-toggle, .insights-sparkline")).toHaveCount(0);
  });

  test("axe: /insights in both themes, and the wide-table note in both themes", async ({ page }) => {
    await page.goto(insightsUrl(), { waitUntil: "networkidle" });
    await checkAxeInThemes(page);
    await page.goto(`${insights.baseUrl}/notes/${loadNoteFixture().id}`, { waitUntil: "networkidle" });
    await checkAxeInThemes(page);
  });

  test("identity: Plex Serif headings, Plex Sans body and kit primary colours on /insights in both themes", async ({ page }) => {
    await page.goto(insightsUrl(), { waitUntil: "networkidle" });
    for (const theme of THEMES) {
      await setTheme(page, theme);
      const heading = await page.locator("h2.page-heading").first().evaluate((el) => getComputedStyle(el).fontFamily);
      expect(heading.replace(/["']/g, "")).toMatch(/^IBM Plex Serif/);
      const body = await page.locator(".insights-basis").first().evaluate((el) => getComputedStyle(el).fontFamily);
      expect(body.replace(/["']/g, "")).toMatch(/^IBM Plex Sans/);
      const current = page.locator('.insights-window-link[aria-current="page"]');
      expect(await textColour(current)).toBe(await tokenColour(page, "--ds-primary-subtle-text"));
    }
  });

  test("wide table: the scroll region uses the full card width, prose keeps its reading width, the page does not scroll sideways", async ({ page }) => {
    await page.goto(`${insights.baseUrl}/notes/${loadNoteFixture().id}`, { waitUntil: "networkidle" });
    const measured = await page.evaluate(() => {
      const card = document.querySelector('[data-testid="note-content"]') as HTMLElement;
      const cardStyle = getComputedStyle(card);
      const content = card.clientWidth - parseFloat(cardStyle.paddingLeft) - parseFloat(cardStyle.paddingRight);
      const region = document.querySelector(".markdown-table-scroll") as HTMLElement;
      const paragraph = document.querySelector(".markdown-body > p") as HTMLElement;
      const probe = document.createElement("div");
      probe.style.setProperty("width", "72ch");
      (document.querySelector(".markdown-body") as HTMLElement).appendChild(probe);
      const reading = probe.getBoundingClientRect().width;
      probe.remove();
      return {
        content,
        region: region.getBoundingClientRect().width,
        paragraph: paragraph.getBoundingClientRect().width,
        reading,
        overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        regionScrolls: region.scrollWidth > region.clientWidth,
      };
    });
    expect(Math.abs(measured.region - measured.content), `region ${measured.region} vs card content ${measured.content}`).toBeLessThanOrEqual(1);
    expect(measured.region, "the region is wider than the prose column").toBeGreaterThan(measured.paragraph);
    expect(measured.paragraph, "prose stays at the reading width").toBeLessThanOrEqual(measured.reading + 1);
    expect(measured.overflow, "horizontal page scroll").toBeLessThanOrEqual(0);
    expect(measured.regionScrolls, "the wide table scrolls inside its own region").toBe(true);
  });
});
