import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import type net from "node:net";
import type https from "node:https";
import http from "node:http";
import selfsigned from "selfsigned";
import * as client from "openid-client";
import type { BrowserContext } from "@playwright/test";
import { createWebServer } from "../../../server.js";
import { saveSession } from "../../../app/lib/session.server.js";
import { setOidcConfigForTesting } from "../../../app/lib/oidc.server.js";

/**
 * Shared end-to-end harness (009 phase 1): the loopback stub RoleRadar API with fictional data, throwaway TLS, a seeded
 * session and the real web server. Moved out of csp-style-attribute.spec.ts without changing what the stub answers.
 * It never talks to a real API, Identity or database: the API base URL is a free loopback port on this machine.
 */

export interface CareerSourceStub {
  sourceType: string;
  atsProvider: string;
  atsTenant: string;
  sourceUrl: string;
  fetchMethod: string;
  active: boolean;
}

export interface ApplicationEventStub {
  eventType: string;
  eventDate: string;
  notes: string | null;
  /** 008: where the event came from ("web", "posting_transition"); the timeline shows it as a source line when set. */
  eventSource?: string | null;
}

/**
 * The mutable state the stub API reads and writes. A spec that keeps its own module-level variables hands the harness
 * an object of accessors over them; a spec that has none (the visual baseline) uses `defaultStubState()`.
 */
export interface StubState {
  currentPostingDisposition: string;
  currentPostingVersionToken: string;
  currentPostingTitle: string;
  /** Optional: the assessment note the posting detail returns. Absent means none. */
  currentPostingAssessmentNote?: { summary: string; updatedAt: string } | null;
  nextTransitionResponseStatus: number | null;
  nextTransitionResponseBody: unknown;
  currentCompanyName: string;
  currentCompanyStatus: string;
  currentCompanyOwnership: string;
  currentCompanyDescription: string;
  currentCompanyHeadquarters: string;
  currentCompanyCategoryTags: string[];
  currentCompanyCareerSources: CareerSourceStub[];
  currentCompanyVersionToken: string;
  nextCompanyResponseStatus: number | null;
  nextCompanyResponseBody: unknown;
  lastCompaniesQueryReceived: Record<string, string>;
  currentApplicationStatus: string;
  currentApplicationEvents: ApplicationEventStub[];
  nextApplicationResponseStatus: number | null;
  nextApplicationResponseBody: unknown;
  lastApplicationsQueryReceived: Record<string, string>;
  lastCriteriaQueryReceived: Record<string, string>;
  currentCriteriaList: Array<Record<string, unknown>> | null;
  lastNotesQueryReceived: Record<string, string>;
  currentNotesList: Array<Record<string, unknown>>;
  currentNoteDetail: Record<string, unknown> | null;
  lastSearchRunsQueryReceived: Record<string, string>;
  currentSearchRunsList: Array<Record<string, unknown>>;
}

export function defaultStubState(): StubState {
  return {
    currentPostingDisposition: "lead",
    currentPostingVersionToken: "ver-1",
    currentPostingTitle: "Principal Engineer",
    currentPostingAssessmentNote: null,
    nextTransitionResponseStatus: null,
    nextTransitionResponseBody: null,
    currentCompanyName: "Acme Cloud Services",
    currentCompanyStatus: "target",
    currentCompanyOwnership: "private",
    currentCompanyDescription: "Cloud hosting and observability tools",
    currentCompanyHeadquarters: "Seattle, WA",
    currentCompanyCategoryTags: ["cloud", "devops"],
    currentCompanyCareerSources: [
      {
        sourceType: "ats",
        atsProvider: "greenhouse",
        atsTenant: "acmecloud",
        sourceUrl: "https://boards.greenhouse.io/acmecloud",
        fetchMethod: "api",
        active: true,
      },
    ],
    currentCompanyVersionToken: "comp-ver-1",
    nextCompanyResponseStatus: null,
    nextCompanyResponseBody: null,
    lastCompaniesQueryReceived: {},
    currentApplicationStatus: "applied",
    currentApplicationEvents: [{ eventType: "applied", eventDate: "2026-10-31", notes: "Initial application submitted" }],
    nextApplicationResponseStatus: null,
    nextApplicationResponseBody: null,
    lastApplicationsQueryReceived: {},
    lastCriteriaQueryReceived: {},
    currentCriteriaList: null,
    lastNotesQueryReceived: {},
    currentNotesList: [],
    currentNoteDetail: null,
    lastSearchRunsQueryReceived: {},
    currentSearchRunsList: [],
  };
}

export interface AppHarness {
  baseUrl: string;
  /** Adds the seeded session cookie to a browser context. */
  signIn(context: BrowserContext): Promise<void>;
  stop(): Promise<void>;
}

