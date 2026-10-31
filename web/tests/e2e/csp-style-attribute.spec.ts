import { test, expect, type Page, type BrowserContext } from "@playwright/test";
import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import net from "node:net";
import type https from "node:https";
import http from "node:http";
import selfsigned from "selfsigned";
import axe from "axe-core";
import { createWebServer } from "../../server.js";
import { saveSession } from "../../app/lib/session.server.js";
import * as client from "openid-client";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";

let server: https.Server | http.Server;
let apiServer: http.Server;
let baseUrl: string;
let tempTlsDir: string;
const E2E_SESSION_ID = "e2e-auth-session-id-playwright";

let currentPostingDisposition = "lead";
let currentPostingVersionToken = "ver-1";
let currentPostingTitle = "Principal Engineer";
let nextTransitionResponseStatus: number | null = null;
let nextTransitionResponseBody: unknown = null;

test.beforeAll(async () => {
  const mockOidcConfig = new client.Configuration(
    {
      issuer: "https://identity:5001",
      authorization_endpoint: "https://identity:5001/connect/authorize",
      token_endpoint: "https://identity:5001/connect/token",
      end_session_endpoint: "https://identity:5001/connect/endsession",
    },
    "roleradar-web",
    "playwright-test-client-secret"
  );
  setOidcConfigForTesting(mockOidcConfig);
  // Generate throwaway self-signed certificate and key in temporary directory (zero dependency on gitignored files)
  tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-tls-"));
  const certPath = path.join(tempTlsDir, "throwaway-cert.crt");
  const keyPath = path.join(tempTlsDir, "throwaway-key.key");

  const pems = await selfsigned.generate(
    [{ name: "commonName", value: "127.0.0.1" }],
    { notAfterDate: new Date(Date.now() + 86400_000) }
  );
  fs.writeFileSync(certPath, pems.cert, "utf8");
  fs.writeFileSync(keyPath, pems.private, "utf8");

  process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
  process.env.ROLERADAR_WEB_CLIENT_SECRET = "playwright-test-client-secret";
  process.env.HTTPS_CERT_PATH = certPath;
  process.env.HTTPS_CERT_KEY_PATH = keyPath;
  process.env.COOKIE_SECURE = "true";

  // Stub RoleRadar API at the network layer on a free loopback port (no real tokens)
  apiServer = http.createServer((req, res) => {
    const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
    res.setHeader("Content-Type", "application/json");

    if (url.pathname === "/api/v1/postings") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: [
            {
              id: "post-e2e-1",
              companyId: "comp-1",
              companyName: "Acme Corp",
              title: currentPostingTitle,
              requisitionId: "REQ-101",
              location: "Remote",
              disposition: currentPostingDisposition,
              resurfacePolicy: "never",
              score: 92.5,
              versionToken: currentPostingVersionToken,
              createdAt: "2026-10-31T10:00:00Z",
              updatedAt: "2026-10-31T11:00:00Z",
            },
          ],
          totalCount: 1,
          limit: 25,
          offset: 0,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/postings/post-e2e-1") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: "post-e2e-1",
          companyId: "comp-1",
          companyName: "Acme Corp",
          title: currentPostingTitle,
          requisitionId: "REQ-101",
          location: "Remote",
          disposition: currentPostingDisposition,
          resurfacePolicy: "never",
          score: 92.5,
          versionToken: currentPostingVersionToken,
          createdAt: "2026-10-31T10:00:00Z",
          updatedAt: "2026-10-31T11:00:00Z",
          url: "https://example.com/jobs/101",
          compensation: {
            currency: "USD",
            rawText: "$180,000 - $220,000",
            hasSalary: true,
          },
          signals: [
            {
              signalType: "positive",
              evidence: "100% remote flexibility",
              detectedAt: "2026-10-31T10:00:00Z",
            },
          ],
          assessmentNote: null,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/postings/post-e2e-1/transition" && req.method === "POST") {
      let bodyStr = "";
      req.on("data", (chunk) => {
        bodyStr += chunk;
      });
      req.on("end", () => {
        if (nextTransitionResponseStatus !== null) {
          const status = nextTransitionResponseStatus;
          const body = nextTransitionResponseBody;
          nextTransitionResponseStatus = null;
          nextTransitionResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        currentPostingDisposition = payload.toDisposition || "scored";
        currentPostingVersionToken = "ver-2";

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "post-e2e-1",
            companyId: "comp-1",
            companyName: "Acme Corp",
            title: currentPostingTitle,
            requisitionId: "REQ-101",
            location: "Remote",
            disposition: currentPostingDisposition,
            resurfacePolicy: payload.resurfacePolicy || "never",
            score: 92.5,
            versionToken: currentPostingVersionToken,
            createdAt: "2026-10-31T10:00:00Z",
            updatedAt: "2026-10-31T12:00:00Z",
            url: "https://example.com/jobs/101",
            compensation: {
              currency: "USD",
              rawText: "$180,000 - $220,000",
              hasSalary: true,
            },
            signals: [
              {
                signalType: "positive",
                evidence: "100% remote flexibility",
                detectedAt: "2026-10-31T10:00:00Z",
              },
            ],
            assessmentNote: null,
          })
        );
      });
      return;
    }

    if (url.pathname === "/api/v1/criteria") {
      res.writeHead(200);
      res.end(
        JSON.stringify([
          {
            id: "crit-1",
            name: "Remote Policy",
            weight: "high",
            ruleText: "Must allow remote work",
            effectiveFrom: "2026-01-01T00:00:00Z",
            effectiveTo: null,
            isActive: true,
          },
        ])
      );
      return;
    }

    res.writeHead(404);
    res.end(JSON.stringify({ ok: false, errorCode: "NOT_FOUND", message: "Not found" }));
  });

  await new Promise<void>((resolve) => {
    apiServer.listen(0, "127.0.0.1", () => {
      const addr = apiServer.address() as net.AddressInfo;
      process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${addr.port}`;
      resolve();
    });
  });

  // Seed authenticated user session into shared in-memory session store
  saveSession({
    sessionId: E2E_SESSION_ID,
    ownerSub: "playwright-test-user",
    accessToken: "stubbed-access-token",
    refreshToken: "stubbed-refresh-token",
    expiresAt: Date.now() + 3600_000,
    createdAt: Date.now(),
    csrfToken: "playwright-csrf-token",
  });

  server = createWebServer();

  await new Promise<void>((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const addr = server.address() as net.AddressInfo;
      baseUrl = `https://127.0.0.1:${addr.port}`;
      resolve();
    });
  });
});

