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

let server: https.Server | http.Server;
let apiServer: http.Server;
let baseUrl: string;
let tempTlsDir: string;
const E2E_SESSION_ID = "e2e-auth-session-id-playwright";

test.beforeAll(async () => {
  // Generate throwaway self-signed certificate and key in temporary directory (zero dependency on gitignored files)
  tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-tls-"));
  const certPath = path.join(tempTlsDir, "throwaway-cert.crt");
  const keyPath = path.join(tempTlsDir, "throwaway-key.key");

  const pems = await selfsigned.generate(
    [{ name: "commonName", value: "127.0.0.1" }],
    { days: 1 }
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
              title: "Principal Engineer",
              requisitionId: "REQ-101",
              location: "Remote",
              disposition: "screened_in",
              resurfacePolicy: "resurface_on_change",
              score: 92.5,
              versionToken: "ver-1",
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
          title: "Principal Engineer",
          requisitionId: "REQ-101",
          location: "Remote",
          disposition: "screened_in",
          resurfacePolicy: "resurface_on_change",
          score: 92.5,
          versionToken: "ver-1",
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
              name: "Remote Work",
              signalType: "positive",
              detail: "100% remote flexibility",
            },
          ],
          assessmentNote: null,
        })
      );
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

    // Assert zero CSP violations
    expect(cspViolations).toEqual([]);
  });

  test("loads /postings/:id detail with API stubbed, asserting zero CSP violations and axe zero serious/critical in light and dark", async ({
    page,
    context,
  }) => {
    await setupSessionCookie(context);

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

    // Assert zero CSP violations
    expect(cspViolations).toEqual([]);
  });
});
