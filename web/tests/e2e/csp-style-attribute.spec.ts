import { type Page, type BrowserContext } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, type AppHarness, type StubState } from "./support/app-harness.js";
import axe from "axe-core";
import { saveSession } from "../../app/lib/session.server.js";

let baseUrl: string;
const E2E_SESSION_ID = "e2e-auth-session-id-playwright";

let currentPostingDisposition = "lead";
let currentPostingVersionToken = "ver-1";
let currentPostingTitle = "Principal Engineer";
let nextTransitionResponseStatus: number | null = null;
let nextTransitionResponseBody: unknown = null;

let currentCompanyName = "Acme Cloud Services";
let currentCompanyStatus = "target";
let currentCompanyOwnership = "private";
let currentCompanyDescription = "Cloud hosting and observability tools";
let currentCompanyHeadquarters = "Seattle, WA";
let currentCompanyCategoryTags = ["cloud", "devops"];
let currentCompanyCareerSources = [
  {
    sourceType: "ats",
    atsProvider: "greenhouse",
    atsTenant: "acmecloud",
    sourceUrl: "https://boards.greenhouse.io/acmecloud",
    fetchMethod: "api",
    active: true,
  },
];
let currentCompanyVersionToken = "comp-ver-1";
let nextCompanyResponseStatus: number | null = null;
let nextCompanyResponseBody: unknown = null;
let lastCompaniesQueryReceived: Record<string, string> = {};

let currentApplicationStatus = "applied";
let currentApplicationEvents: Array<{ eventType: string; eventDate: string; notes: string | null }> = [
  { eventType: "applied", eventDate: "2026-10-31", notes: "Initial application submitted" },
];
let nextApplicationResponseStatus: number | null = null;
let nextApplicationResponseBody: unknown = null;
let lastApplicationsQueryReceived: Record<string, string> = {};

let lastCriteriaQueryReceived: Record<string, string> = {};
let currentCriteriaList: Array<Record<string, unknown>> | null = null;

let lastNotesQueryReceived: Record<string, string> = {};
let currentNotesList: Array<Record<string, unknown>> = [];
let currentNoteDetail: Record<string, unknown> | null = null;

let lastSearchRunsQueryReceived: Record<string, string> = {};
let currentSearchRunsList: Array<Record<string, unknown>> = [];

let harness: AppHarness;

const stubState: StubState = {
  get currentPostingDisposition() { return currentPostingDisposition; },
  set currentPostingDisposition(v) { currentPostingDisposition = v; },
  get currentPostingVersionToken() { return currentPostingVersionToken; },
  set currentPostingVersionToken(v) { currentPostingVersionToken = v; },
  get currentPostingTitle() { return currentPostingTitle; },
  set currentPostingTitle(v) { currentPostingTitle = v; },
  get nextTransitionResponseStatus() { return nextTransitionResponseStatus; },
  set nextTransitionResponseStatus(v) { nextTransitionResponseStatus = v; },
  get nextTransitionResponseBody() { return nextTransitionResponseBody; },
  set nextTransitionResponseBody(v) { nextTransitionResponseBody = v; },
  get currentCompanyName() { return currentCompanyName; },
  set currentCompanyName(v) { currentCompanyName = v; },
  get currentCompanyStatus() { return currentCompanyStatus; },
  set currentCompanyStatus(v) { currentCompanyStatus = v; },
  get currentCompanyOwnership() { return currentCompanyOwnership; },
  set currentCompanyOwnership(v) { currentCompanyOwnership = v; },
  get currentCompanyDescription() { return currentCompanyDescription; },
  set currentCompanyDescription(v) { currentCompanyDescription = v; },
  get currentCompanyHeadquarters() { return currentCompanyHeadquarters; },
  set currentCompanyHeadquarters(v) { currentCompanyHeadquarters = v; },
  get currentCompanyCategoryTags() { return currentCompanyCategoryTags; },
  set currentCompanyCategoryTags(v) { currentCompanyCategoryTags = v; },
  get currentCompanyCareerSources() { return currentCompanyCareerSources; },
  set currentCompanyCareerSources(v) { currentCompanyCareerSources = v; },
  get currentCompanyVersionToken() { return currentCompanyVersionToken; },
  set currentCompanyVersionToken(v) { currentCompanyVersionToken = v; },
  get nextCompanyResponseStatus() { return nextCompanyResponseStatus; },
  set nextCompanyResponseStatus(v) { nextCompanyResponseStatus = v; },
  get nextCompanyResponseBody() { return nextCompanyResponseBody; },
  set nextCompanyResponseBody(v) { nextCompanyResponseBody = v; },
  get lastCompaniesQueryReceived() { return lastCompaniesQueryReceived; },
  set lastCompaniesQueryReceived(v) { lastCompaniesQueryReceived = v; },
  get currentApplicationStatus() { return currentApplicationStatus; },
  set currentApplicationStatus(v) { currentApplicationStatus = v; },
  get currentApplicationEvents() { return currentApplicationEvents; },
  set currentApplicationEvents(v) { currentApplicationEvents = v; },
  get nextApplicationResponseStatus() { return nextApplicationResponseStatus; },
  set nextApplicationResponseStatus(v) { nextApplicationResponseStatus = v; },
  get nextApplicationResponseBody() { return nextApplicationResponseBody; },
  set nextApplicationResponseBody(v) { nextApplicationResponseBody = v; },
  get lastApplicationsQueryReceived() { return lastApplicationsQueryReceived; },
  set lastApplicationsQueryReceived(v) { lastApplicationsQueryReceived = v; },
  get lastCriteriaQueryReceived() { return lastCriteriaQueryReceived; },
  set lastCriteriaQueryReceived(v) { lastCriteriaQueryReceived = v; },
  get currentCriteriaList() { return currentCriteriaList; },
  set currentCriteriaList(v) { currentCriteriaList = v; },
  get lastNotesQueryReceived() { return lastNotesQueryReceived; },
  set lastNotesQueryReceived(v) { lastNotesQueryReceived = v; },
  get currentNotesList() { return currentNotesList; },
  set currentNotesList(v) { currentNotesList = v; },
  get currentNoteDetail() { return currentNoteDetail; },
  set currentNoteDetail(v) { currentNoteDetail = v; },
  get lastSearchRunsQueryReceived() { return lastSearchRunsQueryReceived; },
  set lastSearchRunsQueryReceived(v) { lastSearchRunsQueryReceived = v; },
  get currentSearchRunsList() { return currentSearchRunsList; },
  set currentSearchRunsList(v) { currentSearchRunsList = v; },
};

test.beforeAll(async () => {
  harness = await startAppHarness(stubState, E2E_SESSION_ID);
  baseUrl = harness.baseUrl;
});

test.afterAll(async () => {
  await harness.stop();
});


async function setupSessionCookie(context: BrowserContext) {
  saveSession({
    sessionId: E2E_SESSION_ID,
    ownerSub: "playwright-test-user",
    accessToken: "stubbed-access-token",
    refreshToken: "stubbed-refresh-token",
    expiresAt: Date.now() + 3600_000,
    createdAt: Date.now(),
    csrfToken: "playwright-csrf-token",
  });
  await context.addCookies([
    {
      name: "__Host-roleradar_session",
      value: E2E_SESSION_ID,
      url: baseUrl,
      httpOnly: true,
      secure: true,
      sameSite: "Lax",
    },
  ]);
}

async function runAxeChecks(page: Page) {
  await page.evaluate(axe.source);
  const results = await page.evaluate(async () => {
    const win = window as unknown as { axe: { run: (doc: Document, opts: unknown) => Promise<{ violations: Array<{ impact?: string }> }> } };
    return await win.axe.run(document, {
      runOnly: {
        type: "tag",
        values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"],
      },
    });
  });
  return results.violations.filter(
    (v: { impact?: string }) => v.impact === "serious" || v.impact === "critical"
  );
}