test.afterAll(async () => {
  setOidcConfigForTesting(null);
  if (server) {
    await new Promise<void>((resolve) => {
      server.close(() => resolve());
    });
  }
  if (apiServer) {
    await new Promise<void>((resolve) => {
      apiServer.close(() => resolve());
    });
  }
  if (tempTlsDir && fs.existsSync(tempTlsDir)) {
    fs.rmSync(tempTlsDir, { recursive: true, force: true });
  }
});

async function setupSessionCookie(context: BrowserContext) {
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
    return await (window as any).axe.run(document, {
      runOnly: {
        type: "tag",
        values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"],
      },
    });
  });
  return results.violations.filter(
    (v: any) => v.impact === "serious" || v.impact === "critical"
  );
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

    // Verify EmptyState vendored design system component has inline computed styles applied before hydration
    const emptyStateIcon = page.locator("span[aria-hidden='true']");
    await expect(emptyStateIcon).toBeVisible();

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

    // Verify axe accessibility in light mode
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "light");
    });
    const lightViolations = await runAxeChecks(page);
    expect(lightViolations).toEqual([]);

    // Verify axe accessibility in dark mode
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "dark");
    });
    const darkViolations = await runAxeChecks(page);
    expect(darkViolations).toEqual([]);

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

    // Verify axe accessibility in light mode
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "light");
    });
    const lightViolations = await runAxeChecks(page);
    expect(lightViolations).toEqual([]);

    // Verify axe accessibility in dark mode
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "dark");
    });
    const darkViolations = await runAxeChecks(page);
    expect(darkViolations).toEqual([]);

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
      errorCode: "INVALID_TRANSITION",
      message: "Transition to this disposition is not allowed by policy.",
      details: [{ field: "toDisposition", message: "Transition not allowed." }],
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
      errorCode: "CONCURRENCY_CONFLICT",
      message: "This posting was modified by another process.",
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
      (window as any).__reloadedViaWindow = false;
      const origReload = window.location.reload.bind(window.location);
      window.location.reload = () => {
        (window as any).__reloadedViaWindow = true;
        origReload();
      };
    });

    // Click Reload action in Toast
    const reloadButton = toast.getByRole("button", { name: "Reload" });
    await reloadButton.click();

    // Verify page revalidated and fetched updated title
    await expect(page.getByRole("heading", { name: "Principal Engineer (Concurrently Updated)", level: 1 })).toBeVisible();

    const reloadedViaWindow = await page.evaluate(() => (window as any).__reloadedViaWindow);
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
      errorCode: "UNAUTHORIZED",
      message: "API access token is expired or unauthorized.",
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
