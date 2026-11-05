import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as insightsLoader } from "../../app/routes/insights.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";
import {
  COUNTED_LENS_KEYS,
  COVERAGE_KEYS,
  lensCountQuery,
  lensEntity,
  coverageCountQuery,
  coverageEntity,
  type LensContext,
  type CountedLensKey,
} from "../../app/lib/insights-lenses.js";
import type { InsightsView } from "../../app/lib/insights-view.js";

interface InsightsLoaderData {
  view: InsightsView;
  csrfToken: string;
  userLabel: string;
}

const AS_OF = "2026-09-30";
const NINETY: LensContext = { window: "90d", asOf: AS_OF };
const ALL: LensContext = { window: "all", asOf: AS_OF };

/** Consistent counts: every funnel nests and every flag is covered. */
const COUNTS: Record<CountedLensKey, number> = {
  leads: 120,
  reviewed: 60,
  "applied-to": 30,
  "screened-out": 15,
  expired: 7,
  applied: 40,
  responded: 24,
  interviewed: 16,
  offers: 5,
  "offers-interviewed": 4,
  mature: 36,
  "mature-interviewed": 15,
  rejected: 12,
  "rejected-before-interview": 7,
  "rejected-after-interview": 5,
  "ghost-eligible": 30,
  ghosted: 9,
  open: 22,
};

/** Maps a query string back to the count it should answer, for the 90 day window and the all-time pair. */
function answerFor(queryString: string): number {
  for (const key of COUNTED_LENS_KEYS) {
    if (queryString === lensCountQuery(key, NINETY)) return COUNTS[key];
  }
  if (queryString === lensCountQuery("interviewed", ALL)) return 20;
  if (queryString === lensCountQuery("applied", ALL)) return 55;
  if (queryString === coverageCountQuery("coverage-postings", NINETY)) return COUNTS.leads;
  if (queryString === coverageCountQuery("coverage-applications", NINETY)) return COUNTS.applied;
  throw new Error(`Unexpected count query: ${queryString}`);
}

function okResponse(count: number) {
  return { data: { value: [], "@odata.count": count }, error: undefined, response: { status: 200 } as Response };
}