async function checkAxeInThemes(page: Page) {
  // Emulate reduced motion so bundle.css disables transitions during theme changes
  await page.emulateMedia({ reducedMotion: "reduce" });

  for (const theme of ["light", "dark"] as const) {
    await page.evaluate((t) => {
      document.documentElement.setAttribute("data-theme", t);
    }, theme);

    // Ensure all CSS transitions and animations have fully settled before axe evaluation
    await page.evaluate(async () => {
      const animations = document.getAnimations();
      await Promise.all(animations.map((a) => a.finished));
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    });

    const violations = await runAxeChecks(page);
    expect(violations).toEqual([]);
  }
}

test.describe("CSP and SSR Style Attribute Invariant (Real Chromium)", () => {
  test("loads /not-authorized with zero CSP violations and verifies computed styles apply before hydration", async ({
    page,
  }) => {
    const cspViolations: string[] = [];

    page.on("console", (msg) => {
      const text = msg.text();
      if (
        text.toLowerCase().includes("content security policy") ||
        text.toLowerCase().includes("violates the following") ||
        msg.type() === "error" && text.includes("CSP")
      ) {
        cspViolations.push(text);
      }
    });

    // Intercept client scripts to inspect SSR computed styles prior to hydration
    let resumeScripts: () => void = () => {};
    const scriptsPaused = new Promise<void>((resolve) => {
      resumeScripts = resolve;
    });

    await page.route("**/*.js", async (route) => {
      await scriptsPaused;
      await route.continue();
    });

    // Navigate to page (SSR HTML delivers first)
    const navPromise = page.goto(`${baseUrl}/not-authorized`, {
      waitUntil: "commit",
    });
    await navPromise;

    // Verify main element has SSR computed style applied by browser before hydration executes
    const main = page.locator("main");
    await expect(main).toBeVisible();

    const displayBeforeHydration = await main.evaluate((el) => {
      return window.getComputedStyle(el).display;
    });
    expect(displayBeforeHydration).toBe("flex");

    const justifyContentBeforeHydration = await main.evaluate((el) => {
      return window.getComputedStyle(el).justifyContent;
    });
    expect(justifyContentBeforeHydration).toBe("center");

    // Allow client scripts to load and hydrate
    resumeScripts();
    await page.waitForLoadState("networkidle");

    // Verify heading is present
    const heading = page.getByRole("heading", { name: "Not Authorized" });
    await expect(heading).toBeVisible();

    // Assert zero CSP violation console messages
    expect(cspViolations).toEqual([]);
  });

  test("loads /service-unavailable with zero CSP violations and verifies computed styles apply before hydration", async ({
    page,
  }) => {
    const cspViolations: string[] = [];

    page.on("console", (msg) => {
      const text = msg.text();
      if (
        text.toLowerCase().includes("content security policy") ||
        text.toLowerCase().includes("violates the following") ||
        (msg.type() === "error" && text.includes("CSP"))
      ) {
        cspViolations.push(text);
      }
    });

    // Intercept client scripts to inspect SSR computed styles prior to hydration
    let resumeScripts: () => void = () => {};
    const scriptsPaused = new Promise<void>((resolve) => {
      resumeScripts = resolve;
    });

    await page.route("**/*.js", async (route) => {
      await scriptsPaused;
      await route.continue();
    });

    await page.goto(`${baseUrl}/service-unavailable`, {
      waitUntil: "commit",
    });

    const heading = page.getByRole("heading", { name: "Service Unavailable" });
    await expect(heading).toBeVisible();

    // Verify the EmptyState vendored design system component has its computed styles applied before hydration, through
    // classes: the element carries no style attribute (009 phase 6: the policy has no style-src-attr).
    const emptyStateIcon = page.locator("span[aria-hidden='true']");
    await expect(emptyStateIcon).toBeVisible();
    expect(await emptyStateIcon.getAttribute("style")).toBeNull();

    const iconDisplay = await emptyStateIcon.evaluate((el) => {
      return window.getComputedStyle(el).display;
    });
    // In CSS Flexbox, inline-flex is blockified to flex on flex items
    expect(iconDisplay).toBe("flex");

    const iconWidth = await emptyStateIcon.evaluate((el) => {
      return window.getComputedStyle(el).width;
    });
    expect(iconWidth).toBe("48px");

    const codeSnippet = page.locator("code");
    await expect(codeSnippet).toBeVisible();
    await expect(codeSnippet).toContainText("pwsh ./scripts/start-services.ps1");

    // Allow scripts to load and hydrate
    resumeScripts();
    await page.waitForLoadState("networkidle");

    // Zero CSP violations
    expect(cspViolations).toEqual([]);
  });

  test("sends a CSP without style-src-attr and without any unsafe-inline (009 phase 6)", async ({ request }) => {
    const response = await request.get(`${baseUrl}/service-unavailable`);
    const csp = response.headers()["content-security-policy"];
    expect(csp).toBeDefined();
    expect(csp).not.toContain("style-src-attr");
    expect(csp).not.toContain("unsafe-inline");
    expect(csp).toContain("style-src 'self' 'nonce-");
  });

  test("EmptyState on /service-unavailable is styled from the server HTML with JavaScript disabled, no style attribute anywhere", async ({
    browser,
  }) => {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, javaScriptEnabled: false });
    try {
      const page = await context.newPage();
      await page.goto(`${baseUrl}/service-unavailable`);
      await expect(page.getByRole("heading", { name: "Service Unavailable" })).toBeVisible();

      const icon = page.locator("span[aria-hidden='true']").first();
      await expect(icon).toBeVisible();
      // page.evaluate cannot run with JavaScript off, so read the laid-out box: an unstyled inline span would be 0 wide,
      // and the kit class (width 3rem, centred flex box) gives 48px. The server HTML alone carries the styling.
      const box = await icon.boundingBox();
      expect(box?.width).toBe(48);
      expect(box?.height).toBe(48);

      const withStyleAttribute = await page.locator("[style]").count();
      expect(withStyleAttribute, "elements with a style attribute in the server HTML").toBe(0);
    } finally {
      await context.close();
    }
  });

  test("loads /postings list with API stubbed, asserting zero CSP violations and axe zero serious/critical in light and dark", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);

    const cspViolations: string[] = [];
    const hydrationWarnings: string[] = [];
    page.on("console", (msg) => {
      const text = msg.text();
      if (
        text.toLowerCase().includes("content security policy") ||
        text.toLowerCase().includes("violates the following") ||
        (msg.type() === "error" && text.includes("CSP"))
      ) {
        cspViolations.push(text);
      }
      if (
        text.toLowerCase().includes("hydration") ||
        text.toLowerCase().includes("did not match") ||
        text.toLowerCase().includes("extra attributes from the server")
      ) {
        hydrationWarnings.push(text);
      }
    });

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    // Verify main page elements are rendered
    await expect(page.getByRole("heading", { name: "Job Postings" })).toBeVisible();
    await expect(page.getByText("Principal Engineer")).toBeVisible();
    await expect(page.getByText("Acme Corp")).toBeVisible();

    // Verify axe accessibility in light and dark mode with settling
    await checkAxeInThemes(page);

    // Assert zero hydration warnings and zero CSP violations
    expect(hydrationWarnings).toEqual([]);
    expect(cspViolations).toEqual([]);
  });

  test("loads /postings/:id detail with API stubbed, asserting zero CSP violations and axe zero serious/critical in light and dark", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);

    const cspViolations: string[] = [];
    const hydrationWarnings: string[] = [];
    page.on("console", (msg) => {
      const text = msg.text();
      if (
        text.toLowerCase().includes("content security policy") ||
        text.toLowerCase().includes("violates the following") ||
        (msg.type() === "error" && text.includes("CSP"))
      ) {
        cspViolations.push(text);
      }
      if (
        text.toLowerCase().includes("hydration") ||
        text.toLowerCase().includes("did not match") ||
        text.toLowerCase().includes("extra attributes from the server")
      ) {
        hydrationWarnings.push(text);
      }
    });

    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Verify detail page elements are rendered
    await expect(page.getByRole("heading", { name: "Principal Engineer", level: 1 })).toBeVisible();
    await expect(page.getByText("Acme Corp")).toBeVisible();
    await expect(page.getByText("REQ-101")).toBeVisible();
    await expect(page.getByText("$180,000 - $220,000")).toBeVisible();

    // Verify axe accessibility in light and dark mode with settling
    await checkAxeInThemes(page);

    // Assert zero hydration warnings and zero CSP violations
    expect(hydrationWarnings).toEqual([]);
    expect(cspViolations).toEqual([]);
  });
});

