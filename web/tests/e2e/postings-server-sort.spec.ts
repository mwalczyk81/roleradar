import { test, expect } from "./support/csp-guard.js";
import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import net from "node:net";
import type https from "node:https";
import http from "node:http";
import selfsigned from "selfsigned";
import { createWebServer } from "../../server.js";
import { saveSession } from "../../app/lib/session.server.js";
import * as client from "openid-client";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";

let server: https.Server | http.Server;
let apiServer: http.Server;
let baseUrl: string;
let tempTlsDir: string;
const E2E_SESSION_ID = "e2e-odata-postings-session-playwright";

test.describe("Postings Multi-Page Server Sorting & Pagination E2E (T033)", () => {
  test.beforeAll(async () => {
    tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-postings-sort-"));
    const certPath = path.join(tempTlsDir, "test.crt");
    const keyPath = path.join(tempTlsDir, "test.key");

    const pems = await selfsigned.generate(
      [{ name: "commonName", value: "127.0.0.1" }],
      { notAfterDate: new Date(Date.now() + 86400_000) }
    );
    fs.writeFileSync(certPath, pems.cert, "utf8");
    fs.writeFileSync(keyPath, pems.private, "utf8");

    process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
    process.env.ROLERADAR_WEB_CLIENT_SECRET = "test-secret";
    process.env.HTTPS_CERT_PATH = certPath;
    process.env.HTTPS_CERT_KEY_PATH = keyPath;
    process.env.COOKIE_SECURE = "true";

    // Mock OIDC discovery configuration
    const dummyOidcConfig = new client.Configuration(
      {
        issuer: "https://identity:5001",
        authorization_endpoint: "https://identity:5001/connect/authorize",
        token_endpoint: "https://identity:5001/connect/token",
        end_session_endpoint: "https://identity:5001/connect/endsession",
      },
      "roleradar-web",
      "test-secret"
    );
    setOidcConfigForTesting(dummyOidcConfig);

    saveSession({
      sessionId: E2E_SESSION_ID,
      ownerSub: "matt-owner-e2e",
      ownerUsername: "matt-e2e",
      accessToken: "mock-e2e-access-token",
      refreshToken: "mock-e2e-refresh-token",
      idToken: "mock-e2e-id-token",
      expiresAt: Date.now() + 3_600_000,
      createdAt: Date.now(),
      csrfToken: "mock-e2e-csrf-token",
    });

    // Start mock API server serving /odata/Postings
    const apiPort = await new Promise<number>((resolve) => {
      const s = net.createServer();
      s.listen(0, "127.0.0.1", () => {
        const port = (s.address() as net.AddressInfo).port;
        s.close(() => resolve(port));
      });
    });

    apiServer = http.createServer((req, res) => {
      const reqUrl = new URL(req.url ?? "", `http://127.0.0.1:${apiPort}`);
      if (reqUrl.pathname === "/odata/Postings") {
        const top = Number(reqUrl.searchParams.get("$top") || "25");
        const skip = Number(reqUrl.searchParams.get("$skip") || "0");
        const orderby = reqUrl.searchParams.get("$orderby") || "id asc";

        // Generate 60 test postings to test multi-page sorting
        const total = 60;
        const disps = ["lead", "scored", "applied", "rejected", "screened_out", "closed"];
        const allPostings = Array.from({ length: total }, (_, i) => {
          const score = total - i; // 60 down to 1
          const disp = disps[i % disps.length];
          return {
            id: `post-${String(i + 1).padStart(3, "0")}`,
            companyId: `comp-${i + 1}`,
            companyName: `Company ${(i % 5) + 1}`,
            title: `Software Engineer ${score}`,
            requisitionId: `REQ-${i + 1}`,
            location: "Remote",
            disposition: disp,
            resurfacePolicy: "never",
            score: score,
            createdAt: new Date(Date.now() - i * 60000).toISOString(),
            updatedAt: new Date(Date.now() - i * 60000).toISOString(),
          };
        });

        const rankMap: Record<string, number> = {
          unscored: 0,
          lead: 1,
          scored: 2,
          applied: 3,
          rejected: 4,
          screened_out: 5,
          closed: 6,
        };

        if (orderby.includes("score asc")) {
          allPostings.sort((a, b) => a.score - b.score);
        } else if (orderby.includes("score desc")) {
          allPostings.sort((a, b) => b.score - a.score);
        } else if (orderby.includes("disposition asc")) {
          allPostings.sort((a, b) => (rankMap[a.disposition] ?? 0) - (rankMap[b.disposition] ?? 0));
        } else if (orderby.includes("disposition desc")) {
          allPostings.sort((a, b) => (rankMap[b.disposition] ?? 0) - (rankMap[a.disposition] ?? 0));
        } else {
          allPostings.sort((a, b) => a.id.localeCompare(b.id));
        }

        const pageItems = allPostings.slice(skip, skip + top);

        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(
          JSON.stringify({
            value: pageItems,
            "@odata.count": total,
          })
        );
        return;
      }

      res.writeHead(404);
      res.end();
    });

    await new Promise<void>((resolve) => apiServer.listen(apiPort, "127.0.0.1", resolve));

    process.env.NODE_ENV = "test";
    process.env.PORT = "0";
    process.env.HTTPS_CERT_PATH = certPath;
    process.env.HTTPS_CERT_KEY_PATH = keyPath;
    process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
    process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${apiPort}`;
    process.env.ROLERADAR_WEB_CLIENT_SECRET = "test-secret";

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

  test("proves server-side sorting works across pages and url parameters update", async ({ page, context }) => {
    saveSession({
      sessionId: E2E_SESSION_ID,
      ownerSub: "matt-owner-e2e",
      ownerUsername: "matt-e2e",
      accessToken: "mock-e2e-access-token",
      refreshToken: "mock-e2e-refresh-token",
      idToken: "mock-e2e-id-token",
      expiresAt: Date.now() + 3_600_000,
      createdAt: Date.now(),
      csrfToken: "mock-e2e-csrf-token",
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

    // 1. Navigate to /postings
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    await expect(page.locator("h1")).toContainText("Job Postings");

    // Default sort is score desc, first item should have score 60
    const firstScore = page.locator("tbody tr:first-child td:nth-child(5)");
    await expect(firstScore).toHaveText("60");

    // 2. Click Score header to toggle sort to score asc
    const scoreHeader = page.locator("th button:has-text('Score')");
    await scoreHeader.click();

    // URL should now reflect sort=score&direction=asc
    await expect(page).toHaveURL(/sort=score/);
    await expect(page).toHaveURL(/direction=asc/);

    // First item on Page 1 (asc) should now be 1 (lowest score across entire 60 items)
    await expect(firstScore).toHaveText("1");

    // 3. Navigate to Page 2
    const nextButton = page.locator("button[aria-label='Next page'], button:has-text('Next')");
    if (await nextButton.isVisible()) {
      await nextButton.click();
      await expect(page).toHaveURL(/offset=25/);
      // First item on Page 2 (with top=25) should be score 26
      await expect(firstScore).toHaveText("26");
    }

    // 4. Click Disposition header to test pipeline rank ordering
    const dispHeader = page.locator("th button:has-text('Disposition')");
    await dispHeader.click();

    // URL should reflect sort=disposition&direction=asc
    await expect(page).toHaveURL(/sort=disposition/);
    await expect(page).toHaveURL(/direction=asc/);

    // Verify pipeline rank order: Lead (rank 1) comes before Scored (rank 2) comes before Applied (rank 3)
    // If it were alphabetical, Applied would come first
    const allBadges = await page.locator("tbody [data-testid='disposition-badge']").allTextContents();
    const firstLead = allBadges.indexOf("Lead");
    const firstScored = allBadges.indexOf("Scored");
    const firstApplied = allBadges.indexOf("Applied");

    expect(firstLead).toBeGreaterThanOrEqual(0);
    expect(firstScored).toBeGreaterThan(firstLead);
    expect(firstApplied).toBeGreaterThan(firstScored);
  });
});
