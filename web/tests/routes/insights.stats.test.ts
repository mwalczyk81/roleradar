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
  lensCountQuery,
  coverageCountQuery,
  type LensContext,
  type CountedLensKey,
} from "../../app/lib/insights-lenses.js";
import type { InsightsView } from "../../app/lib/insights-view.js";
import type { InsightsStatsDto, StatsView } from "../../app/lib/insights-stats.js";

/**
 * T069 (route part): the loader starts the stats request with the first wave of counts, shares the 6-request bound,
 * makes exactly 23 requests in the 90 day view and 21 in the all-time view (22 and 20 counts plus 1 stats), and a stats
 * failure degrades only the trend and median tiles.
 */

interface InsightsLoaderData {
  view: InsightsView;
  stats: StatsView;
  csrfToken: string;
  userLabel: string;
}

const AS_OF = "2026-09-30";
const NINETY: LensContext = { window: "90d", asOf: AS_OF };
const ALL: LensContext = { window: "all", asOf: AS_OF };

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

function answerFor(queryString: string, ctx: LensContext): number {
  for (const key of COUNTED_LENS_KEYS) {
    if (queryString === lensCountQuery(key, ctx)) return COUNTS[key];
  }
  if (queryString === lensCountQuery("interviewed", ALL)) return 20;
  if (queryString === lensCountQuery("applied", ALL)) return 55;
  if (queryString === coverageCountQuery("coverage-postings", ctx)) return COUNTS.leads;
  if (queryString === coverageCountQuery("coverage-applications", ctx)) return COUNTS.applied;
  throw new Error(`Unexpected count query: ${queryString}`);
}

function okCount(count: number) {
  return { data: { value: [], "@odata.count": count }, error: undefined, response: { status: 200 } as Response };
}

const STATS: InsightsStatsDto = {
  asOf: AS_OF,
  window: "last90Days",
  windowStart: "2026-07-03",
  responseTime: { medianDays: 10, sampleSize: 14 },
  weeklyApplications: [
    { weekStart: "2026-09-21", count: 1 },
    { weekStart: "2026-09-28", count: 1 },
  ],
  searchRunYield: { runs: [{ startedAt: "2026-09-20T12:00:00Z", newPostings: 9 }], truncated: false },
};

