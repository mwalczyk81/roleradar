import type { Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, defaultStubState, type AppHarness, type StubState } from "./support/app-harness.js";
import { settleForCapture } from "./support/capture-settle.js";
import { assertNotRealStackProject } from "./support/isolation.js";
import { startInsightsHarness, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";

/**
 * 010 US3: the intermittent `company-detail-errors` visual baseline failure (about 2000 differing pixels, always the
 * sticky sidebar, dark theme, under load). Playwright scrolls to the "Save Changes" button to click it, and the page
 * then reflows around the error banner, so the scroll offset is still settling when the capture starts. A full-page
 * capture shows a sticky sidebar at the current scroll offset, so a few pixels of offset move every sidebar glyph.
 * `settleForCapture` therefore has to wait on the real condition (scroll offset and layout unchanged for several
 * frames) and capture from a canonical position (the top). These checks reproduce the race on demand instead of
 * waiting for a loaded machine to hit it. Same fictional loopback stub as the other e2e specs.
 */

const SESSION_ID = "e2e-capture-settle-session";

test.describe("Capture settling (fictional loopback stub)", () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  const state: StubState = defaultStubState();
  let harness: AppHarness;

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startAppHarness(state, SESSION_ID);
  });

  test.afterAll(async () => {
    await harness?.stop();
  });

  async function openCompany(page: Page, context: Parameters<AppHarness["signIn"]>[0]): Promise<void> {
    await harness.signIn(context);
    await page.emulateMedia({ colorScheme: "dark", reducedMotion: "reduce" });
    await page.goto(`${harness.baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });
  }

  const sidebarTop = (page: Page): Promise<number> =>
    page.evaluate(() => {
      const aside = document.querySelector("aside, nav");
      return aside ? Math.round(aside.getBoundingClientRect().top + window.scrollY) : -1;
    });

  const scrollY = (page: Page): Promise<number> => page.evaluate(() => Math.round(window.scrollY));

  test("a page scrolled down (as the click on Save Changes leaves it) is captured from the top", async ({ page, context }) => {
    await openCompany(page, context);
    const restingTop = await sidebarTop(page);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    expect(await scrollY(page), "precondition: the page really is scrolled").toBeGreaterThan(100);

    await settleForCapture(page);

    expect(await scrollY(page)).toBe(0);
    expect(await sidebarTop(page)).toBe(restingTop);
  });

  test("a scroll that is still moving when the capture starts is waited out, not captured mid-way", async ({ page, context }) => {
    await openCompany(page, context);
    // A scroll that keeps moving for about 400 ms in a few-pixel steps, like the reflow after the error banner.
    await page.evaluate(() => {
      let frame = 0;
      const tick = () => {
        window.scrollTo(0, 480 + (frame++ % 7));
        if (frame < 24) requestAnimationFrame(tick);
      };
      tick();
    });

    await settleForCapture(page);

    expect(await scrollY(page)).toBe(0);
    // Nothing moves it afterwards: ten more frames leave the offset and the sidebar where they are.
    const before = { y: await scrollY(page), top: await sidebarTop(page) };
    await page.evaluate(async () => {
      for (let i = 0; i < 10; i++) await new Promise((resolve) => requestAnimationFrame(resolve));
    });
    expect({ y: await scrollY(page), top: await sidebarTop(page) }).toEqual(before);
  });

  test("it reports which geometry never settled instead of capturing an unstable page", async ({ page, context }) => {
    await openCompany(page, context);
    // A layout that changes on every frame for longer than the settle may wait.
    await page.evaluate(() => {
      const main = document.querySelector("main") ?? document.body;
      let flip = false;
      const tick = () => {
        (main as HTMLElement).style.paddingTop = flip ? "1px" : "2px";
        flip = !flip;
        requestAnimationFrame(tick);
      };
      tick();
    });

    await expect(settleForCapture(page)).rejects.toThrow(/did not settle/);
  });
});

/**
 * 010 close-out review: `settleForCapture` scrolled to the top after the pointer was positioned, so on a page where the
 * hover target is below the fold the pointer ended over the funnel chart (a tooltip on the Leads bar and a highlighted
 * row in the `insights-drill-link-hover` capture). A hover capture must keep the scroll position the hover left, so the
 * pointer stays on the target, and show only the target's own hover state.
 */
test.describe("Capture settling keeps a hover (fictional loopback stub)", () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  const snapshot = loadSnapshot();
  let insights: InsightsHarness;

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    insights = await startInsightsHarness("e2e-capture-settle-insights-session");
  });

  test.afterAll(async () => {
    await insights?.close();
  });

  test("the drill-link hover capture state has the link hovered and no chart tooltip or hovered chart row", async ({ page, context }) => {
    await insights.signIn(context);
    await page.emulateMedia({ colorScheme: "light", reducedMotion: "reduce" });
    await page.goto(`${insights.baseUrl}/insights?window=90d&asOf=${snapshot.asOf}`, { waitUntil: "networkidle" });

    const link = page.locator('a[data-role="numerator"]').first();
    await link.hover();
    const scrolledBy = await page.evaluate(() => Math.round(window.scrollY));

    await settleForCapture(page, { keepScroll: true });

    expect(await link.evaluate((element) => element.matches(":hover")), "the pointer is still on the drill link").toBe(true);
    expect(await page.evaluate(() => Math.round(window.scrollY)), "a hover capture does not move the scroll position").toBe(scrolledBy);
    expect(await page.locator(".ds-tooltip-pos[data-open]").count(), "no chart tooltip is open").toBe(0);
    expect(await page.locator(".ds-chart-row:hover, .ds-chart-slot:hover").count(), "no chart row is hovered").toBe(0);
  });

  test("without keepScroll the capture goes to the top (the default is unchanged)", async ({ page, context }) => {
    await insights.signIn(context);
    await page.goto(`${insights.baseUrl}/insights?window=90d&asOf=${snapshot.asOf}`, { waitUntil: "networkidle" });
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));

    await settleForCapture(page);

    expect(await page.evaluate(() => Math.round(window.scrollY))).toBe(0);
  });
});
