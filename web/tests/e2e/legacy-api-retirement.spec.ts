import { type BrowserContext, type Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import net from "node:net";
import type https from "node:https";
import http from "node:http";
import selfsigned from "selfsigned";
import * as client from "openid-client";
import { createWebServer } from "../../server.js";
import { saveSession } from "../../app/lib/session.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";

/**
 * T065: the six legacy list endpoints are retired. The web server calls the API from its loaders (not from the
 * browser), so the requests are recorded at the stub API the web server talks to. Every list page is visited with a
 * sort, a filter and a page change, plus a posting detail page, and no GET to a legacy list path may appear.
 * Detail, command and other /api/v1 calls stay allowed.
 */

const LEGACY_LIST_PATHS = [
  "/api/v1/postings",
  "/api/v1/companies",
  "/api/v1/applications",
  "/api/v1/notes",
  "/api/v1/criteria",
  "/api/v1/search-runs",
] as const;

interface RecordedRequest {
  method: string;
  pathname: string;
  search: string;
}

/** The legacy list calls in a request log: GET only, the exact collection path (a trailing slash counts). */
export function legacyListRequests(log: readonly RecordedRequest[]): string[] {
  return log
    .filter((r) => r.method === "GET")
    .filter((r) => (LEGACY_LIST_PATHS as readonly string[]).includes(r.pathname.replace(/\/+$/, "")))
    .map((r) => `${r.method} ${r.pathname}${r.search}`);
}

const E2E_SESSION_ID = "e2e-legacy-retirement-session";
const TOTAL = 60;

let server: https.Server | http.Server;
let apiServer: http.Server;
let baseUrl: string;
let tempTlsDir: string;
const requestLog: RecordedRequest[] = [];

const iso = (i: number) => new Date(Date.UTC(2026, 9, 31, 12, 0, 0) - i * 3_600_000).toISOString();

function rowsFor(set: string): Record<string, unknown>[] {
  return Array.from({ length: TOTAL }, (_, i) => {
    const n = i + 1;
    switch (set) {
      case "Postings":
        return {
          id: `post-${n}`, companyId: `comp-${n}`, companyName: `Company ${n}`, title: `Engineer ${n}`,
          requisitionId: `REQ-${n}`, location: "Remote", disposition: "lead", resurfacePolicy: "never",
          score: n, createdAt: iso(i), updatedAt: iso(i),
        };
      case "Companies":
        return {
          id: `comp-${n}`, name: `Company ${n}`, status: "active", ownershipType: "private",
          headquarters: "Remote", description: null, categoryTags: ["fintech"],
        };
      case "Applications":
        return {
          id: `app-${n}`, postingId: `post-${n}`, companyId: `comp-${n}`, postingTitle: `Engineer ${n}`,
          companyName: `Company ${n}`, status: "applied", appliedAt: iso(i), hasInterview: false,
          firstResponseAt: null, lastEventAt: iso(i), ageInDays: n,
        };
      case "Notes":
        return {
          id: `note-${n}`, heading: `Note ${n}`, tags: ["t"], linkedEntityType: "area", linkedEntityId: null,
          createdAt: iso(i), updatedAt: iso(i),
        };
      case "Criteria":
        return {
          id: `crit-${n}`, name: `Criterion ${n}`, category: "weight", ruleText: "Fictional rule",
          effectiveFrom: iso(i + 2000), effectiveTo: null, isActive: true,
        };
      default:
        return { id: `run-${n}`, runDate: iso(i), newPostings: n % 7, summary: `sweep ${n}` };
    }
  });
}

const POSTING_DETAIL = {
  id: "post-1", companyId: "comp-1", companyName: "Company 1", title: "Engineer 1", requisitionId: "REQ-1",
  location: "Remote", disposition: "lead", resurfacePolicy: "never", score: 1, versionToken: "vt-1",
  createdAt: iso(0), updatedAt: iso(0), url: "https://example.com/jobs/1",
  compensation: { currency: "USD", rawText: "$1", hasSalary: true },
  signals: [], assessmentNote: null,
};

async function setupSession(context: BrowserContext) {
  saveSession({
    sessionId: E2E_SESSION_ID,
    ownerSub: "legacy-retirement-owner",
    ownerUsername: "retire-e2e",
    accessToken: "mock-e2e-access-token",
    refreshToken: "mock-e2e-refresh-token",
    idToken: "mock-e2e-id-token",
    expiresAt: Date.now() + 3_600_000,
    createdAt: Date.now(),
    csrfToken: "mock-e2e-csrf-token",
  });
  await context.addCookies([
    { name: "__Host-roleradar_session", value: E2E_SESSION_ID, url: baseUrl, httpOnly: true, secure: true, sameSite: "Lax" },
  ]);
}

async function open(page: Page, route: string, heading: string) {
  await page.goto(`${baseUrl}${route}`, { waitUntil: "networkidle" });
  await expect(page.getByRole("heading", { name: heading, level: 1 })).toBeVisible();
}

async function sortBy(page: Page, header: string) {
  await page.locator(`th button:has-text('${header}')`).first().click();
  await expect(page).toHaveURL(/sort=/);
}

async function nextPage(page: Page) {
  await page.getByRole("button", { name: /next/i }).first().click();
  await expect(page).toHaveURL(/offset=25/);
}

test.describe("Legacy list API retirement (T065)", () => {
  test.beforeAll(async () => {
    tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-retire-"));
    const certPath = path.join(tempTlsDir, "test.crt");
    const keyPath = path.join(tempTlsDir, "test.key");
    const pems = await selfsigned.generate([{ name: "commonName", value: "127.0.0.1" }], {
      notAfterDate: new Date(Date.now() + 86_400_000),
    });
    fs.writeFileSync(certPath, pems.cert, "utf8");
    fs.writeFileSync(keyPath, pems.private, "utf8");

    process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
    process.env.ROLERADAR_WEB_CLIENT_SECRET = "test-secret";
    process.env.HTTPS_CERT_PATH = certPath;
    process.env.HTTPS_CERT_KEY_PATH = keyPath;
    process.env.COOKIE_SECURE = "true";

    setOidcConfigForTesting(
      new client.Configuration(
        {
          issuer: "https://identity:5001",
          authorization_endpoint: "https://identity:5001/connect/authorize",
          token_endpoint: "https://identity:5001/connect/token",
          end_session_endpoint: "https://identity:5001/connect/endsession",
        },
        "roleradar-web",
        "test-secret"
      )
    );

    const apiPort = await new Promise<number>((resolve) => {
      const s = net.createServer();
      s.listen(0, "127.0.0.1", () => {
        const port = (s.address() as net.AddressInfo).port;
        s.close(() => resolve(port));
      });
    });

    apiServer = http.createServer((req, res) => {
      const url = new URL(req.url ?? "/", `http://127.0.0.1:${apiPort}`);
      requestLog.push({ method: req.method ?? "GET", pathname: url.pathname, search: url.search });
      res.setHeader("Content-Type", "application/json");

      const odata = /^\/odata\/(Postings|Companies|Applications|Notes|Criteria|SearchRuns)$/.exec(url.pathname);
      if (odata) {
        const top = Number(url.searchParams.get("$top") || "25");
        const skip = Number(url.searchParams.get("$skip") || "0");
        res.writeHead(200);
        res.end(JSON.stringify({ value: rowsFor(odata[1]).slice(skip, skip + top), "@odata.count": TOTAL }));
        return;
      }

      if (url.pathname === "/api/v1/postings/post-1") {
        res.writeHead(200);
        res.end(JSON.stringify(POSTING_DETAIL));
        return;
      }

      // Anything else, including every legacy list path, is unknown to the stub.
      res.writeHead(404);
      res.end(JSON.stringify({ ok: false, errorCode: "not_found", message: "Not found" }));
    });
    await new Promise<void>((resolve) => apiServer.listen(apiPort, "127.0.0.1", resolve));

    process.env.NODE_ENV = "test";
    process.env.PORT = "0";
    process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${apiPort}`;

    server = createWebServer();
    await new Promise<void>((resolve) => {
      server.listen(0, "127.0.0.1", () => {
        baseUrl = `https://127.0.0.1:${(server.address() as net.AddressInfo).port}`;
        resolve();
      });
    });
  });

  test.afterAll(async () => {
    setOidcConfigForTesting(null);
    if (server) await new Promise<void>((resolve) => server.close(() => resolve()));
    if (apiServer) await new Promise<void>((resolve) => apiServer.close(() => resolve()));
    if (tempTlsDir && fs.existsSync(tempTlsDir)) fs.rmSync(tempTlsDir, { recursive: true, force: true });
  });

  test("the detector flags a legacy list call and ignores detail and command calls", () => {
    const log: RecordedRequest[] = [
      { method: "GET", pathname: "/api/v1/criteria", search: "" },
      { method: "GET", pathname: "/api/v1/postings/", search: "?limit=5" },
      { method: "GET", pathname: "/api/v1/postings/post-1", search: "" },
      { method: "GET", pathname: "/api/v1/notes/n-1", search: "" },
      { method: "POST", pathname: "/api/v1/applications", search: "" },
      { method: "PATCH", pathname: "/api/v1/companies/c-1", search: "" },
      { method: "GET", pathname: "/odata/Criteria", search: "?$top=5" },
    ];
    expect(legacyListRequests(log)).toEqual(["GET /api/v1/criteria", "GET /api/v1/postings/?limit=5"]);
  });

  test("a legacy list call that really reaches the API is recorded by the stub", async ({ request }) => {
    requestLog.length = 0;
    const apiBase = process.env.ROLERADAR_API_BASE_URL ?? "";
    await request.get(`${apiBase}/api/v1/search-runs`);
    expect(legacyListRequests(requestLog)).toEqual(["GET /api/v1/search-runs"]);
    requestLog.length = 0;
  });

  test("navigating every list page with a sort, a filter and a page change, and a posting detail, never calls a legacy list endpoint", async ({ page, context }) => {
    // Deterministic rendering for the whole run (same setting the a11y runs use).
    await page.emulateMedia({ reducedMotion: "reduce" });
    await setupSession(context);
    requestLog.length = 0;

    // Postings: sort, filter, page, then into a posting detail (which loads its criteria picker).
    await open(page, "/postings", "Job Postings");
    await sortBy(page, "Score");
    await open(page, "/postings?q=engineer&disposition=lead", "Job Postings");
    await page.getByRole("link", { name: "Engineer 1", exact: true }).first().click();
    await expect(page).toHaveURL(/\/postings\/post-1$/);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Engineer 1");
    await open(page, "/postings", "Job Postings");
    await nextPage(page);

    // Companies
    await open(page, "/companies", "Companies");
    await sortBy(page, "Status");
    await open(page, "/companies?q=company", "Companies");
    await nextPage(page);

    // Applications
    await open(page, "/applications", "Applications");
    await sortBy(page, "Status");
    await open(page, "/applications?status=applied&q=engineer", "Applications");
    await nextPage(page);

    // Notes
    await open(page, "/notes", "Notes");
    await sortBy(page, "Heading");
    await open(page, "/notes?q=note", "Notes");
    await nextPage(page);

    // Criteria
    await open(page, "/criteria", "Screening Criteria");
    await sortBy(page, "Rule Name");
    await open(page, "/criteria?effectiveDate=2026-10-01", "Screening Criteria");
    await nextPage(page);

    // Search runs (no filter control exists; the page has a sort and paging)
    await open(page, "/search-runs", "Search Runs");
    await sortBy(page, "New Postings");
    await nextPage(page);

    // Sanity: the OData sets were really used by every list page, and the detail GET was allowed through.
    const hit = (p: string) => requestLog.some((r) => r.pathname === p);
    for (const set of ["Postings", "Companies", "Applications", "Notes", "Criteria", "SearchRuns"]) {
      expect(hit(`/odata/${set}`), `/odata/${set} should have been requested`).toBe(true);
    }
    expect(hit("/api/v1/postings/post-1")).toBe(true);

    expect(legacyListRequests(requestLog)).toEqual([]);
  });
});
