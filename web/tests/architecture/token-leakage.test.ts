import { describe, it, expect, beforeAll, afterAll, vi } from "vitest";
import http from "node:http";
import https from "node:https";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import net from "node:net";
import selfsigned from "selfsigned";
import { chromium, type Browser, type BrowserContext } from "@playwright/test";
import * as client from "openid-client";
import { createWebServer } from "../../server.js";
import {
  saveSession,
  createSessionCookie,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
} from "../../app/lib/session.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";

// Loaders & Actions for static route body inspection
import { loader as notAuthorizedLoader } from "../../app/routes/not-authorized.js";
import { loader as postingsLoader } from "../../app/routes/postings._index.js";
import { loader as postingDetailLoader } from "../../app/routes/postings.$id.js";
import { loader as companiesLoader } from "../../app/routes/companies._index.js";
import { loader as companyDetailLoader } from "../../app/routes/companies.$id.js";
import { loader as applicationsLoader } from "../../app/routes/applications._index.js";
import { loader as criteriaLoader } from "../../app/routes/criteria.js";
import { loader as notesLoader } from "../../app/routes/notes._index.js";
import { loader as noteDetailLoader } from "../../app/routes/notes.$id.js";
import { loader as searchRunsLoader } from "../../app/routes/search-runs.js";
import { callLoader } from "../test-helpers.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";