test.describe("Posting Detail Real Transitions (Real Chromium)", () => {
  test.beforeEach(async () => {
    // Reset state before each test
    currentPostingDisposition = "lead";
    currentPostingVersionToken = "ver-1";
    currentPostingTitle = "Principal Engineer";
    nextTransitionResponseStatus = null;
    nextTransitionResponseBody = null;
  });

  test("submits successful transition: page updates, URL remains clean, dialog closes, badge tones differ", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Initial badge: Lead with primary tone
    const badge = page.getByTestId("disposition-badge");
    await expect(badge).toHaveText("Lead");
    expect(await badge.getAttribute("data-tone")).toBe("primary");
    const initialBgColor = await badge.evaluate((el) => window.getComputedStyle(el).backgroundColor);

    // Open change disposition dialog
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();

    // Fill valid transition (lead -> scored is first target)
    await page.getByLabel("Reason *").fill("Candidate matches required qualifications");
    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Dialog closes
    await expect(modal).not.toBeVisible();

    // URL remains clean (no raw JSON page)
    expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);

    // Page updates: Badge is now Scored with warning tone
    await expect(badge).toHaveText("Scored");
    expect(await badge.getAttribute("data-tone")).toBe("warning");
    const updatedBgColor = await badge.evaluate((el) => window.getComputedStyle(el).backgroundColor);

    // Assert the badge tone classes/colours differ between dispositions
    expect(updatedBgColor).not.toBe(initialBgColor);
  });

  test("submits transition with 400 InvalidTransition: shows inline error in dialog without closing or raw JSON", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Configure mock API to return 400 InvalidTransition
    nextTransitionResponseStatus = 400;
    nextTransitionResponseBody = {
      status: 400,
      errorCode: "validation_error",
      detail: "Transition to this disposition is not allowed by policy.",
      errors: { toDisposition: ["Transition not allowed."] },
    };

    // Open change disposition dialog
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();

    await page.getByLabel("Reason *").fill("Attempting invalid transition");
    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Dialog remains open
    await expect(modal).toBeVisible();

    // Inline error message is displayed
    const alert = modal.getByRole("alert");
    await expect(alert).toBeVisible();
    await expect(alert).toContainText("Transition to this disposition is not allowed by policy.");

    // URL remains clean
    expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);
  });

  test("submits transition with 409 Conflict: shows Toast with Reload action which refetches without full page reload", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Configure mock API to return 409 Concurrency Conflict
    nextTransitionResponseStatus = 409;
    nextTransitionResponseBody = {
      status: 409,
      errorCode: "concurrency_conflict",
      detail: "This posting was modified by another process.",
    };

    // Open change disposition dialog
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();

    await page.getByLabel("Reason *").fill("Attempting transition that conflicts");
    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Toast appears with Reload button
    const toast = page.locator('[role="alert"], [role="status"]').filter({ hasText: "Concurrency Conflict" });
    await expect(toast).toBeVisible();
    await expect(toast).toContainText("This posting was updated by another process.");

    // Simultaneously simulate server state change for reload verification
    currentPostingTitle = "Principal Engineer (Concurrently Updated)";

    // Track whether window.location.reload was called (it should NOT be called)
    await page.evaluate(() => {
      const win = window as unknown as { __reloadedViaWindow: boolean };
      win.__reloadedViaWindow = false;
      const origReload = window.location.reload.bind(window.location);
      window.location.reload = () => {
        win.__reloadedViaWindow = true;
        origReload();
      };
    });

    // Click Reload action in Toast
    const reloadButton = toast.getByRole("button", { name: "Reload" });
    await reloadButton.click();

    // Verify page revalidated and fetched updated title
    await expect(page.getByRole("heading", { name: "Principal Engineer (Concurrently Updated)", level: 1 })).toBeVisible();

    const reloadedViaWindow = await page.evaluate(() => (window as unknown as { __reloadedViaWindow: boolean }).__reloadedViaWindow);
    expect(reloadedViaWindow).toBe(false);
  });

  test("submits transition with bad CSRF: shows inline error without raw response", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Open change disposition dialog
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();

    await page.getByLabel("Reason *").fill("Attempt with bad CSRF");

    // Tamper the CSRF token in the dialog form right before submitting
    await page.evaluate(() => {
      const modalEl = document.querySelector('[role="dialog"]');
      const input = modalEl?.querySelector('input[name="_csrf"]') as HTMLInputElement;
      if (input) {
        input.value = "tampered-invalid-csrf-token";
      }
    });

    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Dialog remains open
    await expect(modal).toBeVisible();

    // Inline CSRF error is displayed
    const alert = modal.getByRole("alert");
    await expect(alert).toBeVisible();
    await expect(alert).toContainText("Invalid or missing CSRF token");

    // URL remains clean (no raw error page)
    expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);
  });

  test("submits transition with API 401: redirects into authorization flow, avoids loop, and clears session cookie", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

    // Verify session cookie is present initially
    let cookies = await context.cookies();
    expect(cookies.some((c) => c.name === "__Host-roleradar_session")).toBe(true);

    // Configure mock API to return 401 on transition attempt
    nextTransitionResponseStatus = 401;
    nextTransitionResponseBody = {
      status: 401,
      errorCode: "unauthorized",
      detail: "API access token is expired or unauthorized.",
    };

    let authRedirectCount = 0;
    const authRedirectPromise = page.waitForRequest((req) => {
      const u = req.url();
      if (u.includes("identity:5001") || u.includes("/connect/authorize") || u.includes("/auth")) {
        authRedirectCount++;
        return true;
      }
      return false;
    });

    // Intercept outbound request to Identity to assert the request/redirect without depending on IdP page
    await page.route("**/connect/authorize*", (route) => {
      route.fulfill({
        status: 200,
        contentType: "text/html",
        body: "<html><body>Mock Identity Provider Login Page</body></html>",
      });
    });

    // Open change disposition dialog
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();

    await page.getByLabel("Reason *").fill("Attempt transition with 401 response");
    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Assert that redirect into authorization flow was requested
    const authRequest = await authRedirectPromise;
    expect(authRequest.url()).toContain("connect/authorize");
    expect(authRequest.url()).toContain("client_id=roleradar-web");

    // Wait briefly to verify no redirect loop occurs
    await page.waitForTimeout(500);
    expect(authRedirectCount).toBe(1);

    // Assert session cookie is cleared
    cookies = await context.cookies();
    const sessionCookie = cookies.find((c) => c.name === "__Host-roleradar_session");
    expect(!sessionCookie || sessionCookie.value === "").toBe(true);
  });
});

