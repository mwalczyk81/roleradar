import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import net from "node:net";
import http from "node:http";
import type https from "node:https";
import selfsigned from "selfsigned";
import * as client from "openid-client";
import type { BrowserContext } from "@playwright/test";
import { createWebServer } from "../../../server.js";
import { saveSession } from "../../../app/lib/session.server.js";
import { setOidcConfigForTesting } from "../../../app/lib/oidc.server.js";
import { assertLoopbackStub } from "./isolation.js";

/**
 * A loopback stub API plus the real web server in front of it, for the Insights e2e specs. The stub answers only the
 * OData reads the Insights page and the two list pages make, plus GET /api/v1/insights/stats, from entirely fictional data: the counts come from
 * contracts/insights-lenses.json (the oracle's counts over the fictional seed) and the rows are generated from
 * tests/fixtures/insights-fictional.json. No database, no developer stack, no network beyond 127.0.0.1.
 */

export interface RecordedRequest {
  method: string;
  pathname: string;
  filter: string | null;
  top: string | null;
  count: string | null;
}

export type CoverageMode = "ok" | "needs-backfill";

/** What the stub answers for the stats request: normal data, a server error, no applications or runs, or a capped run series. */
export type StatsMode = "ok" | "fail" | "empty" | "capped";

export interface InsightsHarness {
  apiPort: number;
  baseUrl: string;
  requests: RecordedRequest[];
  /** Filters the stub did not recognise (a lens filter it was never given), so a spec can assert there were none. */
  unexpectedFilters: string[];
  setCoverageMode(mode: CoverageMode): void;
  setStatsMode(mode: StatsMode): void;
  signIn(context: BrowserContext): Promise<void>;
  /** The count the stub answers for a filter on an entity set, for specs that compare numbers. */
  countFor(set: "Postings" | "Applications", filter: string): number | undefined;
  close(): Promise<void>;
}

interface SnapshotEntry {
  entity: string;
  filters: Record<string, string>;
  expected?: Record<string, Record<string, number>>;
}

interface Snapshot {
  asOf: string;
  lenses: Record<string, SnapshotEntry>;
  coverage: Record<string, SnapshotEntry>;
}

interface Fixture {
  companies: string[];
  postingTitles: string[];
  unfilteredTotals: Record<string, number>;
}

const OWNER = "insights-owner-aaa";

export const SNAPSHOT_PATH = path.resolve(process.cwd(), "..", "contracts", "insights-lenses.json");
const FIXTURE_PATH = path.resolve(process.cwd(), "tests", "fixtures", "insights-fictional.json");
const STATS_FIXTURE_PATH = path.resolve(process.cwd(), "tests", "fixtures", "insights-stats-fictional.json");

interface StatsFixture {
  asOf: string;
  last90Days: StatsBody;
  allTime: StatsBody;
}

interface StatsBody {
  asOf: string;
  window: string;
  windowStart: string | null;
  responseTime: { medianDays: number | null; sampleSize: number };
  weeklyApplications: Array<{ weekStart: string; count: number }>;
  searchRunYield: { runs: Array<{ startedAt: string; newPostings: number }>; truncated: boolean };
}

function statsBodyFor(fixture: StatsFixture, window: string, asOf: string, mode: StatsMode): StatsBody {
  const base = window === "allTime" ? fixture.allTime : fixture.last90Days;
  const body: StatsBody = { ...base, asOf };
  if (mode === "empty") {
    return {
      ...body,
      responseTime: { medianDays: null, sampleSize: 0 },
      weeklyApplications: [],
      searchRunYield: { runs: [], truncated: false },
    };
  }
  if (mode === "capped") {
    const runs = Array.from({ length: 120 }, (_, i) => ({
      startedAt: new Date(Date.UTC(2026, 4, 1) + i * 86_400_000 + 43_200_000).toISOString(),
      newPostings: (i * 7) % 13,
    }));
    return { ...body, searchRunYield: { runs, truncated: true } };
  }
  return body;
}

export function loadSnapshot(): Snapshot {
  return JSON.parse(fs.readFileSync(SNAPSHOT_PATH, "utf8")) as Snapshot;
}