describe("Architecture Verification: Token Leakage Assertion (FR-028, SC-005)", () => {
  const CANARY_ACCESS = "CANARY_ACCESS_TOKEN_ZX987654321_SECRET";
  const CANARY_REFRESH = "CANARY_REFRESH_TOKEN_QW123456789_SECRET";
  const CANARY_ID = "CANARY_ID_TOKEN_PL555666777_SECRET";
  const CANARY_SESSION_ID = "canary-session-uuid-000000000001";
  const CANARY_CSRF = "canary-csrf-token-12345";

  const allCanaries = [CANARY_ACCESS, CANARY_REFRESH, CANARY_ID];

  describe("Server Route Bodies, Errors, and Set-Cookie Inspection", () => {
    beforeAll(() => {
      const dummyKey = Buffer.alloc(32, 7);
      setSessionEncryptionKeyForTesting(dummyKey);

      saveSession({
        sessionId: CANARY_SESSION_ID,
        ownerSub: "canary-owner-sub",
        accessToken: CANARY_ACCESS,
        refreshToken: CANARY_REFRESH,
        idToken: CANARY_ID,
        expiresAt: Date.now() + 3_600_000,
        createdAt: Date.now(),
        csrfToken: CANARY_CSRF,
      });
    });

    it("verifies zero tokens appear in serialized return values of all route loaders", async () => {
      const createApiSpy = vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
        GET: vi.fn().mockImplementation((pathStr: string) => {
          if (pathStr === "/api/v1/postings") {
            return Promise.resolve({
              data: { items: [], totalCount: 0, limit: 25, offset: 0 },
              response: { status: 200 } as Response,
            });
          }
          if (pathStr === "/api/v1/postings/{id}") {
            return Promise.resolve({
              data: {
                id: "p1",
                companyId: "c1",
                companyName: "Acme",
                title: "Engineer",
                requisitionId: null,
                location: "Remote",
                disposition: "lead",
                resurfacePolicy: "never",
                score: null,
                versionToken: "v1",
                createdAt: new Date().toISOString(),
                updatedAt: new Date().toISOString(),
                url: null,
                compensation: { currency: "USD", rawText: null },
                signals: [],
                assessmentNote: null,
              },
              response: { status: 200 } as Response,
            });
          }
          if (pathStr === "/api/v1/companies/{id}") {
            return Promise.resolve({
              data: {
                id: "c1",
                name: "Acme",
                status: "target",
                ownershipType: "private",
                description: null,
                headquarters: null,
                categoryTags: [],
                careerSources: [],
                versionToken: "cv1",
              },
              response: { status: 200 } as Response,
            });
          }
          if (pathStr === "/api/v1/applications") {
            return Promise.resolve({
              data: { items: [], totalCount: 0, limit: 25, offset: 0 },
              response: { status: 200 } as Response,
            });
          }
          if (pathStr === "/api/v1/criteria") {
            return Promise.resolve({
              data: [],
              response: { status: 200 } as Response,
            });
          }
          if (pathStr === "/api/v1/notes/{id}") {
            return Promise.resolve({
              data: { id: "n1", noteText: "test note", createdAt: new Date().toISOString() },
              response: { status: 200 } as Response,
            });
          }
          return Promise.resolve({ data: undefined, response: { status: 404 } as Response });
        }),
      } as unknown as ReturnType<typeof apiClientModule.createApiClient>);

      vi.spyOn(apiClientModule, "fetchOData").mockResolvedValue({
        data: { value: [], "@odata.count": 0 },
        response: { status: 200 } as Response,
      });

      const headers = { Cookie: `${SECURE_COOKIE_NAME}=${CANARY_SESSION_ID}` };
      const loadersToTest: Array<{ name: string; invoke: () => Promise<unknown> }> = [
        {
          name: "/not-authorized",
          invoke: () =>
            callLoader(notAuthorizedLoader, new Request("https://localhost:5003/not-authorized", { headers })),
        },
        {
          name: "/postings",
          invoke: () =>
            callLoader(postingsLoader, new Request("https://localhost:5003/postings", { headers })),
        },
        {
          name: "/postings/:id",
          invoke: () =>
            callLoader(postingDetailLoader, new Request("https://localhost:5003/postings/p1", { headers }), { id: "p1" }),
        },
        {
          name: "/companies",
          invoke: () =>
            callLoader(companiesLoader, new Request("https://localhost:5003/companies", { headers })),
        },
        {
          name: "/companies/:id",
          invoke: () =>
            callLoader(companyDetailLoader, new Request("https://localhost:5003/companies/c1", { headers }), { id: "c1" }),
        },
        {
          name: "/applications",
          invoke: () =>
            callLoader(applicationsLoader, new Request("https://localhost:5003/applications", { headers })),
        },
        {
          name: "/criteria",
          invoke: () =>
            callLoader(criteriaLoader, new Request("https://localhost:5003/criteria", { headers })),
        },
        {
          name: "/notes",
          invoke: () =>
            callLoader(notesLoader, new Request("https://localhost:5003/notes", { headers })),
        },
        {
          name: "/notes/:id",
          invoke: () =>
            callLoader(noteDetailLoader, new Request("https://localhost:5003/notes/n1", { headers }), { id: "n1" }),
        },
        {
          name: "/search-runs",
          invoke: () =>
            callLoader(searchRunsLoader, new Request("https://localhost:5003/search-runs", { headers })),
        },
      ];

      for (const { name, invoke } of loadersToTest) {
        const result = await invoke();
        const serialized = JSON.stringify(result);
        for (const canary of allCanaries) {
          expect(
            serialized,
            `Loader ${name} leaked token ${canary} in return data`
          ).not.toContain(canary);
        }
      }

      createApiSpy.mockRestore();
    });

    it("verifies Set-Cookie header contains only opaque IDs and never raw tokens", () => {
      const cookie = createSessionCookie(CANARY_SESSION_ID, true);
      expect(cookie.header).toContain(CANARY_SESSION_ID);
      for (const canary of allCanaries) {
        expect(cookie.header).not.toContain(canary);
      }
    });
  });

  describe("Real Chromium Browser Storage & Response Body Inspection", () => {
    let apiServer: http.Server;
    let webServer: https.Server | http.Server;
    let baseUrl: string;
    let tempTlsDir: string;
    let browser: Browser;
    let context: BrowserContext;

    beforeAll(async () => {
      const mockOidcConfig = new client.Configuration(
        {
          issuer: "https://identity:5001",
          authorization_endpoint: "https://identity:5001/connect/authorize",
          token_endpoint: "https://identity:5001/connect/token",
          end_session_endpoint: "https://identity:5001/connect/endsession",
        },
        "roleradar-web",
        "canary-client-secret"
      );
      setOidcConfigForTesting(mockOidcConfig);

      tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-token-leakage-tls-"));
      const certPath = path.join(tempTlsDir, "throwaway-cert.crt");
      const keyPath = path.join(tempTlsDir, "throwaway-key.key");

      const pems = await selfsigned.generate(
        [{ name: "commonName", value: "127.0.0.1" }],
        { notAfterDate: new Date(Date.now() + 86400_000) }
      );
      fs.writeFileSync(certPath, pems.cert, "utf8");
      fs.writeFileSync(keyPath, pems.private, "utf8");

      process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 9).toString("base64");
      // The first describe cached a different key in this module. The built server reads the env key, so
      // reset the cached key to match; otherwise the server cannot decrypt the session saved below.
      setSessionEncryptionKeyForTesting(Buffer.alloc(32, 9));
      process.env.ROLERADAR_WEB_CLIENT_SECRET = "canary-client-secret";
      process.env.HTTPS_CERT_PATH = certPath;
      process.env.HTTPS_CERT_KEY_PATH = keyPath;
      process.env.COOKIE_SECURE = "true";

      // Stub API server on loopback
      apiServer = http.createServer((req, res) => {
        const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
        res.setHeader("Content-Type", "application/json");

        if (url.pathname === "/api/v1/postings") {
          res.writeHead(200);
          res.end(
            JSON.stringify({
              items: [
                {
                  id: "post-leak-1",
                  companyId: "comp-1",
                  companyName: "Safe Corp",
                  title: "Security Engineer",
                  requisitionId: "REQ-LEAK",
                  location: "Remote",
                  disposition: "lead",
                  resurfacePolicy: "never",
                  score: 95.0,
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

        if (url.pathname === "/api/v1/postings/post-leak-1") {
          res.writeHead(200);
          res.end(
            JSON.stringify({
              id: "post-leak-1",
              companyId: "comp-1",
              companyName: "Safe Corp",
              title: "Security Engineer",
              requisitionId: "REQ-LEAK",
              location: "Remote",
              disposition: "lead",
              resurfacePolicy: "never",
              score: 95.0,
              versionToken: "ver-1",
              createdAt: "2026-10-31T10:00:00Z",
              updatedAt: "2026-10-31T11:00:00Z",
              url: "https://example.com/jobs/leak",
              compensation: { currency: "USD", rawText: "$150,000", hasSalary: true },
              signals: [],
              assessmentNote: null,
            })
          );
          return;
        }

        if (url.pathname === "/odata/Companies") {
          res.writeHead(200);
          res.end(
            JSON.stringify({
              "@odata.count": 1,
              value: [
                {
                  id: "comp-1",
                  name: "Safe Corp",
                  status: "target",
                  ownershipType: "private",
                  headquarters: "Austin, TX",
                  description: null,
                  categoryTags: [],
                },
              ],
            })
          );
          return;
        }

        if (url.pathname === "/api/v1/companies/comp-1") {
          res.writeHead(200);
          res.end(
            JSON.stringify({
              id: "comp-1",
              name: "Safe Corp",
              status: "target",
              ownershipType: "private",
              description: "Safe privacy-first systems",
              headquarters: "Austin, TX",
              categoryTags: ["security"],
              careerSources: [],
              versionToken: "comp-ver-1",
            })
          );
          return;
        }

        if (url.pathname === "/api/v1/applications") {
          res.writeHead(200);
          res.end(
            JSON.stringify({
              items: [],
              totalCount: 0,
              limit: 25,
              offset: 0,
            })
          );
          return;
        }

        if (url.pathname === "/api/v1/criteria") {
          res.writeHead(200);
          res.end(JSON.stringify([]));
          return;
        }

        if (url.pathname === "/odata/Criteria" || url.pathname === "/odata/Notes" || url.pathname === "/odata/SearchRuns") {
          res.writeHead(200);
          res.end(JSON.stringify({ "@odata.count": 0, value: [] }));
          return;
        }

        res.writeHead(404);
        res.end(JSON.stringify({ ok: false, errorCode: "NOT_FOUND" }));
      });

      await new Promise<void>((resolve) => {
        apiServer.listen(0, "127.0.0.1", () => {
          const addr = apiServer.address() as net.AddressInfo;
          process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${addr.port}`;
          resolve();
        });
      });

      // Save authenticated session containing secret canaries
      saveSession({
        sessionId: CANARY_SESSION_ID,
        ownerSub: "chromium-leak-user",
        accessToken: CANARY_ACCESS,
        refreshToken: CANARY_REFRESH,
        idToken: CANARY_ID,
        expiresAt: Date.now() + 3_600_000,
        createdAt: Date.now(),
        csrfToken: CANARY_CSRF,
      });

      webServer = createWebServer();
      await new Promise<void>((resolve) => {
        webServer.listen(0, "127.0.0.1", () => {
          const addr = webServer.address() as net.AddressInfo;
          baseUrl = `https://127.0.0.1:${addr.port}`;
          resolve();
        });
      });

      // Launch real Chromium browser
      browser = await chromium.launch({
        headless: true,
        args: [
          "--ignore-certificate-errors",
          "--host-resolver-rules=MAP identity 127.0.0.1, MAP api 127.0.0.1",
        ],
      });

      context = await browser.newContext({
        ignoreHTTPSErrors: true,
      });

      // Set session cookie in browser context. A __Host- cookie must be host-only (no Domain attribute):
      // Linux Chromium rejects it when a domain is passed, so address it by url like the e2e spec does.
      await context.addCookies([
        {
          name: SECURE_COOKIE_NAME,
          value: CANARY_SESSION_ID,
          url: baseUrl,
          httpOnly: true,
          secure: true,
          sameSite: "Lax",
        },
      ]);
    }, 45_000);

    afterAll(async () => {
      try {
        if (context) await context.close();
      } catch {
        // Ignored on cleanup
      }
      try {
        if (browser) await browser.close();
      } catch {
        // Ignored on cleanup
      }
      try {
        if (webServer) await new Promise<void>((res) => webServer.close(() => res()));
      } catch {
        // Ignored on cleanup
      }
      try {
        if (apiServer) await new Promise<void>((res) => apiServer.close(() => res()));
      } catch {
        // Ignored on cleanup
      }
      try {
        if (tempTlsDir && fs.existsSync(tempTlsDir)) {
          fs.rmSync(tempTlsDir, { recursive: true, force: true });
        }
      } catch {
        // Ignored on cleanup
      }
    });

    const routesToVisit = [
      "/postings",
      "/postings/post-leak-1",
      "/companies",
      "/companies/comp-1",
      "/applications",
      "/criteria",
      "/notes",
      "/search-runs",
      "/not-authorized",
      "/service-unavailable",
    ];

    for (const route of routesToVisit) {
      it(`asserts zero token leakage in DOM, body, headers, and storage on ${route}`, async () => {
        const page = await context.newPage();
        const interceptedBodies: string[] = [];
        const setCookieHeaders: string[] = [];

        page.on("response", async (res) => {
          try {
            const h = res.headers();
            if (h["set-cookie"]) {
              setCookieHeaders.push(h["set-cookie"]);
            }
            // Capture text responses
            const contentType = h["content-type"] || "";
            if (contentType.includes("text") || contentType.includes("json")) {
              const text = await res.text();
              interceptedBodies.push(text);
            }
          } catch {
            // Ignored on binary or aborted responses
          }
        });

        const targetUrl = `${baseUrl}${route}`;
        await page.goto(targetUrl, { waitUntil: "domcontentloaded" });

        // 1. Assert response bodies
        for (const body of interceptedBodies) {
          for (const canary of allCanaries) {
            expect(body, `Response body on ${route} leaked token ${canary}`).not.toContain(canary);
          }
        }

        // 2. Assert Set-Cookie headers
        for (const setCookie of setCookieHeaders) {
          for (const canary of allCanaries) {
            expect(setCookie, `Set-Cookie header on ${route} leaked token ${canary}`).not.toContain(
              canary
            );
          }
        }

        // 3. Inspect document DOM and text content
        const pageContent = await page.content();
        for (const canary of allCanaries) {
          expect(pageContent, `Page HTML content on ${route} leaked token ${canary}`).not.toContain(
            canary
          );
        }

        // 4. Inspect browser storage: localStorage, sessionStorage, and IndexedDB
        const storageInspection = await page.evaluate(async () => {
          const results: {
            localStorageEntries: Record<string, string>;
            sessionStorageEntries: Record<string, string>;
            indexedDbData: string[];
            cookieString: string;
          } = {
            localStorageEntries: {},
            sessionStorageEntries: {},
            indexedDbData: [],
            cookieString: document.cookie,
          };

          for (let i = 0; i < localStorage.length; i++) {
            const k = localStorage.key(i);
            if (k !== null) {
              results.localStorageEntries[k] = localStorage.getItem(k) ?? "";
            }
          }

          for (let i = 0; i < sessionStorage.length; i++) {
            const k = sessionStorage.key(i);
            if (k !== null) {
              results.sessionStorageEntries[k] = sessionStorage.getItem(k) ?? "";
            }
          }

          // Query IndexedDB databases if available
          if (typeof indexedDB !== "undefined" && typeof indexedDB.databases === "function") {
            try {
              const dbs = await indexedDB.databases();
              for (const dbInfo of dbs) {
                if (dbInfo.name) {
                  results.indexedDbData.push(dbInfo.name);
                }
              }
            } catch {
              // IndexedDB databases() enumeration not supported in all contexts
            }
          }

          return results;
        });

        // Assert cookie string in client JS (HttpOnly cookie must not even be visible)
        for (const canary of allCanaries) {
          expect(storageInspection.cookieString).not.toContain(canary);
        }

        // Assert localStorage
        for (const [key, val] of Object.entries(storageInspection.localStorageEntries)) {
          for (const canary of allCanaries) {
            expect(key).not.toContain(canary);
            expect(val).not.toContain(canary);
          }
        }

        // Assert sessionStorage
        for (const [key, val] of Object.entries(storageInspection.sessionStorageEntries)) {
          for (const canary of allCanaries) {
            expect(key).not.toContain(canary);
            expect(val).not.toContain(canary);
          }
        }

        // Assert IndexedDB
        for (const entry of storageInspection.indexedDbData) {
          for (const canary of allCanaries) {
            expect(entry).not.toContain(canary);
          }
        }

        await page.close();
      }, 30_000);
    }
  });
});
