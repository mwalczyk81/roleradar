import fs from "node:fs";
import path from "node:path";
import { test, expect, type Page } from "@playwright/test";
import { settleForCapture } from "./support/capture-settle.js";
import {
  THEMES,
  assertDemoTarget,
  parseScreenshotManifest,
  readDemoCredentials,
  type DemoCredentials,
  type ScreenshotView,
  type Theme,
} from "./support/demo-capture.js";
import { totpCode } from "./support/totp.js";

/**
 * Feature 012: the README screenshots, captured from the running demo stack (deploy/demo/docker-compose.yml) in light
 * and dark. Skipped unless ROLERADAR_SCREENSHOT_DIR is set, like the visual baseline spec is skipped without
 * ROLERADAR_VISUAL_DIR.
 *
 *   ROLERADAR_SCREENSHOT_DIR=docs/images ROLERADAR_DEMO_URL=https://localhost:5103 \
 *   ROLERADAR_DEMO_USERNAME=... ROLERADAR_DEMO_PASSWORD=... ROLERADAR_DEMO_TOTP_SECRET=... \
 *   pnpm --filter web exec playwright test screenshots.spec.ts
 *
 * The credentials are the ones `roleradar demo credentials` printed; they are read from the environment only and are
 * never written anywhere. The target must be the demo web app (https, localhost, port 5103): any other address is
 * refused, so the spec cannot run against a real stack. The run writes exactly the files listed in
 * docs/images/manifest.json and nothing else. Every image is opened and reviewed by the owner before it is committed;
 * the seed's fictional names are the only invented text in them.
 */

const SCREENSHOT_DIR = process.env.ROLERADAR_SCREENSHOT_DIR;

interface View {
  name: ScreenshotView;
  open: (page: Page) => Promise<void>;
  /**
   * Tallest picture for this view, in pixels. A longer page is cut at the bottom of the last whole top-level section of
   * the main content that fits under this height (see sectionBoundary), never through the middle of a section.
   */
  maxHeight?: number;
}

const VIEWPORT_WIDTH = 1360;
const VIEWPORT_HEIGHT = 900;
const DEFAULT_MAX_HEIGHT = 1700;
/** Space kept under the last whole section, so the cut reads as the end of the picture. */
const SECTION_GAP = 24;

// The hero records and the drill-down lens are named in docs/images/manifest.json, next to the files they produce.
const MANIFEST = parseScreenshotManifest(fs.readFileSync(path.resolve(process.cwd(), "..", "docs", "images", "manifest.json"), "utf8"));
const HERO = MANIFEST.hero.records;

async function openRowLink(page: Page, list: string, rowTexts: string[], hrefPrefix: string): Promise<void> {
  await page.goto(list, { waitUntil: "networkidle" });
  let row = page.locator("tbody tr");
  for (const text of rowTexts) {
    row = row.filter({ hasText: text });
  }
  await row.locator(`a[href^="${hrefPrefix}"]`).first().click();
  await page.waitForLoadState("networkidle");
}

/**
 * Opens the Insights stage link for the manifest's drill-down lens (the "Interviewed: N" link of the applications
 * funnel) and checks that the list it lands on holds exactly N rows, so the pair of pictures shows a real drill-down.
 */
async function openDrillDown(page: Page, lens: string): Promise<void> {
  await page.goto("/insights", { waitUntil: "networkidle" });
  const link = page.locator(`a[data-role="stage"][data-lens="${lens}"]`).first();
  const label = (await link.textContent()) ?? "";
  const count = Number(/:\s*(\d+)\s*$/.exec(label)?.[1] ?? Number.NaN);
  expect(Number.isInteger(count) && count > 0, `the ${lens} stage link shows a positive count`).toBe(true);
  await link.click();
  await page.waitForURL((url) => url.searchParams.get("lens") === lens);
  await page.waitForLoadState("networkidle");
  await expect(page.locator("tbody tr")).toHaveCount(count);
}

// The hero records come from the manifest and match the fixed fictional data set (src/RoleRadar.Cli/Demo/DemoDataset.cs):
// the hero posting has an application that went through two interviews to an accepted offer, an assessment note, a
// posted range and opportunity signals, so its detail page and timeline show the most.
const VIEWS: View[] = [
  {
    name: "insights",
    maxHeight: 2000,
    open: async (page) => {
      await page.goto("/insights", { waitUntil: "networkidle" });
    },
  },
  {
    name: "insights-drilldown",
    open: (page) => openDrillDown(page, HERO.drilldown),
  },
  {
    name: "postings-list",
    open: async (page) => {
      await page.goto("/postings", { waitUntil: "networkidle" });
    },
  },
  {
    name: "posting-detail",
    open: (page) => openRowLink(page, "/postings", [HERO.company, HERO.posting], "/postings/"),
  },
  {
    name: "application-timeline",
    open: (page) => openRowLink(page, "/applications", [HERO.company, HERO.posting], "/applications/"),
  },
  {
    name: "company",
    open: async (page) => {
      await page.goto("/companies", { waitUntil: "networkidle" });
      await page.getByRole("link", { name: HERO.company }).first().click();
      await page.waitForLoadState("networkidle");
    },
  },
  {
    name: "note",
    open: async (page) => {
      await page.goto("/notes", { waitUntil: "networkidle" });
      await page.getByRole("link", { name: HERO.note }).first().click();
      await page.waitForLoadState("networkidle");
    },
  },
];