test.describe("Theme and Page Reset", () => {
  test("first visit dark: follows prefers-color-scheme, resets html/body margin, matches shell bg, no scrollbars at 1280x800", async ({
    browser,
  }) => {
    const context = await browser.newContext({
      colorScheme: "dark",
      viewport: { width: 1280, height: 800 },
      ignoreHTTPSErrors: true,
    });
    await setupSessionCookie(context);
    const page = await context.newPage();

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    const theme = await page.locator("html").getAttribute("data-theme");
    expect(theme).toBe("dark");

    const layout = await page.evaluate(() => {
      const bodyStyle = window.getComputedStyle(document.body);
      const shellEl = document.querySelector(".ds-appshell") as HTMLElement;
      const shellStyle = shellEl ? window.getComputedStyle(shellEl) : null;
      return {
        bodyMargin: bodyStyle.margin,
        bodyBg: bodyStyle.backgroundColor,
        shellBg: shellStyle?.backgroundColor,
        scrollWidth: document.documentElement.scrollWidth,
        scrollHeight: document.documentElement.scrollHeight,
        clientWidth: document.documentElement.clientWidth,
        clientHeight: document.documentElement.clientHeight,
      };
    });

    // Body margin 0
    expect(layout.bodyMargin).toBe("0px");
    // Body background equals shell background and matches dark token
    expect(layout.bodyBg).toBe("rgb(28, 25, 23)");
    expect(layout.shellBg).toBe("rgb(28, 25, 23)");
    expect(layout.bodyBg).toBe(layout.shellBg);
    // No page scrollbars at 1280x800
    expect(layout.scrollHeight).toBeLessThanOrEqual(800);
    expect(layout.scrollWidth).toBeLessThanOrEqual(1280);
  });

  test("first visit light: follows prefers-color-scheme, resets html/body margin, matches shell bg, no scrollbars at 1280x800", async ({
    browser,
  }) => {
    const context = await browser.newContext({
      colorScheme: "light",
      viewport: { width: 1280, height: 800 },
      ignoreHTTPSErrors: true,
    });
    await setupSessionCookie(context);
    const page = await context.newPage();

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    const theme = await page.locator("html").getAttribute("data-theme");
    expect(theme).toBe("light");

    const layout = await page.evaluate(() => {
      const bodyStyle = window.getComputedStyle(document.body);
      const shellEl = document.querySelector(".ds-appshell") as HTMLElement;
      const shellStyle = shellEl ? window.getComputedStyle(shellEl) : null;
      return {
        bodyMargin: bodyStyle.margin,
        bodyBg: bodyStyle.backgroundColor,
        shellBg: shellStyle?.backgroundColor,
        scrollWidth: document.documentElement.scrollWidth,
        scrollHeight: document.documentElement.scrollHeight,
        clientWidth: document.documentElement.clientWidth,
        clientHeight: document.documentElement.clientHeight,
      };
    });

    expect(layout.bodyMargin).toBe("0px");
    expect(layout.bodyBg).toBe("rgb(255, 255, 255)");
    expect(layout.shellBg).toBe("rgb(255, 255, 255)");
    expect(layout.bodyBg).toBe(layout.shellBg);
    expect(layout.scrollHeight).toBeLessThanOrEqual(800);
    expect(layout.scrollWidth).toBeLessThanOrEqual(1280);
  });

  test("explicit cookie overrides system theme and toggle sets 1-year Lax Secure cookie", async ({
    browser,
  }) => {
    const context = await browser.newContext({
      colorScheme: "light",
      ignoreHTTPSErrors: true,
    });
    await setupSessionCookie(context);
    // Set explicit dark cookie in a light system
    await context.addCookies([
      {
        name: "roleradar_theme",
        value: "dark",
        url: baseUrl,
        sameSite: "Lax",
        secure: true,
      },
    ]);

    const page = await context.newPage();
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    // Explicit cookie overrides OS light preference
    const theme = await page.locator("html").getAttribute("data-theme");
    expect(theme).toBe("dark");

    const shellBg = await page.locator(".ds-appshell").evaluate((el) => {
      return window.getComputedStyle(el).backgroundColor;
    });
    expect(shellBg).toBe("rgb(28, 25, 23)");

    // Click theme toggle button in AppShell
    const toggleButton = page.getByRole("button", { name: /switch to light theme/i });
    await expect(toggleButton).toBeVisible();
    await toggleButton.click();

    // Theme should now be light
    await expect(page.locator("html")).toHaveAttribute("data-theme", "light");

    // Cookie must be updated
    const cookies = await context.cookies();
    const themeCookie = cookies.find((c) => c.name === "roleradar_theme");
    expect(themeCookie).toBeDefined();
    expect(themeCookie?.value).toBe("light");
    expect(themeCookie?.sameSite).toBe("Lax");
    expect(themeCookie?.secure).toBe(true);
    // Expiration ~1 year (31536000 seconds)
    const nowSec = Date.now() / 1000;
    expect(themeCookie?.expires).toBeGreaterThan(nowSec + 30_000_000);
  });

  test("live change: follows OS setting changes when no cookie is set", async ({
    browser,
  }) => {
    const context = await browser.newContext({
      colorScheme: "dark",
      ignoreHTTPSErrors: true,
    });
    await setupSessionCookie(context);
    const page = await context.newPage();

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    expect(await page.locator("html").getAttribute("data-theme")).toBe("dark");

    // Emulate OS switching to light
    await page.emulateMedia({ colorScheme: "light" });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "light");

    const lightBg = await page.locator(".ds-appshell").evaluate((el) => {
      return window.getComputedStyle(el).backgroundColor;
    });
    expect(lightBg).toBe("rgb(255, 255, 255)");

    // Emulate OS switching back to dark
    await page.emulateMedia({ colorScheme: "dark" });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");

    const darkBg = await page.locator(".ds-appshell").evaluate((el) => {
      return window.getComputedStyle(el).backgroundColor;
    });
    expect(darkBg).toBe("rgb(28, 25, 23)");
  });

  test("header label shows preferred username or Signed in, never raw sub GUID", async ({
    browser,
  }) => {
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    await setupSessionCookie(context);
    const page = await context.newPage();

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    // The test user has ownerSub = "playwright-test-user" without ownerUsername
    // Header must fall back to "Signed in", never "playwright-test-user"
    const userLabelEl = page.locator(".ds-appshell-user");
    await expect(userLabelEl).toHaveText("Signed in");
    await expect(userLabelEl).not.toHaveText("playwright-test-user");
  });

  test("favicon is served cleanly without 404 or unhandled errors", async ({
    request,
  }) => {
    const res = await request.get(`${baseUrl}/favicon.ico`);
    expect([200, 204]).toContain(res.status());
  });

  test("clicking Sign Out submits POST /auth/logout, redirects to end-session URL, and clears session cookie", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });

    // Verify session cookie is present initially
    let cookies = await context.cookies();
    expect(cookies.some((c) => c.name === "__Host-roleradar_session")).toBe(true);

    const endSessionPromise = page.waitForRequest((req) => req.url().includes("connect/endsession"));

    await page.route(/\/connect\/endsession/, (route) => {
      route.fulfill({
        status: 200,
        contentType: "text/html",
        body: "<html><body>Mock Identity Provider End Session</body></html>",
      });
    });

    const signOutButton = page.getByRole("button", { name: "Sign Out" });
    await expect(signOutButton).toBeVisible();
    await signOutButton.click();

    const endSessionRequest = await endSessionPromise;
    expect(endSessionRequest.url()).toContain("connect/endsession");
    expect(endSessionRequest.url()).toContain("post_logout_redirect_uri");

    // Verify session cookie was cleared
    cookies = await context.cookies();
    const sessionCookie = cookies.find((c) => c.name === "__Host-roleradar_session");
    expect(!sessionCookie || sessionCookie.value === "").toBe(true);
  });
});

