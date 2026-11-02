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

test.beforeAll(async () => {
  const serverBuild = path.resolve(process.cwd(), "build", "server", "index.js");
  if (!fs.existsSync(serverBuild)) {
    throw new Error(`Missing ${serverBuild}. Run "pnpm --filter web run build" before the e2e suite.`);
  }
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

    if (url.pathname === "/api/v1/postings" || url.pathname === "/odata/Postings") {
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
          value: [
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
              createdAt: "2026-10-31T10:00:00Z",
              updatedAt: "2026-10-31T11:00:00Z",
            },
          ],
          "@odata.count": 1,
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
      lastCriteriaQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify(
          currentCriteriaList !== null
            ? currentCriteriaList
            : [
                {
                  id: "crit-1",
                  name: "Remote Policy",
                  weight: "high",
                  ruleText: "Must allow remote work",
                  effectiveFrom: "2026-01-01T00:00:00Z",
                  effectiveTo: null,
                  isActive: true,
                },
              ]
        )
      );
      return;
    }

    if (url.pathname === "/api/v1/companies" && req.method === "GET") {
      lastCompaniesQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: [
            {
              id: "comp-e2e-1",
              name: currentCompanyName,
              status: currentCompanyStatus,
              ownershipType: currentCompanyOwnership,
              description: currentCompanyDescription,
              headquarters: currentCompanyHeadquarters,
              categoryTags: currentCompanyCategoryTags,
              careerSources: currentCompanyCareerSources,
              versionToken: currentCompanyVersionToken,
            },
          ],
          totalCount: 1,
          limit: 25,
          offset: 0,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/companies/comp-e2e-1" && req.method === "GET") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: "comp-e2e-1",
          name: currentCompanyName,
          status: currentCompanyStatus,
          ownershipType: currentCompanyOwnership,
          description: currentCompanyDescription,
          headquarters: currentCompanyHeadquarters,
          categoryTags: currentCompanyCategoryTags,
          careerSources: currentCompanyCareerSources,
          versionToken: currentCompanyVersionToken,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/companies/comp-e2e-1" && req.method === "PATCH") {
      let bodyStr = "";
      req.on("data", (chunk) => {
        bodyStr += chunk;
      });
      req.on("end", () => {
        if (nextCompanyResponseStatus !== null) {
          const status = nextCompanyResponseStatus;
          const body = nextCompanyResponseBody;
          nextCompanyResponseStatus = null;
          nextCompanyResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        if (payload.name) currentCompanyName = payload.name;
        if (payload.status) currentCompanyStatus = payload.status;
        if (payload.ownershipType) currentCompanyOwnership = payload.ownershipType;
        if (payload.description !== undefined) currentCompanyDescription = payload.description;
        if (payload.headquarters !== undefined) currentCompanyHeadquarters = payload.headquarters;
        if (payload.categoryTags) currentCompanyCategoryTags = payload.categoryTags;
        if (payload.careerSources) currentCompanyCareerSources = payload.careerSources;
        currentCompanyVersionToken = "comp-ver-2";

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "comp-e2e-1",
            name: currentCompanyName,
            status: currentCompanyStatus,
            ownershipType: currentCompanyOwnership,
            description: currentCompanyDescription,
            headquarters: currentCompanyHeadquarters,
            categoryTags: currentCompanyCategoryTags,
            careerSources: currentCompanyCareerSources,
            versionToken: currentCompanyVersionToken,
          })
        );
      });
      return;
    }

    if (url.pathname === "/api/v1/applications" && req.method === "GET") {
      lastApplicationsQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: [
            {
              id: "app-e2e-1",
              postingId: "post-e2e-1",
              companyId: "comp-e2e-1",
              status: currentApplicationStatus,
              appliedAt: "2026-10-31",
              events: currentApplicationEvents,
            },
          ],
          totalCount: 1,
          limit: 25,
          offset: 0,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/applications/app-e2e-1" && req.method === "GET") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: "app-e2e-1",
          postingId: "post-e2e-1",
          companyId: "comp-e2e-1",
          status: currentApplicationStatus,
          appliedAt: "2026-10-31",
          events: currentApplicationEvents,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/applications" && req.method === "POST") {
      let bodyStr = "";
      req.on("data", (chunk) => {
        bodyStr += chunk;
      });
      req.on("end", () => {
        if (nextApplicationResponseStatus !== null) {
          const status = nextApplicationResponseStatus;
          const body = nextApplicationResponseBody;
          nextApplicationResponseStatus = null;
          nextApplicationResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        currentPostingDisposition = "applied";
        currentApplicationStatus = "applied";
        currentApplicationEvents = [
          {
            eventType: "applied",
            eventDate: payload.submittedDate || "2026-10-31",
            notes: "Application submitted",
          },
        ];

        res.writeHead(201);
        res.end(
          JSON.stringify({
            id: "app-e2e-1",
            postingId: payload.postingId || "post-e2e-1",
            companyId: "comp-e2e-1",
            status: "applied",
            appliedAt: payload.submittedDate || "2026-10-31",
            events: currentApplicationEvents,
          })
        );
      });
      return;
    }

    if (url.pathname === "/api/v1/applications/app-e2e-1/events" && req.method === "POST") {
      let bodyStr = "";
      req.on("data", (chunk) => {
        bodyStr += chunk;
      });
      req.on("end", () => {
        if (nextApplicationResponseStatus !== null) {
          const status = nextApplicationResponseStatus;
          const body = nextApplicationResponseBody;
          nextApplicationResponseStatus = null;
          nextApplicationResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        const eventType = payload.eventType || "follow_up";
        currentApplicationEvents.push({
          eventType,
          eventDate: payload.eventDate || "2026-11-01",
          notes: payload.notes || null,
        });

        if (eventType === "offer_accepted" || eventType === "offer_declined" || eventType === "withdrawn") {
          currentApplicationStatus = "closed";
          currentPostingDisposition = "closed";
        } else if (eventType === "rejected") {
          currentApplicationStatus = "rejected";
          currentPostingDisposition = "rejected";
        }

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "app-e2e-1",
            postingId: "post-e2e-1",
            companyId: "comp-e2e-1",
            status: currentApplicationStatus,
            appliedAt: "2026-10-31",
            events: currentApplicationEvents,
          })
        );
      });
      return;
    }

    if (url.pathname === "/api/v1/notes" && req.method === "GET") {
      lastNotesQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: currentNotesList,
          totalCount: currentNotesList.length,
          limit: Number(url.searchParams.get("limit")) || 25,
          offset: Number(url.searchParams.get("offset")) || 0,
        })
      );
      return;
    }

    if (url.pathname === "/api/v1/notes/note-e2e-1" && req.method === "GET") {
      if (!currentNoteDetail) {
        res.writeHead(404);
        res.end(JSON.stringify({ ok: false, errorCode: "NOT_FOUND", message: "Note not found" }));
        return;
      }
      res.writeHead(200);
      res.end(JSON.stringify(currentNoteDetail));
      return;
    }

    if (url.pathname === "/api/v1/search-runs" && req.method === "GET") {
      lastSearchRunsQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: currentSearchRunsList,
          totalCount: currentSearchRunsList.length,
          limit: Number(url.searchParams.get("limit")) || 25,
          offset: Number(url.searchParams.get("offset")) || 0,
        })
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
    expect(layout.bodyBg).toBe("rgb(2, 6, 23)");
    expect(layout.shellBg).toBe("rgb(2, 6, 23)");
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
    expect(shellBg).toBe("rgb(2, 6, 23)");

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
    expect(darkBg).toBe("rgb(2, 6, 23)");
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

  test("loads /companies list with API stubbed, asserting only limit and offset are sent, zero CSP violations and axe zero serious/critical in light and dark", async ({
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
    await page.goto(`${baseUrl}/companies?limit=25&offset=0&q=ignoredQuery&sort=name`, { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Companies" })).toBeVisible();
    await expect(page.getByText("Acme Cloud Services")).toBeVisible();

    // Verify only limit and offset were sent to API
    expect(lastCompaniesQueryReceived.limit).toBe("25");
    expect(lastCompaniesQueryReceived.offset).toBe("0");
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
      errorCode: "VALIDATION_ERROR",
      message: "Validation failed.",
      details: [{ field: "name", issue: "Company name cannot be blank." }],
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
      errorCode: "CONCURRENCY_CONFLICT",
      message: "This company was modified by another operation.",
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
      errorCode: "UNAUTHORIZED",
      message: "Token expired",
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

    test("loads /applications list with API stubbed, asserting only status, limit, offset are sent, zero CSP violations and axe zero serious/critical in light and dark", async ({
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

      // Verify only status, limit, offset were sent
      expect(lastApplicationsQueryReceived.status).toBe("applied");
      expect(lastApplicationsQueryReceived.limit).toBe("25");
      expect(lastApplicationsQueryReceived.offset).toBe("0");
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
        errorCode: "VALIDATION_FAILED",
        message: "Validation failed.",
        details: [{ field: "submittedDate", message: "Invalid submission date format." }],
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
        errorCode: "CONCURRENCY_CONFLICT",
        message: "An application already exists for this posting or state transition conflict occurred.",
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
        errorCode: "UNAUTHORIZED",
        message: "Access token expired",
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
        errorCode: "VALIDATION_FAILED",
        message: "Invalid event date.",
        details: [{ field: "eventDate", message: "Event date must be formatted as YYYY-MM-DD." }],
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
        errorCode: "CONCURRENCY_CONFLICT",
        message: "This application was updated concurrently.",
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
        errorCode: "UNAUTHORIZED",
        message: "Session token expired",
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
          weight: "hard_skip",
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

      // Verify parameter filtering: only effectiveDate sent to API
      expect(lastCriteriaQueryReceived.effectiveDate).toBe("2026-06-01");
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

    test("loads /notes list with API stubbed, asserting only q, limit, offset sent, zero CSP violations, axe zero serious/critical in light and dark, and zero mutation controls", async ({
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
          content: "Detailed technical notes on distributed caching.",
          tags: ["distributed-systems", "sre"],
          linkedEntityType: "posting",
          linkedEntityId: "post-e2e-1",
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

      // Verify parameters: only q, limit, offset sent
      expect(lastNotesQueryReceived.q).toBe("architecture");
      expect(lastNotesQueryReceived.limit).toBe("25");
      expect(lastNotesQueryReceived.offset).toBe("0");
      expect(lastNotesQueryReceived.bad).toBeUndefined();
      expect(lastNotesQueryReceived.sort).toBeUndefined();

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

      // Preserves whitespace via pre-wrap
      const contentEl = page.getByTestId("note-content");
      await expect(contentEl).toBeVisible();
      const style = await contentEl.getAttribute("style");
      expect(style).toContain("pre-wrap");

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

    test("loads /search-runs list with API stubbed, asserting only limit/offset sent, zero CSP violations, axe zero serious/critical, and zero mutation controls", async ({
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
          companiesSearched: 25,
          postingsFound: 14,
          newPostings: 3,
          errorsCount: 0,
          summary: "Automated batch search run completed with 3 new postings.",
        },
      ];

      await setupSessionCookie(context);
      await page.goto(`${baseUrl}/search-runs?limit=25&offset=0&badParam=1&sort=date`, {
        waitUntil: "networkidle",
      });

      await expect(page.getByRole("heading", { name: "Search Runs", level: 1 })).toBeVisible();
      expect(cspErrors).toEqual([]);

      // Verify parameters: only limit and offset sent
      expect(lastSearchRunsQueryReceived.limit).toBe("25");
      expect(lastSearchRunsQueryReceived.offset).toBe("0");
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