describe("Insights loader (T042)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockFetchOData = vi.fn();
  const mockOidcConfig = new client.Configuration(
    {
      issuer: "https://identity:5001",
      authorization_endpoint: "https://identity:5001/connect/authorize",
      token_endpoint: "https://identity:5001/connect/token",
      end_session_endpoint: "https://identity:5001/connect/endsession",
    },
    "roleradar-web",
    "test-secret"
  );

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    setOidcConfigForTesting(mockOidcConfig);
    vi.restoreAllMocks();
    mockFetchOData.mockReset();
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
    // These tests are about the counts. The stats read has its own tests (insights.stats.test.ts); here it answers
    // "unavailable" without any network, so it adds no count request.
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: vi.fn().mockResolvedValue({ data: undefined, error: { message: "not under test" }, response: { status: 500 } }),
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function createTestSession(): string {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "fictional-owner",
      accessToken: "mock-access-token-insights",
      refreshToken: "mock-refresh-token-insights",
      idToken: "mock-id-token-insights",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-insights",
    };
    saveSession(session);
    return sessionId;
  }

  function requestFor(sessionId: string, query: string): Request {
    return new Request(`https://localhost:5003/insights${query}`, { headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` } });
  }

  it("runs 22 count-only requests for the 90 day view: 18 lens counts, 2 coverage checks and the 2 all-time interview counts", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(mockFetchOData).toHaveBeenCalledTimes(22);
    const queries = mockFetchOData.mock.calls.map(([path, qs]) => `${path}?${qs}`);
    expect(new Set(queries).size).toBe(22);
    for (const [path, qs, token] of mockFetchOData.mock.calls) {
      expect(["/odata/Postings", "/odata/Applications"]).toContain(path);
      expect(qs).toContain("$top=0");
      expect(qs).toContain("$count=true");
      expect(qs).not.toContain("$select");
      expect(token).toBe("mock-access-token-insights");
    }
  });

  it("runs 20 count-only requests for the all-time view", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      for (const key of COUNTED_LENS_KEYS) if (queryString === lensCountQuery(key, ALL)) return okResponse(COUNTS[key]);
      for (const key of COVERAGE_KEYS) {
        if (queryString === coverageCountQuery(key, ALL)) return okResponse(key === "coverage-postings" ? COUNTS.leads : COUNTS.applied);
      }
      throw new Error(`Unexpected count query: ${queryString}`);
    });

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=all&asOf=${AS_OF}`));

    expect(mockFetchOData).toHaveBeenCalledTimes(20);
  });

  it("never counts the drill-only lens: the offers-without-interview number is derived, its list is the drill-down", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));

    await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    for (const [, queryString] of mockFetchOData.mock.calls) {
      expect(String(queryString)).not.toContain("hasInterview eq false");
    }
  });

  it("sends each request to the entity set its lens belongs to", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));

    await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    for (const key of COUNTED_LENS_KEYS) {
      const expectedPath = lensEntity(key) === "postings" ? "/odata/Postings" : "/odata/Applications";
      expect(mockFetchOData).toHaveBeenCalledWith(expectedPath, lensCountQuery(key, NINETY), "mock-access-token-insights", expect.anything());
    }
    for (const key of COVERAGE_KEYS) {
      const expectedPath = coverageEntity(key) === "postings" ? "/odata/Postings" : "/odata/Applications";
      expect(mockFetchOData).toHaveBeenCalledWith(expectedPath, coverageCountQuery(key, NINETY), "mock-access-token-insights", expect.anything());
    }
  });

  it("keeps at most 6 requests in flight, and does run them in parallel", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await new Promise((resolve) => setTimeout(resolve, 5));
      inFlight--;
      return okResponse(answerFor(queryString));
    });

    await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    expect(maxInFlight).toBeLessThanOrEqual(6);
    expect(maxInFlight).toBeGreaterThan(1);
    expect(mockFetchOData).toHaveBeenCalledTimes(22);
  });

  it("turns the counts into the view: funnels, rates and links all carry asOf", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    expect(data.view.ctx).toEqual({ window: "90d", asOf: AS_OF });
    expect(data.view.funnels[0].stages.map((s) => s.value)).toEqual([120, 60, 30]);
    expect(data.view.funnels[1].stages.map((s) => s.value)).toEqual([40, 24, 16, 4]);
    expect(data.view.rates.find((r) => r.id === "application-rate")?.of).toBe("30 of 60");
    expect(data.view.rates.find((r) => r.id === "interview-rate-all-time")?.of).toBe("20 of 55");
    expect(data.view.funnels[0].stages[1].href).toBe(`/postings?lens=reviewed&window=90d&asOf=${AS_OF}`);
    expect(data.csrfToken).toBe("mock-csrf-token-insights");
    expect(data.userLabel).toBeTruthy();
  });

  it("defaults the window to 90d and asOf to today (UTC), and ignores a bad window or asOf", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-09-30T23:30:00Z"));
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));
    const sessionId = createTestSession();

    const defaults = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(sessionId, ""));
    expect(defaults.view.ctx).toEqual({ window: "90d", asOf: AS_OF });

    mockFetchOData.mockClear();
    const bad = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(sessionId, "?window=forever&asOf=not-a-date"));
    expect(bad.view.ctx).toEqual({ window: "90d", asOf: AS_OF });
    expect(mockFetchOData).toHaveBeenCalledTimes(22);
  });

  it("a failed request degrades only the numbers that depend on it", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      if (queryString === lensCountQuery("ghost-eligible", NINETY)) {
        return { data: undefined, error: { message: "boom", status: 500 }, response: { status: 500 } as Response };
      }
      return okResponse(answerFor(queryString));
    });

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    expect(data.view.rates.find((r) => r.id === "ghost-rate")?.state).toBe("unavailable");
    expect(data.view.rates.find((r) => r.id === "application-rate")?.state).toBe("ok");
    expect(data.view.funnels[1].stages.every((s) => s.state === "ok")).toBe(true);
  });

  it("a request that never answers is cut off after 5 seconds and only its numbers become unavailable", async () => {
    vi.useFakeTimers();
    mockFetchOData.mockImplementation((_path: string, queryString: string) => {
      if (queryString === lensCountQuery("ghosted", NINETY)) {
        return new Promise(() => {});
      }
      return Promise.resolve(okResponse(answerFor(queryString)));
    });
    const sessionId = createTestSession();

    const pending = callLoader<InsightsLoaderData>(insightsLoader, requestFor(sessionId, `?asOf=${AS_OF}`));
    await vi.advanceTimersByTimeAsync(4_900);
    let settled = false;
    void pending.then(() => {
      settled = true;
    });
    await vi.advanceTimersByTimeAsync(0);
    expect(settled).toBe(false);

    await vi.advanceTimersByTimeAsync(200);
    const data = await pending;

    expect(data.view.rates.find((r) => r.id === "ghost-rate")?.state).toBe("unavailable");
    expect(data.view.rates.find((r) => r.id === "application-rate")?.state).toBe("ok");
  });

  it("passes an abort signal so a timed-out request is actually cancelled", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okResponse(answerFor(queryString)));

    await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));

    for (const call of mockFetchOData.mock.calls) {
      expect(call[3]).toBeInstanceOf(AbortSignal);
    }
  });

  it("re-challenges authentication once on a 401, with no retry loop", async () => {
    let calls = 0;
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      calls++;
      if (calls === 1) {
        return { data: undefined, error: { message: "Unauthorized", status: 401 }, response: { status: 401 } as Response };
      }
      return okResponse(answerFor(queryString));
    });

    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toContain("/connect/authorize");
    expect(mockFetchOData.mock.calls.length).toBeLessThanOrEqual(22);
  });

  it("redirects to /not-authorized on a 403", async () => {
    mockFetchOData.mockImplementation(async () => ({
      data: undefined,
      error: { message: "Forbidden", status: 403 },
      response: { status: 403 } as Response,
    }));

    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, requestFor(createTestSession(), `?asOf=${AS_OF}`));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  it("redirects to sign-in when there is no session", async () => {
    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, new Request("https://localhost:5003/insights"));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown?.status).toBe(302);
    expect(mockFetchOData).not.toHaveBeenCalled();
  });
});
