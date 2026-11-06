import { type Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import fs from "node:fs";
import path from "node:path";
import { startInsightsHarness, loadNoteFixture, type InsightsHarness } from "./support/insights-stub.js";
import { checkAxeInThemes } from "./support/axe.js";
import { assertNotRealStackProject } from "./support/isolation.js";

/**
 * T083: a hostile fictional note rendered as Markdown in real Chromium, against the loopback stub only (the isolation
 * guard from T050 runs in beforeAll). Nothing in the note may execute, load, or leave the app's origin.
 * Set NOTES_SCREENSHOT_DIR to also write review screenshots.
 */

const SESSION_ID = "e2e-notes-markdown-session";
const NOTE = loadNoteFixture();

let harness: InsightsHarness;

interface Watch {
  requests: string[];
  failures: string[];
  consoleMessages: string[];
  dialogs: string[];
  pageErrors: string[];
}

function watch(page: Page): Watch {
  const seen: Watch = { requests: [], failures: [], consoleMessages: [], dialogs: [], pageErrors: [] };
  page.on("request", (request) => seen.requests.push(request.url()));
  page.on("requestfailed", (request) => seen.failures.push(`${request.url()} ${request.failure()?.errorText ?? ""}`));
  page.on("console", (msg) => {
    if (msg.type() === "error" || msg.type() === "warning" || msg.text().toLowerCase().includes("content security policy")) {
      seen.consoleMessages.push(`${msg.type()}: ${msg.text()}`);
    }
  });
  page.on("pageerror", (error) => seen.pageErrors.push(error.message));
  page.on("dialog", async (dialog) => {
    seen.dialogs.push(dialog.message());
    await dialog.dismiss();
  });
  return seen;
}

async function shot(page: Page, name: string) {
  const dir = process.env.NOTES_SCREENSHOT_DIR;
  if (dir) {
    fs.mkdirSync(dir, { recursive: true });
    await page.screenshot({ path: path.join(dir, `${name}.png`), fullPage: true });
  }
}

function body(page: Page) {
  return page.locator(".markdown-body");
}

test.describe("Notes Markdown (real Chromium, fictional loopback stub)", () => {
  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startInsightsHarness(SESSION_ID);
  });

  test.afterAll(async () => {
    await harness?.close();
  });

  test.beforeEach(async ({ context }) => {
    await harness.signIn(context);
    harness.requests.length = 0;
  });

  test("renders the hostile note as formatted text: nothing executes, nothing loads, nothing leaves the app origin", async ({ page }) => {
    const seen = watch(page);
    await page.goto(`${harness.baseUrl}/notes/${NOTE.id}`, { waitUntil: "networkidle" });

    // The page's own h1 is the only h1, and the note heading stays a text node in the card's h2.
    await expect(page.getByRole("heading", { level: 1 })).toHaveCount(1);
    await expect(page.getByRole("heading", { level: 2, name: NOTE.heading })).toBeVisible();
    await expect(body(page)).toBeVisible();
    await expect(body(page).getByRole("heading", { level: 3, name: "Top level heading" })).toBeVisible();
    await expect(body(page).getByRole("heading", { level: 4, name: "Wide table" })).toBeVisible();

    // Formatting happened.
    await expect(body(page).locator("strong")).toHaveText("bold");
    await expect(body(page).locator("del")).toHaveText("struck");
    await expect(body(page).locator("pre code")).toContainText("code <b>not bold</b> stays literal");

    // Hostile markup is visible as text and absent as elements.
    await expect(body(page)).toContainText("<script>window.__pwned = 'script'</script>");
    await expect(body(page)).toContainText('onerror="window.__pwned');
    for (const tag of ["script", "img", "iframe", "style", "link", "form", "object", "embed"]) {
      await expect(body(page).locator(tag), tag).toHaveCount(0);
    }
    await expect(body(page).locator("[id], [name], [style], [onerror], [onclick]")).toHaveCount(0);
    await expect(body(page).getByText("tracker", { exact: true })).toBeVisible(); // image alt text only
    await expect(body(page).getByText("logo", { exact: true })).toBeVisible();

    // Only the three links the author wrote with an explicit https scheme are anchors, each hardened.
    const anchors = body(page).locator("a");
    await expect(anchors).toHaveCount(3);
    expect(await anchors.evaluateAll((els) => els.map((e) => e.getAttribute("href")))).toEqual([
      "https://example.com/safe",
      "https://example.com/bare",
      "https://example.com/home",
    ]);
    expect(await anchors.evaluateAll((els) => els.map((e) => e.getAttribute("rel")))).toEqual([
      "noopener noreferrer",
      "noopener noreferrer",
      "noopener noreferrer",
    ]);
    expect(await anchors.evaluateAll((els) => els.map((e) => e.hasAttribute("target")))).toEqual([false, false, false]);
    await expect(body(page)).toContainText("www.example.com");
    await expect(body(page)).toContainText("person@example.com");

    // Task list is read-only.
    const boxes = body(page).locator('input[type="checkbox"]');
    await expect(boxes).toHaveCount(2);
    await expect(boxes.first()).toBeDisabled();
    await expect(boxes.first()).toBeChecked();
    await expect(boxes.last()).toBeDisabled();
    await expect(boxes.last()).not.toBeChecked();

    // Nothing ran.
    expect(await page.evaluate(() => (window as unknown as { __pwned?: string }).__pwned)).toBeUndefined();
    expect(seen.dialogs).toEqual([]);
    expect(seen.pageErrors).toEqual([]);
    // No CSP violation, no console error or warning of any kind.
    expect(seen.consoleMessages).toEqual([]);
    expect(seen.failures).toEqual([]);

    // Zero requests to any origin other than the app's, and none that mention the hostile hosts.
    const origins = new Set(
      seen.requests.filter((u) => /^(https?|wss?):/i.test(u)).map((u) => new URL(u).origin)
    );
    expect([...origins]).toEqual([harness.baseUrl]);
    expect(seen.requests.filter((u) => u.includes("evil.example") || u.includes("example.com"))).toEqual([]);

    // The web server asked the stub for this one note and nothing else.
    expect(harness.requests.map((r) => r.pathname)).toEqual([`/api/v1/notes/${NOTE.id}`]);

    await shot(page, "notes-markdown-desktop");
  });

  test("shows the same note with no inline style attribute coming from the markdown, and passes axe in both themes", async ({ page }) => {
    await page.goto(`${harness.baseUrl}/notes/${NOTE.id}`, { waitUntil: "networkidle" });
    await expect(body(page)).toBeVisible();

    await expect(page.locator(".markdown-body [style], .markdown-body *[style]")).toHaveCount(0);
    await checkAxeInThemes(page);
  });

  test("keeps the page from scrolling sideways at 360 px; the wide table scrolls inside a keyboard-focusable region", async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 800 });
    await page.goto(`${harness.baseUrl}/notes/${NOTE.id}`, { waitUntil: "networkidle" });

    const region = page.getByRole("region", { name: "Table 1" });
    await expect(region).toBeVisible();
    await expect(region).toHaveAttribute("tabindex", "0");

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, "horizontal page scroll at 360px").toBeLessThanOrEqual(0);

    const sizes = await region.evaluate((el) => ({ scroll: el.scrollWidth, client: el.clientWidth }));
    expect(sizes.scroll, "the table is wider than its region").toBeGreaterThan(sizes.client);

    await region.focus();
    await expect(region).toBeFocused();
    await page.keyboard.press("ArrowRight");
    await expect.poll(() => region.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0);

    await checkAxeInThemes(page);
    await shot(page, "notes-markdown-phone");
  });

  test("dark theme renders the note (review screenshot)", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce", colorScheme: "dark" });
    await page.goto(`${harness.baseUrl}/notes/${NOTE.id}`, { waitUntil: "networkidle" });
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
    await expect(body(page)).toBeVisible();
    await shot(page, "notes-markdown-dark");
  });
});