export async function startAppHarness(s: StubState, sessionId: string): Promise<AppHarness> {
  let baseUrl = "";
  let tempTlsDir = "";
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


  const apiServer = http.createServer((req, res) => {
    const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
    res.setHeader("Content-Type", "application/json");

    if (url.pathname === "/odata/Postings") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          items: [
            {
              id: "post-e2e-1",
              companyId: "comp-1",
              companyName: "Acme Corp",
              title: s.currentPostingTitle,
              requisitionId: "REQ-101",
              location: "Remote",
              disposition: s.currentPostingDisposition,
              resurfacePolicy: "never",
              score: 92.5,
              versionToken: s.currentPostingVersionToken,
              createdAt: "2026-10-31T10:00:00Z",
              updatedAt: "2026-10-31T11:00:00Z",
            },
          ],
          value: [
            {
              id: "post-e2e-1",
              companyId: "comp-1",
              companyName: "Acme Corp",
              title: s.currentPostingTitle,
              requisitionId: "REQ-101",
              location: "Remote",
              disposition: s.currentPostingDisposition,
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
          title: s.currentPostingTitle,
          requisitionId: "REQ-101",
          location: "Remote",
          disposition: s.currentPostingDisposition,
          resurfacePolicy: "never",
          score: 92.5,
          versionToken: s.currentPostingVersionToken,
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
          assessmentNote: s.currentPostingAssessmentNote ?? null,
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
        if (s.nextTransitionResponseStatus !== null) {
          const status = s.nextTransitionResponseStatus;
          const body = s.nextTransitionResponseBody;
          s.nextTransitionResponseStatus = null;
          s.nextTransitionResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        s.currentPostingDisposition = payload.toDisposition || "scored";
        s.currentPostingVersionToken = "ver-2";

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "post-e2e-1",
            companyId: "comp-1",
            companyName: "Acme Corp",
            title: s.currentPostingTitle,
            requisitionId: "REQ-101",
            location: "Remote",
            disposition: s.currentPostingDisposition,
            resurfacePolicy: payload.resurfacePolicy || "never",
            score: 92.5,
            versionToken: s.currentPostingVersionToken,
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

    // Companies, notes, criteria and search runs list pages read from OData (Phase 5).
    if (url.pathname === "/odata/Companies" && req.method === "GET") {
      s.lastCompaniesQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          "@odata.count": 1,
          value: [
            {
              id: "comp-e2e-1",
              name: s.currentCompanyName,
              status: s.currentCompanyStatus,
              ownershipType: s.currentCompanyOwnership,
              description: s.currentCompanyDescription,
              headquarters: s.currentCompanyHeadquarters,
              categoryTags: s.currentCompanyCategoryTags,
            },
          ],
        })
      );
      return;
    }

    if (url.pathname === "/odata/Criteria" && req.method === "GET") {
      s.lastCriteriaQueryReceived = Object.fromEntries(url.searchParams.entries());
      const rows = s.currentCriteriaList ?? [
        {
          id: "crit-1",
          name: "Remote Policy",
          category: "weight",
          ruleText: "Must allow remote work",
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: null,
          isActive: true,
        },
      ];
      res.writeHead(200);
      res.end(JSON.stringify({ "@odata.count": rows.length, value: rows }));
      return;
    }

    if (url.pathname === "/odata/Notes" && req.method === "GET") {
      s.lastNotesQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(JSON.stringify({ "@odata.count": s.currentNotesList.length, value: s.currentNotesList }));
      return;
    }

    if (url.pathname === "/odata/SearchRuns" && req.method === "GET") {
      s.lastSearchRunsQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(JSON.stringify({ "@odata.count": s.currentSearchRunsList.length, value: s.currentSearchRunsList }));
      return;
    }

    if (url.pathname === "/api/v1/companies/comp-e2e-1" && req.method === "GET") {
      res.writeHead(200);
      res.end(
        JSON.stringify({
          id: "comp-e2e-1",
          name: s.currentCompanyName,
          status: s.currentCompanyStatus,
          ownershipType: s.currentCompanyOwnership,
          description: s.currentCompanyDescription,
          headquarters: s.currentCompanyHeadquarters,
          categoryTags: s.currentCompanyCategoryTags,
          careerSources: s.currentCompanyCareerSources,
          versionToken: s.currentCompanyVersionToken,
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
        if (s.nextCompanyResponseStatus !== null) {
          const status = s.nextCompanyResponseStatus;
          const body = s.nextCompanyResponseBody;
          s.nextCompanyResponseStatus = null;
          s.nextCompanyResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        if (payload.name) s.currentCompanyName = payload.name;
        if (payload.status) s.currentCompanyStatus = payload.status;
        if (payload.ownershipType) s.currentCompanyOwnership = payload.ownershipType;
        if (payload.description !== undefined) s.currentCompanyDescription = payload.description;
        if (payload.headquarters !== undefined) s.currentCompanyHeadquarters = payload.headquarters;
        if (payload.categoryTags) s.currentCompanyCategoryTags = payload.categoryTags;
        if (payload.careerSources) s.currentCompanyCareerSources = payload.careerSources;
        s.currentCompanyVersionToken = "comp-ver-2";

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "comp-e2e-1",
            name: s.currentCompanyName,
            status: s.currentCompanyStatus,
            ownershipType: s.currentCompanyOwnership,
            description: s.currentCompanyDescription,
            headquarters: s.currentCompanyHeadquarters,
            categoryTags: s.currentCompanyCategoryTags,
            careerSources: s.currentCompanyCareerSources,
            versionToken: s.currentCompanyVersionToken,
          })
        );
      });
      return;
    }

    // The applications list reads from OData (Phase 4); the REST list endpoint is no longer used by the web.
    if (url.pathname === "/odata/Applications" && req.method === "GET") {
      s.lastApplicationsQueryReceived = Object.fromEntries(url.searchParams.entries());
      res.writeHead(200);
      res.end(
        JSON.stringify({
          "@odata.count": 1,
          value: [
            {
              id: "app-e2e-1",
              postingId: "post-e2e-1",
              companyId: "comp-e2e-1",
              postingTitle: "Principal Engineer",
              companyName: "Acme Cloud Services",
              status: s.currentApplicationStatus,
              appliedAt: "2026-10-31T00:00:00Z",
              hasInterview: false,
              firstResponseAt: null,
              lastEventAt: "2026-10-31T00:00:00Z",
              ageInDays: 2,
            },
          ],
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
          status: s.currentApplicationStatus,
          appliedAt: "2026-10-31",
          events: s.currentApplicationEvents,
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
        if (s.nextApplicationResponseStatus !== null) {
          const status = s.nextApplicationResponseStatus;
          const body = s.nextApplicationResponseBody;
          s.nextApplicationResponseStatus = null;
          s.nextApplicationResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        s.currentPostingDisposition = "applied";
        s.currentApplicationStatus = "applied";
        s.currentApplicationEvents = [
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
            events: s.currentApplicationEvents,
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
        if (s.nextApplicationResponseStatus !== null) {
          const status = s.nextApplicationResponseStatus;
          const body = s.nextApplicationResponseBody;
          s.nextApplicationResponseStatus = null;
          s.nextApplicationResponseBody = null;
          res.writeHead(status);
          res.end(JSON.stringify(body));
          return;
        }

        const payload = JSON.parse(bodyStr || "{}");
        const eventType = payload.eventType || "follow_up";
        s.currentApplicationEvents.push({
          eventType,
          eventDate: payload.eventDate || "2026-11-01",
          notes: payload.notes || null,
        });

        if (eventType === "offer_accepted" || eventType === "offer_declined" || eventType === "withdrawn") {
          s.currentApplicationStatus = "closed";
          s.currentPostingDisposition = "closed";
        } else if (eventType === "rejected") {
          s.currentApplicationStatus = "rejected";
          s.currentPostingDisposition = "rejected";
        }

        res.writeHead(200);
        res.end(
          JSON.stringify({
            id: "app-e2e-1",
            postingId: "post-e2e-1",
            companyId: "comp-e2e-1",
            status: s.currentApplicationStatus,
            appliedAt: "2026-10-31",
            events: s.currentApplicationEvents,
          })
        );
      });
      return;
    }

    if (url.pathname === "/api/v1/notes/note-e2e-1" && req.method === "GET") {
      if (!s.currentNoteDetail) {
        res.writeHead(404);
        res.end(JSON.stringify({ status: 404, errorCode: "not_found", detail: "Note not found" }));
        return;
      }
      res.writeHead(200);
      res.end(JSON.stringify(s.currentNoteDetail));
      return;
    }

    res.writeHead(404);
    res.end(JSON.stringify({ status: 404, errorCode: "not_found", detail: "Not found" }));
  });

  await new Promise<void>((resolve) => {
    apiServer.listen(0, "127.0.0.1", () => {
      const addr = apiServer.address() as net.AddressInfo;
      process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${addr.port}`;
      resolve();
    });
  });

  const seedSession = () =>
    saveSession({
      sessionId,
      ownerSub: "playwright-test-user",
      accessToken: "stubbed-access-token",
      refreshToken: "stubbed-refresh-token",
      expiresAt: Date.now() + 3600_000,
      createdAt: Date.now(),
      csrfToken: "playwright-csrf-token",
    });
  // Seed authenticated user session into shared in-memory session store
  seedSession();

  const server: https.Server | http.Server = createWebServer();

  await new Promise<void>((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const addr = server.address() as net.AddressInfo;
      baseUrl = `https://127.0.0.1:${addr.port}`;
      resolve();
    });
  });

  return {
    baseUrl,
    async signIn(context: BrowserContext) {
      seedSession();
      await context.addCookies([
        {
          name: "__Host-roleradar_session",
          value: sessionId,
          url: baseUrl,
          httpOnly: true,
          secure: true,
          sameSite: "Lax",
        },
      ]);
    },
    async stop() {
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
    },
  };
}
