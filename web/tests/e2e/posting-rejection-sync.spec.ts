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
import { formatDate } from "../../app/lib/format-date.js";
import { checkAxeInThemes } from "./support/axe.js";
import { assertLoopbackStub, assertNotRealStackProject } from "./support/isolation.js";

/**
 * Feature 008 phase 4: posting transition mutation in real Chromium. The web server talks to a loopback stub API
 * that serves fictional data only. It never reaches the developer's stack or a real database.
 */

const SESSION_ID = "e2e-rejection-sync-session";
const POSTING_ID = "post-rr-1";
const APPLICATION_ID = "app-rr-1";
const COMPANY_ID = "comp-rr-1";
const POSTING_URL_PATH = `/postings/${POSTING_ID}`;
const STILL_REJECTED_MESSAGE = "Fictional stub: the application stays rejected after this reopen.";

let server: https.Server | http.Server;
let apiServer: http.Server;
let baseUrl: string;
let tempTlsDir: string;

interface ApplicationEventState {
  eventType: string;
  eventDate: string;
  notes: string | null;
  eventSource: string | null;
}

let disposition = "applied";
let versionToken = "ver-1";
let hasApplication = true;
let applicationStatus = "applied";
let applicationEvents: ApplicationEventState[] = [];
let transitionBodies: Array<Record<string, unknown>> = [];
let nextTransitionStatus: number | null = null;
let nextTransitionBody: unknown = null;

function resetState(overrides: { disposition?: string; applicationStatus?: string } = {}) {
  disposition = overrides.disposition ?? "applied";
  applicationStatus = overrides.applicationStatus ?? (disposition === "rejected" ? "rejected" : "applied");
  versionToken = "ver-1";
  hasApplication = true;
  applicationEvents = [
    { eventType: "applied", eventDate: "2026-09-01", notes: "Submitted online", eventSource: "web" },
  ];
  if (applicationStatus === "rejected") {
    applicationEvents.push({
      eventType: "rejected",
      eventDate: "2026-09-10",
      notes: "Rejected by email",
      eventSource: "posting_transition",
    });
  }
  transitionBodies = [];
  nextTransitionStatus = null;
  nextTransitionBody = null;
}

function postingBody(warnings: Array<{ code: string; message: string }> | null) {
  return {
    id: POSTING_ID,
    companyId: COMPANY_ID,
    companyName: "Harbor Analytics",
    title: "Staff Platform Engineer",
    requisitionId: "HAR-1001",
    location: "Remote",
    disposition,
    resurfacePolicy: "never",
    score: 88.5,
    versionToken,
    createdAt: "2026-08-20T10:00:00Z",
    updatedAt: "2026-09-01T11:00:00Z",
    url: "https://example.com/jobs/har-1001",
    compensation: { currency: "USD", rawText: "$190,000 - $230,000", hasSalary: true },
    signals: [],
    assessmentNote: null,
    warnings,
    applicationId: hasApplication ? APPLICATION_ID : null,
  };
}

function todayUtc(): string {
  return formatDate(new Date().toISOString());
}

function handleTransition(payload: Record<string, unknown>, res: http.ServerResponse) {
  const target = String(payload.toDisposition ?? "");
  const eventDate = typeof payload.eventDate === "string" && payload.eventDate !== "" ? payload.eventDate : null;

  if (payload.versionToken !== versionToken) {
    res.writeHead(409);
    res.end(JSON.stringify({ status: 409, errorCode: "concurrency_conflict", detail: "Stale version token." }));
    return;
  }

  if (eventDate !== null && !/^\d{4}-\d{2}-\d{2}$/.test(eventDate)) {
    res.writeHead(400);
    res.end(
      JSON.stringify({
        status: 400,
        errorCode: "validation_error",
        detail: "Validation failed.",
        errors: { eventDate: ["Event date must be a valid date in yyyy-MM-dd format."] },
      })
    );
    return;
  }

  const previous = disposition;
  let warnings: Array<{ code: string; message: string }> = [];

  if (previous === "applied" && target === "rejected" && hasApplication && applicationStatus !== "rejected") {
    applicationStatus = "rejected";
    applicationEvents.push({
      eventType: "rejected",
      eventDate: eventDate ?? todayUtc(),
      notes: String(payload.reason ?? ""),
      eventSource: "posting_transition",
    });
  }

  if (previous === "rejected" && target === "lead" && hasApplication && applicationStatus === "rejected") {
    warnings = [{ code: "application_still_rejected", message: STILL_REJECTED_MESSAGE }];
  }

  disposition = target;
  versionToken = `ver-${Number(versionToken.slice(4)) + 1}`;
  res.writeHead(200);
  res.end(JSON.stringify(postingBody(warnings)));
}