async function signIn(page: Page, credentials: DemoCredentials, origin: string): Promise<void> {
  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByLabel("Username").fill(credentials.username);
  await page.getByLabel("Password").fill(credentials.password);
  await page.getByRole("button", { name: "Sign In" }).click();
  const codeField = page.getByLabel("6-Digit Authenticator Code");
  await codeField.waitFor();
  await codeField.fill(totpCode(credentials.totpSecret));
  await page.getByRole("button", { name: "Verify Code" }).click();
  await page.waitForURL((url) => url.origin === origin);
}

test.describe.configure({ mode: "serial" });

test.describe("Demo screenshots (running demo stack only)", () => {
  test.skip(!SCREENSHOT_DIR, "Set ROLERADAR_SCREENSHOT_DIR (and the ROLERADAR_DEMO_* variables) to capture the README screenshots from the running demo.");
  test.use({ viewport: { width: VIEWPORT_WIDTH, height: VIEWPORT_HEIGHT } });

  test("captures seven views in light and dark into the screenshot folder", async ({ browser }) => {
    test.setTimeout(240_000);
    const outputDir = path.resolve(SCREENSHOT_DIR ?? "");
    const target = assertDemoTarget(process.env.ROLERADAR_DEMO_URL);
    const credentials = readDemoCredentials(process.env);
    fs.mkdirSync(outputDir, { recursive: true });

    // Self-signed demo certificates: HTTPS errors are ignored for this context.
    const context = await browser.newContext({ baseURL: target.origin, ignoreHTTPSErrors: true, viewport: { width: VIEWPORT_WIDTH, height: VIEWPORT_HEIGHT } });
    const page = await context.newPage();
    await signIn(page, credentials, target.origin);

    for (const theme of THEMES) {
      await applyTheme(page, theme, target.origin);
      for (const view of VIEWS) {
        await page.setViewportSize({ width: VIEWPORT_WIDTH, height: VIEWPORT_HEIGHT });
        await view.open(page);
        await expect(page.locator("main")).toBeVisible();
        // The sidebar is as tall as the viewport, so the picture is a viewport as tall as the page (up to the cap)
        // rather than a full-page capture, which would leave the sidebar short of the page height.
        await settleForCapture(page);
        const height = await sectionBoundary(page, view.maxHeight ?? DEFAULT_MAX_HEIGHT);
        await page.setViewportSize({ width: VIEWPORT_WIDTH, height: Math.max(VIEWPORT_HEIGHT, height) });
        // A view opened by a click leaves the pointer over the page, which would show a hover highlight on a row.
        await page.mouse.move(0, 0);
        await settleForCapture(page);
        await page.screenshot({
          path: path.join(outputDir, `${view.name}-${theme}.png`),
          animations: "disabled",
          caret: "hide",
        });
      }
    }

    await context.close();
  });
});

/**
 * Picture height for the current page: the whole page when it fits under the cap; otherwise the bottom of the last
 * top-level `main section` that ends under the cap (plus a small gap), so the picture never stops mid-section. A page
 * with no section that fits falls back to the cap.
 */
async function sectionBoundary(page: Page, cap: number): Promise<number> {
  return page.evaluate(
    ({ limit, gap }) => {
      const pageHeight = document.documentElement.scrollHeight;
      if (pageHeight <= limit) {
        return pageHeight;
      }
      const sections = Array.from(document.querySelectorAll("main section")).filter((el) => !el.parentElement?.closest("main section"));
      const bottoms = sections.map((el) => Math.ceil(el.getBoundingClientRect().bottom + window.scrollY)).filter((bottom) => bottom + gap <= limit);
      return bottoms.length > 0 ? Math.max(...bottoms) + gap : limit;
    },
    { limit: cap, gap: SECTION_GAP },
  );
}

async function applyTheme(page: Page, theme: Theme, origin: string): Promise<void> {
  await page.context().addCookies([{ name: "roleradar_theme", value: theme, url: origin }]);
  await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });
}