function freePort(): Promise<number> {
  return new Promise((resolve) => {
    const s = net.createServer();
    s.listen(0, "127.0.0.1", () => {
      const port = (s.address() as net.AddressInfo).port;
      s.close(() => resolve(port));
    });
  });
}

function postingRow(i: number, fixture: Fixture) {
  const company = fixture.companies[i % fixture.companies.length];
  const day = new Date(Date.UTC(2026, 8, 1) - i * 86_400_000).toISOString();
  return {
    id: `00000000-0000-4000-8000-${String(i + 1).padStart(12, "0")}`,
    companyId: `10000000-0000-4000-8000-${String((i % fixture.companies.length) + 1).padStart(12, "0")}`,
    companyName: company,
    title: `${fixture.postingTitles[i % fixture.postingTitles.length]} ${i + 1}`,
    requisitionId: `REQ-${i + 1}`,
    location: "Remote",
    disposition: "scored",
    resurfacePolicy: "never",
    score: 7.5,
    createdAt: day,
    updatedAt: day,
  };
}

function applicationRow(i: number, fixture: Fixture) {
  const company = fixture.companies[i % fixture.companies.length];
  const day = new Date(Date.UTC(2026, 8, 1) - i * 86_400_000).toISOString();
  return {
    id: `20000000-0000-4000-8000-${String(i + 1).padStart(12, "0")}`,
    postingId: `00000000-0000-4000-8000-${String(i + 1).padStart(12, "0")}`,
    companyId: `10000000-0000-4000-8000-${String((i % fixture.companies.length) + 1).padStart(12, "0")}`,
    postingTitle: `${fixture.postingTitles[i % fixture.postingTitles.length]} ${i + 1}`,
    companyName: company,
    status: "applied",
    appliedAt: day,
    hasInterview: false,
    firstResponseAt: null,
    lastEventAt: null,
    ageInDays: i + 30,
  };
}

export const NOTE_FIXTURE_PATH = path.resolve(process.cwd(), "tests", "fixtures", "notes-markdown-fictional.json");

export interface NoteFixture {
  id: string;
  heading: string;
  content: string;
  tags: string[];
  linkedEntityType: string | null;
  linkedEntityId: string | null;
  createdAt: string;
  updatedAt: string;
}

export function loadNoteFixture(): NoteFixture {
  return JSON.parse(fs.readFileSync(NOTE_FIXTURE_PATH, "utf8")) as NoteFixture;
}