test.beforeAll(async () => {
  assertNotRealStackProject(test.info().project.name);
  const serverBuild = path.resolve(process.cwd(), "build", "server", "index.js");
  if (!fs.existsSync(serverBuild)) {
    throw new Error(`Missing ${serverBuild}. Run "pnpm --filter web run build" before the e2e suite.`);
  }

  setOidcConfigForTesting(
    new client.Configuration(
      {
        issuer: "https://identity:5001",
        authorization_endpoint: "https://identity:5001/connect/authorize",
        token_endpoint: "https://identity:5001/connect/token",
        end_session_endpoint: "https://identity:5001/connect/endsession",
      },
      "roleradar-web",
      "playwright-test-client-secret"
    )
  );

  tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-rejection-sync-"));
  const certPath = path.join(tempTlsDir, "throwaway-cert.crt");
  const keyPath = path.join(tempTlsDir, "throwaway-key.key");
  const pems = await selfsigned.generate([{ name: "commonName", value: "127.0.0.1" }], {
    notAfterDate: new Date(Date.now() + 86_400_000),
  });
  fs.writeFileSync(certPath, pems.cert, "utf8");
  fs.writeFileSync(keyPath, pems.private, "utf8");

  process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
  process.env.ROLERADAR_WEB_CLIENT_SECRET = "playwright-test-client-secret";
  process.env.HTTPS_CERT_PATH = certPath;
  process.env.HTTPS_CERT_KEY_PATH = keyPath;
  process.env.COOKIE_SECURE = "true";

  apiServer = http.createServer((req, res) => {
    const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
    res.setHeader("Content-Type", "application/json");

    if (url.pathname === "/odata/Criteria" && req.method === "GET") {
      res.writeHead(200);
      res.end(JSON.stringify({ value: [], "@odata.count": 0 }));
      return;
    }

    if (url.pathname === `/api/v1/postings/${POSTING_ID}` && req.method === "GET") {
      res.writeHead(200);
      res.end(JSON.stringify(postingBody(null)));
      return;
    }

    if (url.pathname === `/api/v1/postings/${POSTING_ID}/transition` && req.method === "POST") {
      let bodyStr = "";
      req.on("data", (chunk) => {
        bodyStr += chunk;
      });
      req.on("end", () => {
        const payload = JSON.parse(bodyStr || "{}") as Record<string, unknown>;
        transitionBodies.push(payload);
        if (nextTransitionStatus !== null) {
          const status = nextTransitionStatus;
          const body = nextTransitionBody;
          nextTransitionStatus = null;
          nextTransitionBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }
        handleTransition(payload, res);
      });
      return;
    }

    if (url.pathname === `/api/v1/applications/${APPLICATION_ID}` && req.method === "GET") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: APPLICATION_ID,
          postingId: POSTING_ID,
          companyId: COMPANY_ID,
          status: applicationStatus,
          appliedAt: "2026-09-01",
          events: applicationEvents,
        })
      );
      return;
    }

    if (url.pathname === `/api/v1/companies/${COMPANY_ID}` && req.method === "GET") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: COMPANY_ID,
          name: "Harbor Analytics",
          status: "target",
          ownershipType: "private",
          description: "Fictional analytics company",
          headquarters: "Austin, TX",
          categoryTags: [],
          careerSources: [],
          versionToken: "comp-ver-1",
        })
      );
      return;
    }

    res.writeHead(404);
    res.end(JSON.stringify({ status: 404, errorCode: "not_found", detail: "Not found in the fictional stub." }));
  });

  await new Promise<void>((resolve) => {
    apiServer.listen(0, "127.0.0.1", () => {
      const addr = apiServer.address() as net.AddressInfo;
      process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${addr.port}`;
      assertLoopbackStub(process.env.ROLERADAR_API_BASE_URL, addr.port);
      resolve();
    });
  });

  saveSession({
    sessionId: SESSION_ID,
    ownerSub: "fictional-owner-e2e",
    accessToken: "stubbed-access-token",
    refreshToken: "stubbed-refresh-token",
    expiresAt: Date.now() + 3_600_000,
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
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
  if (apiServer) {
    await new Promise<void>((resolve) => apiServer.close(() => resolve()));
  }
  if (tempTlsDir && fs.existsSync(tempTlsDir)) {
    fs.rmSync(tempTlsDir, { recursive: true, force: true });
  }
});

async function signIn(context: BrowserContext) {
  saveSession({
    sessionId: SESSION_ID,
    ownerSub: "fictional-owner-e2e",
    accessToken: "stubbed-access-token",
    refreshToken: "stubbed-refresh-token",
    expiresAt: Date.now() + 3_600_000,
    createdAt: Date.now(),
    csrfToken: "playwright-csrf-token",
  });
  await context.addCookies([
    { name: "__Host-roleradar_session", value: SESSION_ID, url: baseUrl, httpOnly: true, secure: true, sameSite: "Lax" },
  ]);
}

async function openPosting(page: Page) {
  await page.goto(`${baseUrl}${POSTING_URL_PATH}`, { waitUntil: "networkidle" });
  await expect(page.getByRole("heading", { name: "Staff Platform Engineer", level: 1 })).toBeVisible();
}

async function openDialogAndChooseRejected(page: Page) {
  await page.getByRole("button", { name: "Change Disposition" }).click();
  const modal = page.getByRole("dialog");
  await expect(modal).toBeVisible();
  await modal.getByLabel("Target Disposition *").selectOption("rejected");
  return modal;
}

const NOTICE = "This also records the rejection on the application.";

test.describe("Posting rejection sync (real Chromium, fictional stub API)", () => {
  test.beforeEach(async ({ context }) => {
    resetState();
    await signIn(context);
  });

  test("success: states the application effect, posts eventDate, closes, and the application shows the posting_transition source", async ({
    page,
  }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);

    await expect(modal.getByText(NOTICE)).toBeVisible();
    const dateInput = modal.getByLabel("Event Date (Optional)");
    await expect(dateInput).toHaveValue(todayUtc());
    await dateInput.fill("2026-09-15");
    await modal.getByLabel("Reason *").fill("Rejected by email");
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    await expect(modal).not.toBeVisible();
    expect(page.url()).toBe(`${baseUrl}${POSTING_URL_PATH}`);
    await expect(page.getByTestId("disposition-badge")).toHaveText("Rejected");
    await expect(page.getByRole("alert")).toHaveCount(0);
    expect(transitionBodies).toHaveLength(1);
    expect(transitionBodies[0].eventDate).toBe("2026-09-15");

    await page.goto(`${baseUrl}/applications/${APPLICATION_ID}`, { waitUntil: "networkidle" });
    const events = page.getByTestId("timeline-event");
    const rejected = events.filter({ hasText: "Rejected by email" }).last();
    await expect(rejected).toContainText("Rejected");
    await expect(rejected).toContainText("2026-09-15");
    await expect(rejected.getByTestId("event-source")).toHaveText("Source: posting_transition");
    await expect(events.filter({ hasText: "Submitted online" }).getByTestId("event-source")).toHaveText("Source: web");
  });

  test("a posting with no application shows no notice and no date field", async ({ page }) => {
    hasApplication = false;
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);

    await expect(modal.getByText(NOTICE)).toHaveCount(0);
    await expect(modal.getByLabel("Event Date (Optional)")).toHaveCount(0);
  });

  test("400 on a bad eventDate: inline error on the date field, dialog stays open, nothing changes", async ({ page }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);

    const dateInput = modal.getByLabel("Event Date (Optional)");
    await dateInput.fill("09/15/2026");
    await modal.getByLabel("Reason *").fill("Rejected by email");
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    await expect(modal).toBeVisible();
    await expect(modal.getByText("Event date must be a valid date in yyyy-MM-dd format.")).toBeVisible();
    await expect(dateInput).toHaveAttribute("aria-invalid", "true");
    await expect(page.getByTestId("disposition-badge")).toHaveText("Applied");
    expect(page.url()).toBe(`${baseUrl}${POSTING_URL_PATH}`);
  });

  test("409 stale version: Toast with Reload, dialog does not close", async ({ page }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);

    // Another process moves the posting on, so the version token in the open dialog is stale.
    versionToken = "ver-9";
    await modal.getByLabel("Reason *").fill("Rejected by email");
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    const toast = page.locator('[role="alert"], [role="status"]').filter({ hasText: "Concurrency Conflict" });
    await expect(toast).toBeVisible();
    await toast.getByRole("button", { name: "Reload" }).click();
    await expect(toast).not.toBeVisible();
    await expect(page.getByTestId("disposition-badge")).toHaveText("Applied");
  });

  test("bad CSRF: inline error, the API is never called", async ({ page }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);

    await modal.getByLabel("Reason *").fill("Rejected by email");
    await page.evaluate(() => {
      const input = document.querySelector('[role="dialog"] input[name="_csrf"]') as HTMLInputElement | null;
      if (input) input.value = "tampered-invalid-csrf-token";
    });
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    await expect(modal).toBeVisible();
    await expect(modal.getByRole("alert")).toContainText("Invalid or missing CSRF token");
    expect(transitionBodies).toHaveLength(0);
  });

  test("401 from the API: one redirect into the authorization flow, no loop, session cleared", async ({ page, context }) => {
    await openPosting(page);

    nextTransitionStatus = 401;
    nextTransitionBody = { status: 401, errorCode: "unauthorized" };

    let authRedirectCount = 0;
    const authRequest = page.waitForRequest((req) => {
      const u = req.url();
      if (u.includes("identity:5001") || u.includes("/connect/authorize") || u.includes("/auth")) {
        authRedirectCount++;
        return true;
      }
      return false;
    });
    await page.route("**/connect/authorize*", (route) =>
      route.fulfill({ status: 200, contentType: "text/html", body: "<html><body>Fictional login page</body></html>" })
    );

    const modal = await openDialogAndChooseRejected(page);
    await modal.getByLabel("Reason *").fill("Rejected by email");
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    const request = await authRequest;
    expect(request.url()).toContain("connect/authorize");
    await page.waitForTimeout(500);
    expect(authRedirectCount).toBe(1);

    const cookies = await context.cookies();
    const session = cookies.find((c) => c.name === "__Host-roleradar_session");
    expect(!session || session.value === "").toBe(true);
  });

  test("reopening a rejected posting whose application is rejected shows the still-rejected warning", async ({ page }) => {
    resetState({ disposition: "rejected", applicationStatus: "rejected" });
    await openPosting(page);

    await page.getByRole("button", { name: "Change Disposition" }).click();
    const modal = page.getByRole("dialog");
    await expect(modal).toBeVisible();
    await expect(modal.getByText(NOTICE)).toHaveCount(0);
    await modal.getByLabel("Target Disposition *").selectOption("lead");
    await modal.getByLabel("Reason *").fill("Recruiter reopened the role");
    await modal.getByLabel("Override Reason (Optional)").fill("Recruiter reopened the role");
    await modal.getByRole("button", { name: "Apply Transition" }).click();

    await expect(modal).not.toBeVisible();
    const warning = page.getByRole("alert").filter({ hasText: STILL_REJECTED_MESSAGE });
    await expect(warning).toBeVisible();
    await expect(page.getByTestId("disposition-badge")).toHaveText("Lead");

    // The warning is rendered from the response; run the shared reduced-motion axe check on the page that shows it.
    await checkAxeInThemes(page);
  });

  test("the dialog stays open when reopened after a successful submit", async ({ page }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);
    await modal.getByLabel("Reason *").fill("Rejected by email");
    await modal.getByRole("button", { name: "Apply Transition" }).click();
    await expect(modal).not.toBeVisible();
    await expect(page.getByTestId("disposition-badge")).toHaveText("Rejected");

    await page.getByRole("button", { name: "Change Disposition" }).click();
    const reopened = page.getByRole("dialog");
    await expect(reopened).toBeVisible();
    // A stale success in the fetcher must not close it again: give any wrongly-fired close time to land.
    await page.waitForTimeout(750);
    await expect(reopened).toBeVisible();
    await expect(reopened.getByLabel("Target Disposition *")).toBeVisible();
  });

  test("axe: the dialog with the application notice, light and dark", async ({ page }) => {
    await openPosting(page);
    const modal = await openDialogAndChooseRejected(page);
    await expect(modal.getByText(NOTICE)).toBeVisible();
    await expect(modal.getByLabel("Event Date (Optional)")).toBeVisible();

    await checkAxeInThemes(page);
  });
});