describe("Insights loader with the stats request (T069)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockFetchOData = vi.fn();
  const mockGet = vi.fn();
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
    mockGet.mockReset();
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({ GET: mockGet } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
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

  function answerCounts(ctx: LensContext) {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => okCount(answerFor(queryString, ctx)));
  }

  function answerStats() {
    mockGet.mockResolvedValue({ data: STATS, error: undefined, response: { status: 200 } });
  }

  it("makes exactly 23 requests in the 90 day view: 22 counts and 1 stats", async () => {
    answerCounts(NINETY);
    answerStats();

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(mockFetchOData).toHaveBeenCalledTimes(22);
    expect(mockGet).toHaveBeenCalledTimes(1);
    expect(mockFetchOData.mock.calls.length + mockGet.mock.calls.length).toBe(23);
  });

  it("makes exactly 21 requests in the all-time view: 20 counts and 1 stats", async () => {
    answerCounts(ALL);
    answerStats();

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=all&asOf=${AS_OF}`));

    expect(mockFetchOData).toHaveBeenCalledTimes(20);
    expect(mockGet).toHaveBeenCalledTimes(1);
    expect(mockFetchOData.mock.calls.length + mockGet.mock.calls.length).toBe(21);
  });

  it("asks the generated stats path for the window and as-of date on screen, with the owner's token", async () => {
    answerCounts(NINETY);
    answerStats();

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(apiClientModule.createApiClient).toHaveBeenCalledWith("mock-access-token-insights");
    const [path, init] = mockGet.mock.calls[0];
    expect(path).toBe("/api/v1/insights/stats");
    expect(init.params).toEqual({ query: { window: "last90Days", asOf: AS_OF } });
  });

  it("starts the stats request with the first wave of counts, so it adds no latency", async () => {
    const order: string[] = [];
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      order.push("count");
      return okCount(answerFor(queryString, NINETY));
    });
    mockGet.mockImplementation(async () => {
      order.push("stats");
      return { data: STATS, error: undefined, response: { status: 200 } };
    });

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(order).toHaveLength(23);
    // The bound is 6 in flight, so the first wave is the first 6 calls: the stats call is one of them.
    expect(order.indexOf("stats")).toBeLessThan(6);
  });

  it("does not wait for the counts to finish before starting the stats request", async () => {
    let releaseCounts: () => void = () => {};
    const countsBlocked = new Promise<void>((resolve) => {
      releaseCounts = resolve;
    });
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      await countsBlocked;
      return okCount(answerFor(queryString, NINETY));
    });
    answerStats();

    const pending = callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(mockGet).toHaveBeenCalledTimes(1); // started while every count is still waiting
    releaseCounts();
    await pending;
  });

  it("keeps at most 6 requests in flight across counts and stats together", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    const track = async <T>(result: T): Promise<T> => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await new Promise((resolve) => setTimeout(resolve, 5));
      inFlight--;
      return result;
    };
    mockFetchOData.mockImplementation((_path: string, queryString: string) => track(okCount(answerFor(queryString, NINETY))));
    mockGet.mockImplementation(() => track({ data: STATS, error: undefined, response: { status: 200 } }));

    await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(maxInFlight).toBeLessThanOrEqual(6);
    expect(maxInFlight).toBeGreaterThan(1);
    expect(mockFetchOData.mock.calls.length + mockGet.mock.calls.length).toBe(23);
  });

  it("returns the stats view next to the counts view", async () => {
    answerCounts(NINETY);
    answerStats();

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(data.stats.state).toBe("ok");
    expect(data.stats.median.value).toBe("10 days");
    expect(data.stats.weekly.total).toBe(2);
    expect(data.stats.runs.values).toEqual([9]);
    expect(data.view.rates.find((r) => r.id === "application-rate")?.state).toBe("ok");
  });

  it("a stats failure degrades only the trend and median tiles: the counts view is untouched", async () => {
    answerCounts(NINETY);
    mockGet.mockResolvedValue({ data: undefined, error: { message: "boom" }, response: { status: 500 } });

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(data.stats.state).toBe("unavailable");
    expect(data.stats.median.state).toBe("unavailable");
    expect(data.view.funnels[0].stages.map((s) => s.value)).toEqual([120, 60, 30]);
    expect(data.view.rates.every((r) => r.state !== "unavailable")).toBe(true);
  });

  it("a stats request that throws degrades only the stats", async () => {
    answerCounts(NINETY);
    mockGet.mockRejectedValue(new Error("connect ECONNREFUSED"));

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(data.stats.state).toBe("unavailable");
    expect(data.view.funnels[1].stages.every((s) => s.state === "ok")).toBe(true);
  });

  it("a stats request that never answers is cut off after 5 seconds and the counts still render", async () => {
    vi.useFakeTimers();
    answerCounts(NINETY);
    mockGet.mockImplementation(() => new Promise(() => {}));

    const pending = callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));
    await vi.advanceTimersByTimeAsync(5_200);
    const data = await pending;

    expect(data.stats.state).toBe("unavailable");
    expect(data.view.rates.find((r) => r.id === "application-rate")?.state).toBe("ok");
  });

  it("a failed count does not take the stats down with it", async () => {
    mockFetchOData.mockImplementation(async (_path: string, queryString: string) => {
      if (queryString === lensCountQuery("ghosted", NINETY)) {
        return { data: undefined, error: { message: "boom", status: 500 }, response: { status: 500 } as Response };
      }
      return okCount(answerFor(queryString, NINETY));
    });
    answerStats();

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(data.stats.state).toBe("ok");
    expect(data.view.rates.find((r) => r.id === "ghost-rate")?.state).toBe("unavailable");
  });

  it("re-challenges authentication once when only the stats request answers 401", async () => {
    answerCounts(NINETY);
    mockGet.mockResolvedValue({ data: undefined, error: { message: "Unauthorized" }, response: { status: 401 } });

    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toContain("/connect/authorize");
    expect(mockGet).toHaveBeenCalledTimes(1); // no retry loop
  });

  it("redirects to /not-authorized when only the stats request answers 403", async () => {
    answerCounts(NINETY);
    mockGet.mockResolvedValue({ data: undefined, error: { message: "Forbidden" }, response: { status: 403 } });

    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  it("does not throw on a stats 404 (an API without the endpoint): the stats are unavailable and the page stays up", async () => {
    answerCounts(NINETY);
    mockGet.mockResolvedValue({ data: undefined, error: { message: "Not found" }, response: { status: 404 } });

    const data = await callLoader<InsightsLoaderData>(insightsLoader, requestFor(createTestSession(), `?window=90d&asOf=${AS_OF}`));

    expect(data.stats.state).toBe("unavailable");
    expect(data.view.funnels).toHaveLength(2);
  });

  it("never calls the stats endpoint without a session", async () => {
    let thrown: Response | null = null;
    try {
      await callLoader(insightsLoader, new Request("https://localhost:5003/insights"));
    } catch (err) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown?.status).toBe(302);
    expect(mockGet).not.toHaveBeenCalled();
  });
});