test.describe("Company Management (Real Chromium)", () => {
  test.beforeEach(async () => {
    currentCompanyName = "Acme Cloud Services";
    currentCompanyStatus = "target";
    currentCompanyOwnership = "private";
    currentCompanyDescription = "Cloud hosting and observability tools";
    currentCompanyHeadquarters = "Seattle, WA";
    currentCompanyCategoryTags = ["cloud", "devops"];
    currentCompanyCareerSources = [
      {
        sourceType: "ats",
        atsProvider: "greenhouse",
        atsTenant: "acmecloud",
        sourceUrl: "https://boards.greenhouse.io/acmecloud",
        fetchMethod: "api",
        active: true,
      },
    ];
    currentCompanyVersionToken = "comp-ver-1";
    nextCompanyResponseStatus = null;
    nextCompanyResponseBody = null;
    lastCompaniesQueryReceived = {};
  });

  test("loads /companies list with API stubbed, asserting only whitelisted OData options are sent, zero CSP violations and axe zero serious/critical in light and dark", async ({
    page,
    context,
  }) => {
    const cspViolations: string[] = [];
    page.on("console", (msg) => {
      if (msg.text().includes("Content Security Policy") || msg.text().includes("violates")) {
        cspViolations.push(msg.text());
      }
    });

    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies?limit=25&offset=0&q=ignoredQuery&sort=bogus`, { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Companies" })).toBeVisible();
    await expect(page.getByText("Acme Cloud Services")).toBeVisible();

    // Verify only whitelisted OData options were sent: the q search becomes a name filter, the unknown
    // sort field is dropped in favour of the default name order, and nothing else reaches the API.
    expect(lastCompaniesQueryReceived["$filter"]).toBe("contains(tolower(name),'ignoredquery')");
    expect(lastCompaniesQueryReceived["$orderby"]).toBe("name asc");
    expect(lastCompaniesQueryReceived["$top"]).toBe("25");
    expect(lastCompaniesQueryReceived["$skip"]).toBe("0");
    expect(lastCompaniesQueryReceived["$count"]).toBe("true");
    expect(Object.keys(lastCompaniesQueryReceived).sort()).toEqual(["$count", "$filter", "$orderby", "$skip", "$top"]);
    expect(lastCompaniesQueryReceived.q).toBeUndefined();
    expect(lastCompaniesQueryReceived.sort).toBeUndefined();

    // Axe in light and dark mode with settling
    await checkAxeInThemes(page);

    expect(cspViolations).toEqual([]);
  });

  test("loads /companies/:id detail with API stubbed, asserting zero CSP violations and axe zero serious/critical in light and dark", async ({
    page,
    context,
  }) => {
    const cspViolations: string[] = [];
    page.on("console", (msg) => {
      if (msg.text().includes("Content Security Policy") || msg.text().includes("violates")) {
        cspViolations.push(msg.text());
      }
    });

    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Acme Cloud Services", level: 1 })).toBeVisible();
    await expect(page.getByLabel("Company Name *")).toHaveValue("Acme Cloud Services");

    // Axe in light and dark mode with settling
    await checkAxeInThemes(page);

    expect(cspViolations).toEqual([]);
  });

  test("submits company edit form successfully: updates page, shows success toast, URL remains clean", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    await page.getByLabel("Company Name *").fill("Acme Cloud Platform Inc");
    await page.getByLabel("Headquarters").fill("Bellevue, WA");
    await page.getByRole("button", { name: "Save Changes" }).click();

    // URL remains clean
    expect(page.url()).toBe(`${baseUrl}/companies/comp-e2e-1`);

    // Success toast appears
    await expect(page.getByText("Changes Saved")).toBeVisible();
    await expect(page.getByText("Company profile has been updated successfully.")).toBeVisible();

    // Header updates with new company name
    await expect(page.getByRole("heading", { name: "Acme Cloud Platform Inc", level: 1 })).toBeVisible();
  });

  test("submits company edit form with 400 validation error: displays inline field errors next to fields without closing or raw JSON", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    nextCompanyResponseStatus = 400;
    nextCompanyResponseBody = {
      status: 400,
      errorCode: "validation_error",
      detail: "Validation failed.",
      errors: { name: ["Company name cannot be blank."] },
    };

    await page.getByRole("button", { name: "Save Changes" }).click();

    // Inline field error is displayed
    await expect(page.getByText("Company name cannot be blank.")).toBeVisible();
    expect(page.url()).toBe(`${baseUrl}/companies/comp-e2e-1`);
  });

  test("submits company edit form with 409 Conflict: displays conflict Toast with Reload action that refetches", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    nextCompanyResponseStatus = 409;
    nextCompanyResponseBody = {
      status: 409,
      errorCode: "concurrency_conflict",
      detail: "This company was modified by another operation.",
    };

    await page.getByRole("button", { name: "Save Changes" }).click();

    // Conflict toast is displayed
    await expect(page.getByText("Concurrency Conflict")).toBeVisible();
    const reloadButton = page.getByRole("button", { name: "Reload" });
    await expect(reloadButton).toBeVisible();

    // Clicking reload refetches and dismisses toast
    await reloadButton.click();
    await expect(page.getByText("Concurrency Conflict")).not.toBeVisible();
  });

  test("submits company edit form with bad CSRF: shows inline error without raw response", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    // Tamper with hidden CSRF input inside company form
    await page.evaluate(() => {
      const form = document.querySelector("#ds-main-content form") || document.forms[1];
      const csrfEl = form?.querySelector("input[name='_csrf']") as HTMLInputElement;
      if (csrfEl) csrfEl.value = "tampered-invalid-csrf";
    });

    await page.getByRole("button", { name: "Save Changes" }).click();

    // Error alert is displayed
    await expect(page.getByText("Error Saving Changes")).toBeVisible();
    await expect(page.getByText(/csrf|not authorized/i)).toBeVisible();
    expect(page.url()).toBe(`${baseUrl}/companies/comp-e2e-1`);
  });

  test("submits company edit form with API 401: redirects into authorization flow, avoids loop, and clears session cookie", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    nextCompanyResponseStatus = 401;
    nextCompanyResponseBody = {
      status: 401,
      errorCode: "unauthorized",
      detail: "Token expired",
    };

    let authRedirectCount = 0;
    const authRedirectPromise = page.waitForRequest((req) => {
      const u = req.url();
      if (u.includes("identity:5001") || u.includes("/connect/authorize") || u.includes("/auth")) {
        authRedirectCount++;
        return true;
      }
      return false;
    });

    await page.route("**/connect/authorize*", (route) => {
      route.fulfill({
        status: 200,
        contentType: "text/html",
        body: "<html><body>Mock Identity Provider Login Page</body></html>",
      });
    });

    await page.getByRole("button", { name: "Save Changes" }).click();

    // Assert that redirect into authorization flow was requested
    const authRequest = await authRedirectPromise;
    expect(authRequest.url()).toContain("connect/authorize");
    expect(authRequest.url()).toContain("client_id=roleradar-web");

    // Wait briefly to verify no redirect loop occurs
    await page.waitForTimeout(500);
    expect(authRedirectCount).toBe(1);

    // Session cookie cleared
    const cookies = await context.cookies();
    const sessionCookie = cookies.find((c) => c.name === "__Host-roleradar_session");
    expect(!sessionCookie || sessionCookie.value === "").toBe(true);
  });

  test("hostile company data with <script>, HTML and javascript: career source renders safely as plain text without script execution", async ({
    page,
    context,
  }) => {
    let alertTriggered = false;
    page.on("dialog", async (dialog) => {
      alertTriggered = true;
      await dialog.dismiss();
    });

    currentCompanyName = "<script>alert('hostile-xss')</script>";
    currentCompanyDescription = "<iframe src='evil.com'></iframe><img src=x onerror=alert('img-xss')>";
    currentCompanyCareerSources = [
      {
        sourceType: "ats",
        atsProvider: "custom",
        atsTenant: "evil",
        sourceUrl: "javascript:alert('malicious')",
        fetchMethod: "browser",
        active: true,
      },
    ];

    await setupSessionCookie(context);
    await page.goto(`${baseUrl}/companies/comp-e2e-1`, { waitUntil: "networkidle" });

    expect(alertTriggered).toBe(false);
    expect(await page.locator("script:has-text('hostile-xss')").count()).toBe(0);
    expect(await page.locator("iframe").count()).toBe(0);
    expect(await page.locator("a[href*='javascript:']").count()).toBe(0);
  });

  test.describe("Application Management (Real Chromium)", () => {
    test.beforeEach(() => {
      currentPostingDisposition = "lead";
      currentApplicationStatus = "applied";
      currentApplicationEvents = [
        { eventType: "applied", eventDate: "2026-10-31", notes: "Initial application submitted" },
      ];
      nextApplicationResponseStatus = null;
      nextApplicationResponseBody = null;
    });

    test("loads /applications list from stubbed /odata/Applications, asserting only whitelisted OData options are sent, zero CSP violations and axe zero serious/critical in light and dark", async ({
      page,
      context,
    }) => {
      const cspViolations: string[] = [];
      page.on("console", (msg) => {
        if (msg.text().includes("Content Security Policy") || msg.text().includes("violates")) {
          cspViolations.push(msg.text());
        }
      });

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications?status=applied&limit=25&offset=0&bad=param&sort=foo`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: "Applications", level: 1 })).toBeVisible();
      await expect(page.getByText("Principal Engineer")).toBeVisible();
      await expect(page.getByText("Acme Cloud Services").first()).toBeVisible();

      // Verify only the whitelisted OData options were sent, and unknown or unsupported URL params were dropped
      expect(lastApplicationsQueryReceived["$filter"]).toBe("status eq 'applied'");
      expect(lastApplicationsQueryReceived["$orderby"]).toBe("appliedAt desc");
      expect(lastApplicationsQueryReceived["$top"]).toBe("25");
      expect(lastApplicationsQueryReceived["$skip"]).toBe("0");
      expect(lastApplicationsQueryReceived["$count"]).toBe("true");
      expect(Object.keys(lastApplicationsQueryReceived).sort()).toEqual(["$count", "$filter", "$orderby", "$skip", "$top"]);
      expect(lastApplicationsQueryReceived.bad).toBeUndefined();
      expect(lastApplicationsQueryReceived.sort).toBeUndefined();

      await checkAxeInThemes(page);
      expect(cspViolations).toEqual([]);
    });

    test("loads /applications/:id detail with API stubbed, asserting zero CSP violations and axe zero serious/critical in light and dark", async ({
      page,
      context,
    }) => {
      const cspViolations: string[] = [];
      page.on("console", (msg) => {
        if (msg.text().includes("Content Security Policy") || msg.text().includes("violates")) {
          cspViolations.push(msg.text());
        }
      });

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: /Application: Principal Engineer/i, level: 1 })).toBeVisible();
      await expect(page.getByText("Initial application submitted")).toBeVisible();

      await checkAxeInThemes(page);
      expect(cspViolations).toEqual([]);
    });

    test("submits create application dialog on /postings/:id: page updates posting badge to applied, create button disappears, dialog closes, URL remains clean", async ({
      page,
      context,
    }) => {
      currentPostingDisposition = "scored";
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

      // Create Application button is visible for 'scored' disposition
      const createBtn = page.getByRole("button", { name: "Create Application" });
      await expect(createBtn).toBeVisible();
      await createBtn.click();

      // Dialog opens
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();
      await expect(page.getByText("Create Job Application")).toBeVisible();

      // Fill and submit
      await page.getByLabel("Submission Date *").fill("2026-10-31");
      await page.getByLabel("Desired Min").fill("190000");
      await page.getByLabel("Desired Max").fill("220000");
      await page.getByRole("button", { name: "Record Application" }).click();

      // URL remains clean
      expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);

      // Dialog closed
      await expect(modal).not.toBeVisible();

      // Posting badge updated to Applied (tone: success)
      const badge = page.getByTestId("disposition-badge");
      await expect(badge).toHaveText("Applied");
      expect(await badge.getAttribute("data-tone")).toBe("success");

      // Create Application button is no longer shown for applied postings
      await expect(page.getByRole("button", { name: "Create Application" })).not.toBeVisible();
    });

    test("submits create application with 400 validation error: shows inline error in dialog without closing or raw JSON", async ({
      page,
      context,
    }) => {
      currentPostingDisposition = "scored";
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 400;
      nextApplicationResponseBody = {
        status: 400,
        errorCode: "validation_error",
        detail: "Validation failed.",
        errors: { submittedDate: ["Invalid submission date format."] },
      };

      await page.getByRole("button", { name: "Create Application" }).click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();
      await expect(page.getByText("Create Job Application")).toBeVisible();

      await page.getByRole("button", { name: "Record Application" }).click();

      // Inline field error is visible
      await expect(page.getByText("Invalid submission date format.")).toBeVisible();
      // Dialog remains open
      await expect(modal).toBeVisible();
      expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);
    });

    test("submits create application with 409 conflict: shows Toast with Reload action which refetches", async ({
      page,
      context,
    }) => {
      currentPostingDisposition = "scored";
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 409;
      nextApplicationResponseBody = {
        status: 409,
        errorCode: "concurrency_conflict",
        detail: "An application already exists for this posting or state transition conflict occurred.",
      };

      await page.getByRole("button", { name: "Create Application" }).click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await page.getByRole("button", { name: "Record Application" }).click();

      // Conflict toast displayed
      await expect(page.getByText("Concurrency Conflict")).toBeVisible();
      const reloadBtn = page.getByRole("button", { name: "Reload" });
      await expect(reloadBtn).toBeVisible();

      await reloadBtn.click();
      await expect(page.getByText("Concurrency Conflict")).not.toBeVisible();
    });

    test("submits create application with bad CSRF: shows inline error without raw response", async ({
      page,
      context,
    }) => {
      currentPostingDisposition = "scored";
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

      await page.getByRole("button", { name: "Create Application" }).click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await page.evaluate(() => {
        const modalEl = document.querySelector('[role="dialog"]');
        const input = modalEl?.querySelector('input[name="_csrf"]') as HTMLInputElement;
        if (input) {
          input.value = "tampered-bad-csrf";
        }
      });

      await page.getByRole("button", { name: "Record Application" }).click();

      const alert = modal.getByRole("alert");
      await expect(alert).toBeVisible();
      await expect(alert).toContainText("Invalid or missing CSRF token");
      expect(page.url()).toBe(`${baseUrl}/postings/post-e2e-1`);
    });

    test("submits create application with API 401: redirects into authorization flow, avoids loop, and clears session cookie", async ({
      page,
      context,
    }) => {
      currentPostingDisposition = "scored";
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/postings/post-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 401;
      nextApplicationResponseBody = {
        status: 401,
        errorCode: "unauthorized",
        detail: "Access token expired",
      };

      // Stub the Identity authorize endpoint so this test never depends on a listener on host port 5001
      await page.route("**/connect/authorize*", (route) => {
        route.fulfill({
          status: 200,
          contentType: "text/html",
          body: "<html><body>Mock Identity Provider Login Page</body></html>",
        });
      });

      await page.getByRole("button", { name: "Create Application" }).click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await page.getByRole("button", { name: "Record Application" }).click();

      await page.waitForURL(/identity:5001|connect\/authorize/);
      expect(page.url()).toContain("identity:5001");

      const cookies = await context.cookies();
      const sessionCookie = cookies.find((c) => c.name === "__Host-roleradar_session");
      expect(!sessionCookie || sessionCookie.value === "").toBe(true);
    });

    test("submits append event dialog on /applications/:id: updates timeline, updates status badge if terminal event, dialog closes, URL remains clean", async ({
      page,
      context,
    }) => {
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      await page.getByRole("button", { name: "Append Event" }).first().click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();
      await expect(page.getByText("Append Timeline Event")).toBeVisible();

      // Verify posting sync note appears only for rejected, offer_accepted, offer_declined, withdrawn
      const syncNote = modal.getByTestId("posting-sync-note");
      await expect(syncNote).not.toBeVisible();

      await page.getByLabel("Event Type *").selectOption("interview");
      await expect(syncNote).not.toBeVisible();

      await page.getByLabel("Event Type *").selectOption("offer");
      await expect(syncNote).not.toBeVisible();

      await page.getByLabel("Event Type *").selectOption("offer_accepted");
      await expect(syncNote).toBeVisible();
      await expect(syncNote).toContainText("Closed");

      await page.getByLabel("Event Type *").selectOption("offer_declined");
      await expect(syncNote).toBeVisible();
      await expect(syncNote).toContainText("Closed");

      await page.getByLabel("Event Type *").selectOption("withdrawn");
      await expect(syncNote).toBeVisible();
      await expect(syncNote).toContainText("Closed");

      await page.getByLabel("Event Type *").selectOption("rejected");
      await expect(syncNote).toBeVisible();
      await expect(syncNote).toContainText("Rejected");

      await page.getByLabel("Event Date *").fill("2026-11-01");
      await page.getByLabel("Notes & Details").fill("Received standard rejection email after phone screen.");
      await modal.getByRole("button", { name: "Append Event" }).click();

      // Dialog closes
      await expect(modal).not.toBeVisible();
      expect(page.url()).toBe(`${baseUrl}/applications/app-e2e-1`);

      // Timeline contains the new event
      await expect(page.getByText("Received standard rejection email after phone screen.")).toBeVisible();

      // Terminal event updated status badge to Rejected
      const badge = page.getByTestId("application-status-badge");
      await expect(badge).toHaveText("Rejected");
    });

    test("submits append event with 400 validation error: shows inline error in modal without closing", async ({
      page,
      context,
    }) => {
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 400;
      nextApplicationResponseBody = {
        status: 400,
        errorCode: "validation_error",
        detail: "Invalid event date.",
        errors: { eventDate: ["Event date must be formatted as YYYY-MM-DD."] },
      };

      await page.getByRole("button", { name: "Append Event" }).first().click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await modal.getByRole("button", { name: "Append Event" }).click();

      await expect(page.getByText("Event date must be formatted as YYYY-MM-DD.")).toBeVisible();
      await expect(modal).toBeVisible();
    });

    test("submits append event with 409 conflict: shows conflict Toast with Reload action", async ({
      page,
      context,
    }) => {
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 409;
      nextApplicationResponseBody = {
        status: 409,
        errorCode: "concurrency_conflict",
        detail: "This application was updated concurrently.",
      };

      await page.getByRole("button", { name: "Append Event" }).first().click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await modal.getByRole("button", { name: "Append Event" }).click();

      await expect(page.getByText("Concurrency Conflict")).toBeVisible();
      const reloadBtn = page.getByRole("button", { name: "Reload" });
      await expect(reloadBtn).toBeVisible();
      await reloadBtn.click();
      await expect(page.getByText("Concurrency Conflict")).not.toBeVisible();
    });

    test("submits append event with bad CSRF: shows error alert without raw response", async ({
      page,
      context,
    }) => {
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      await page.getByRole("button", { name: "Append Event" }).first().click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await page.evaluate(() => {
        const modalEl = document.querySelector('[role="dialog"]');
        const input = modalEl?.querySelector('input[name="_csrf"]') as HTMLInputElement;
        if (input) {
          input.value = "tampered-bad-csrf";
        }
      });

      await modal.getByRole("button", { name: "Append Event" }).click();

      const alert = modal.getByRole("alert");
      await expect(alert).toBeVisible();
      await expect(alert).toContainText("Invalid or missing CSRF token");
    });

    test("submits append event with API 401: redirects into authorization flow, avoids loop, and clears session cookie", async ({
      page,
      context,
    }) => {
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      nextApplicationResponseStatus = 401;
      nextApplicationResponseBody = {
        status: 401,
        errorCode: "unauthorized",
        detail: "Session token expired",
      };

      // Stub the Identity authorize endpoint so this test never depends on a listener on host port 5001
      await page.route("**/connect/authorize*", (route) => {
        route.fulfill({
          status: 200,
          contentType: "text/html",
          body: "<html><body>Mock Identity Provider Login Page</body></html>",
        });
      });

      await page.getByRole("button", { name: "Append Event" }).first().click();
      const modal = page.getByRole("dialog");
      await expect(modal).toBeVisible();

      await modal.getByRole("button", { name: "Append Event" }).click();

      await page.waitForURL(/identity:5001|connect\/authorize/);
      expect(page.url()).toContain("identity:5001");

      const cookies = await context.cookies();
      const sessionCookie = cookies.find((c) => c.name === "__Host-roleradar_session");
      expect(!sessionCookie || sessionCookie.value === "").toBe(true);
    });

    test("hostile application data with <script> and HTML renders safely as plain text without script execution", async ({
      page,
      context,
    }) => {
      let alertTriggered = false;
      page.on("dialog", async (dialog) => {
        alertTriggered = true;
        await dialog.dismiss();
      });

      currentPostingTitle = "<script>alert('hostile-posting-title')</script>";
      currentApplicationEvents = [
        {
          eventType: "applied",
          eventDate: "2026-10-31",
          notes: "<iframe src='evil.com'></iframe><script>alert('hostile-app-notes')</script>",
        },
      ];

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/applications/app-e2e-1`, { waitUntil: "networkidle" });

      expect(alertTriggered).toBe(false);
      expect(await page.locator("script:has-text('hostile-posting-title')").count()).toBe(0);
      expect(await page.locator("script:has-text('hostile-app-notes')").count()).toBe(0);
      expect(await page.locator("iframe").count()).toBe(0);
    });
  });

  test.describe("Operational Views (Real Chromium - Phase 8)", () => {
    test("loads /criteria list with API stubbed, asserting only effectiveDate is sent, zero CSP violations, axe zero serious/critical in light and dark, and zero mutation controls", async ({
      page,
      context,
    }) => {
      const cspErrors: string[] = [];
      page.on("console", (msg) => {
        if (msg.type() === "error" && msg.text().includes("Content Security Policy")) {
          cspErrors.push(msg.text());
        }
      });

      currentCriteriaList = [
        {
          id: "crit-e2e-1",
          name: "Remote Only",
          category: "hard_skip",
          ruleText: "Must be 100% remote within US.",
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: null,
          isActive: true,
        },
      ];

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/criteria?effectiveDate=2026-06-01&badParam=123`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: "Screening Criteria", level: 1 })).toBeVisible();
      expect(cspErrors).toEqual([]);

      // Verify parameter filtering: effectiveDate becomes the effective-interval filter, nothing else is forwarded
      expect(lastCriteriaQueryReceived["$filter"]).toBe(
        "(effectiveFrom le 2026-06-01T00:00:00Z) and ((effectiveTo eq null) or (effectiveTo gt 2026-06-01T00:00:00Z))"
      );
      expect(lastCriteriaQueryReceived["$orderby"]).toBe("name asc");
      expect(Object.keys(lastCriteriaQueryReceived).sort()).toEqual(["$count", "$filter", "$orderby", "$skip", "$top"]);
      expect(lastCriteriaQueryReceived.effectiveDate).toBeUndefined();
      expect(lastCriteriaQueryReceived.badParam).toBeUndefined();

      // Criteria content visible
      await expect(page.getByText("Remote Only")).toBeVisible();
      await expect(page.getByText("Must be 100% remote within US.")).toBeVisible();

      // Axe check in both themes
      await checkAxeInThemes(page);

      // Invariant: zero mutation controls (no forms other than Sign Out, zero edit/create/delete buttons)
      const forms = await page.locator("form").all();
      for (const form of forms) {
        const action = await form.getAttribute("action");
        expect(action).toBe("/auth/logout");
      }
      const buttons = await page.getByRole("button").all();
      for (const btn of buttons) {
        const text = ((await btn.textContent()) || "").toLowerCase();
        expect(text).not.toMatch(/create|edit|delete|save|add/);
      }
    });

    test("loads /criteria empty state cleanly", async ({ page, context }) => {
      currentCriteriaList = [];
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/criteria`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: "Screening Criteria", level: 1 })).toBeVisible();
      await expect(page.getByText("No criteria found.")).toBeVisible();
    });

    test("loads /notes list with API stubbed, asserting only whitelisted OData options sent (never content), zero CSP violations, axe zero serious/critical in light and dark, and zero mutation controls", async ({
      page,
      context,
    }) => {
      const cspErrors: string[] = [];
      page.on("console", (msg) => {
        if (msg.type() === "error" && msg.text().includes("Content Security Policy")) {
          cspErrors.push(msg.text());
        }
      });

      currentNotesList = [
        {
          id: "note-e2e-1",
          heading: "Senior SRE Architecture Review",
          tags: ["distributed-systems", "sre"],
          linkedEntityType: "architecture",
          linkedEntityId: null,
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-16T12:00:00Z",
        },
      ];

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/notes?q=architecture&limit=25&offset=0&bad=param&sort=foo`, {
        waitUntil: "networkidle",
      });

      await expect(page.getByRole("heading", { name: "Notes", level: 1 })).toBeVisible();
      expect(cspErrors).toEqual([]);

      // Verify parameters: q becomes a heading filter, the unknown sort falls back to updatedAt desc,
      // and neither $select nor the note body is ever requested.
      expect(lastNotesQueryReceived["$filter"]).toBe("contains(tolower(heading),'architecture')");
      expect(lastNotesQueryReceived["$orderby"]).toBe("updatedAt desc");
      expect(lastNotesQueryReceived["$top"]).toBe("25");
      expect(lastNotesQueryReceived["$skip"]).toBe("0");
      expect(Object.keys(lastNotesQueryReceived).sort()).toEqual(["$count", "$filter", "$orderby", "$skip", "$top"]);
      expect(lastNotesQueryReceived.q).toBeUndefined();
      expect(lastNotesQueryReceived.bad).toBeUndefined();

      // Content rendered
      await expect(page.getByText("Senior SRE Architecture Review")).toBeVisible();

      // Axe check in both themes
      await checkAxeInThemes(page);

      // Invariant: zero mutation controls (no POST forms outside signOut)
      const forms = await page.locator("form").all();
      for (const form of forms) {
        const method = (await form.getAttribute("method"))?.toLowerCase();
        if (method === "post") {
          const action = await form.getAttribute("action");
          expect(action).toBe("/auth/logout");
        }
      }
    });

    test("loads /notes with no search term and lists recent notes, sorted by updatedAt descending", async ({
      page,
      context,
    }) => {
      currentNotesList = [
        {
          id: "note-e2e-2",
          heading: "Recent Planning Note",
          tags: [],
          linkedEntityType: "",
          linkedEntityId: null,
          createdAt: "2026-10-20T12:00:00Z",
          updatedAt: "2026-10-21T12:00:00Z",
        },
      ];
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/notes`, { waitUntil: "networkidle" });

      await expect(page.getByText("Recent Planning Note")).toBeVisible();
      expect(lastNotesQueryReceived["$filter"]).toBeUndefined();
      expect(lastNotesQueryReceived["$orderby"]).toBe("updatedAt desc");
    });

    test("loads /notes empty state cleanly", async ({ page, context }) => {
      currentNotesList = [];
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/notes?q=nonexistent`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: "Notes", level: 1 })).toBeVisible();
      await expect(page.getByText("No notes found.")).toBeVisible();
    });

    test("loads /notes/:id detail with API stubbed, asserting safe plain text rendering of hostile script/HTML, zero CSP violations, axe zero serious/critical, and zero mutation controls", async ({
      page,
      context,
    }) => {
      let alertTriggered = false;
      page.on("dialog", async (dialog) => {
        alertTriggered = true;
        await dialog.dismiss();
      });

      const cspErrors: string[] = [];
      page.on("console", (msg) => {
        if (msg.type() === "error" && msg.text().includes("Content Security Policy")) {
          cspErrors.push(msg.text());
        }
      });

      currentNoteDetail = {
        id: "note-e2e-1",
        heading: "<script>alert('hostile-heading')</script>System Scaling Notes",
        content: "Multi-line note body.\n<script>alert('hostile-content')</script>\n<a href='javascript:alert(1)'>Exploit</a>\nSecond line preserved.",
        tags: ["<script>alert('tag-xss')</script>", "kubernetes"],
        linkedEntityType: "posting",
        linkedEntityId: "post-e2e-1",
        createdAt: "2026-10-15T12:00:00Z",
        updatedAt: "2026-10-16T12:00:00Z",
      };

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/notes/note-e2e-1`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { level: 1 })).toContainText("System Scaling Notes");
      expect(cspErrors).toEqual([]);
      expect(alertTriggered).toBe(false);

      // Untrusted content renders as text, scripts never executed
      expect(await page.locator("script:has-text('hostile-heading')").count()).toBe(0);
      expect(await page.locator("script:has-text('hostile-content')").count()).toBe(0);
      expect(await page.locator("script:has-text('tag-xss')").count()).toBe(0);
      expect(await page.locator('a[href^="javascript:"]').count()).toBe(0);

      // The body is rendered as Markdown (ADR 0032): raw HTML is visible text, line breaks survive, and the markup writes no style attribute
      const contentEl = page.getByTestId("note-content");
      await expect(contentEl).toBeVisible();
      expect(await contentEl.getAttribute("style")).toBeNull();
      await expect(contentEl.locator(".markdown-body")).toBeVisible();
      await expect(contentEl).toContainText("<script>alert('hostile-content')</script>");
      await expect(contentEl.locator("script, a, img, iframe")).toHaveCount(0);
      await expect(contentEl.locator("br")).not.toHaveCount(0);
      await expect(contentEl.locator("[style]")).toHaveCount(0);

      // Linked entity route check
      const linkedPosting = page.getByRole("link", { name: /Job Opportunity/i });
      await expect(linkedPosting).toBeVisible();
      expect(await linkedPosting.getAttribute("href")).toBe("/postings/post-e2e-1");

      // Axe check in both themes
      await checkAxeInThemes(page);

      // Invariant: zero mutation controls (no POST forms outside signOut)
      const forms = await page.locator("form").all();
      for (const form of forms) {
        const method = (await form.getAttribute("method"))?.toLowerCase();
        if (method === "post") {
          const action = await form.getAttribute("action");
          expect(action).toBe("/auth/logout");
        }
      }
      const buttons = await page.getByRole("button").all();
      for (const btn of buttons) {
        const text = ((await btn.textContent()) || "").toLowerCase();
        expect(text).not.toMatch(/create|edit|delete|save|add/);
      }
    });

    test("loads /search-runs list with API stubbed, asserting only whitelisted OData options sent, zero CSP violations, axe zero serious/critical, and zero mutation controls", async ({
      page,
      context,
    }) => {
      const cspErrors: string[] = [];
      page.on("console", (msg) => {
        if (msg.type() === "error" && msg.text().includes("Content Security Policy")) {
          cspErrors.push(msg.text());
        }
      });

      currentSearchRunsList = [
        {
          id: "run-e2e-1",
          runDate: "2026-10-31T08:00:00Z",
          newPostings: 3,
          summary: "Automated batch search run completed with 3 new postings.",
        },
      ];

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/search-runs?limit=25&offset=0&badParam=1&sort=date`, {
        waitUntil: "networkidle",
      });

      await expect(page.getByRole("heading", { name: "Search Runs", level: 1 })).toBeVisible();
      expect(cspErrors).toEqual([]);

      // Verify parameters: newest run first by default (the unknown sort is dropped), nothing else forwarded
      expect(lastSearchRunsQueryReceived["$orderby"]).toBe("runDate desc");
      expect(lastSearchRunsQueryReceived["$top"]).toBe("25");
      expect(lastSearchRunsQueryReceived["$skip"]).toBe("0");
      expect(Object.keys(lastSearchRunsQueryReceived).sort()).toEqual(["$count", "$orderby", "$skip", "$top"]);
      expect(lastSearchRunsQueryReceived.badParam).toBeUndefined();
      expect(lastSearchRunsQueryReceived.sort).toBeUndefined();

      // Content rendered
      await expect(page.getByText("Automated batch search run completed with 3 new postings.")).toBeVisible();

      // Axe check in both themes
      await checkAxeInThemes(page);

      // Invariant: zero mutation controls (no POST forms outside signOut)
      const forms = await page.locator("form").all();
      for (const form of forms) {
        const method = (await form.getAttribute("method"))?.toLowerCase();
        if (method === "post") {
          const action = await form.getAttribute("action");
          expect(action).toBe("/auth/logout");
        }
      }
      const buttons = await page.getByRole("button").all();
      for (const btn of buttons) {
        const text = ((await btn.textContent()) || "").toLowerCase();
        expect(text).not.toMatch(/create|edit|delete|save|add/);
      }
    });

    test("loads /search-runs empty state cleanly", async ({ page, context }) => {
      currentSearchRunsList = [];
      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/search-runs`, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: "Search Runs", level: 1 })).toBeVisible();
      await expect(page.getByText("No search runs found.")).toBeVisible();
    });
  });
});