export async function startInsightsHarness(sessionId: string): Promise<InsightsHarness> {
  const noteFixture = loadNoteFixture();
  const snapshot = loadSnapshot();
  const fixture = JSON.parse(fs.readFileSync(FIXTURE_PATH, "utf8")) as Fixture;
  const statsFixture = JSON.parse(fs.readFileSync(STATS_FIXTURE_PATH, "utf8")) as StatsFixture;

  const counts = new Map<string, number>();
  const coverageFilters = new Set<string>();
  for (const [section, entries] of [["lenses", snapshot.lenses], ["coverage", snapshot.coverage]] as const) {
    for (const entry of Object.values(entries)) {
      const set = entry.entity === "postings" ? "Postings" : "Applications";
      for (const window of ["90d", "all"]) {
        const filter = entry.filters[window];
        const expected = entry.expected?.[OWNER]?.[window];
        if (typeof expected === "number") {
          counts.set(`${set}|${filter}`, expected);
          if (section === "coverage") coverageFilters.add(`${set}|${filter}`);
        }
      }
    }
  }

  const requests: RecordedRequest[] = [];
  const unexpectedFilters: string[] = [];
  let coverageMode: CoverageMode = "ok";
  let statsMode: StatsMode = "ok";

  const apiPort = await freePort();
  const apiServer = http.createServer((req, res) => {
    const url = new URL(req.url ?? "", `http://127.0.0.1:${apiPort}`);
    const set = url.pathname === "/odata/Postings" ? "Postings" : url.pathname === "/odata/Applications" ? "Applications" : null;
    const filter = url.searchParams.get("$filter");
    requests.push({
      method: req.method ?? "GET",
      pathname: url.pathname,
      filter,
      top: url.searchParams.get("$top"),
      count: url.searchParams.get("$count"),
    });

    // Phase 5 (T083): the one fictional note the Markdown e2e renders. Nothing else under /api/v1/notes exists in the stub.
    if (req.method === "GET" && url.pathname === `/api/v1/notes/${noteFixture.id}`) {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(noteFixture));
      return;
    }

    if (url.pathname === "/api/v1/insights/stats" && req.method === "GET") {
      const window = url.searchParams.get("window") ?? "";
      const asOf = url.searchParams.get("asOf") ?? "";
      if (statsMode === "fail") {
        res.writeHead(500, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ ok: false, errorCode: "internal_error", message: "Fictional stub failure." }));
        return;
      }
      if ((window !== "last90Days" && window !== "allTime") || !/^\d{4}-\d{2}-\d{2}$/.test(asOf)) {
        res.writeHead(400, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ ok: false, errorCode: "validation_error", message: "Bad window or asOf." }));
        return;
      }
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(statsBodyFor(statsFixture, window, asOf, statsMode)));
      return;
    }

    if (!set || req.method !== "GET") {
      res.writeHead(404);
      res.end();
      return;
    }

    let total: number | undefined;
    if (filter === null) {
      total = fixture.unfilteredTotals[set];
    } else {
      const key = `${set}|${filter}`;
      total = counts.get(key);
      if (total !== undefined && coverageMode === "needs-backfill" && coverageFilters.has(key) && total > 0) {
        total -= 1;
      }
    }

    if (total === undefined) {
      unexpectedFilters.push(filter ?? "");
      res.writeHead(400, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ ok: false, errorCode: "validation_error", message: "Unexpected filter for the fictional stub." }));
      return;
    }

    const top = Number(url.searchParams.get("$top") ?? "25");
    const skip = Number(url.searchParams.get("$skip") ?? "0");
    const available = Math.max(0, total - skip);
    const size = Number.isNaN(top) ? 0 : Math.min(top, available);
    const rows = Array.from({ length: size }, (_, n) => (set === "Postings" ? postingRow(skip + n, fixture) : applicationRow(skip + n, fixture)));

    const body: { value: unknown[]; "@odata.count"?: number } = { value: rows };
    if (url.searchParams.get("$count") === "true") {
      body["@odata.count"] = total;
    }
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify(body));
  });
  await new Promise<void>((resolve) => apiServer.listen(apiPort, "127.0.0.1", resolve));

  const tempTlsDir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-e2e-insights-"));
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
  process.env.NODE_ENV = "test";
  process.env.PORT = "0";
  process.env.ROLERADAR_API_BASE_URL = `http://127.0.0.1:${apiPort}`;

  // Fail closed before anything is served: the web server must talk to this run's loopback stub and nothing else.
  assertLoopbackStub(process.env.ROLERADAR_API_BASE_URL, apiPort);

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

  saveSession({
    sessionId,
    ownerSub: "fictional-owner-e2e",
    ownerUsername: "fictional-owner",
    accessToken: "mock-e2e-access-token",
    refreshToken: "mock-e2e-refresh-token",
    idToken: "mock-e2e-id-token",
    expiresAt: Date.now() + 3_600_000,
    createdAt: Date.now(),
    csrfToken: "mock-e2e-csrf-token",
  });

  const webServer: https.Server | http.Server = createWebServer();
  const baseUrl = await new Promise<string>((resolve) => {
    webServer.listen(0, "127.0.0.1", () => {
      resolve(`https://127.0.0.1:${(webServer.address() as net.AddressInfo).port}`);
    });
  });

  return {
    apiPort,
    baseUrl,
    requests,
    unexpectedFilters,
    setCoverageMode: (mode) => {
      coverageMode = mode;
    },
    setStatsMode: (mode) => {
      statsMode = mode;
    },
    signIn: async (context) => {
      await context.addCookies([
        { name: "__Host-roleradar_session", value: sessionId, url: baseUrl, httpOnly: true, secure: true, sameSite: "Lax" },
      ]);
    },
    countFor: (set, filter) => counts.get(`${set}|${filter}`),
    close: async () => {
      setOidcConfigForTesting(null);
      await new Promise<void>((resolve) => webServer.close(() => resolve()));
      await new Promise<void>((resolve) => apiServer.close(() => resolve()));
      fs.rmSync(tempTlsDir, { recursive: true, force: true });
    },
  };
}
