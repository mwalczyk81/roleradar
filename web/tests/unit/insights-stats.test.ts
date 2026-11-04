import { describe, it, expect, expectTypeOf, vi } from "vitest";
import type { components, paths } from "../../../contracts/client/api-types.js";
import {
  MAX_RUNS_SHOWN,
  UNAVAILABLE_STATS,
  buildStatsView,
  formatMedianDays,
  statsWindow,
  type InsightsStatsDto,
} from "../../app/lib/insights-stats.js";
import { loadInsightsStats } from "../../app/lib/insights-stats.server.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import type { LensContext } from "../../app/lib/insights-lenses.js";

/**
 * T069 (unit part): the stats call is typed from the generated client, the view is built from the DTO only, and
 * every state the page can be in has a defined, honest rendering.
 */

type StatsOperation = paths["/api/v1/insights/stats"]["get"];

describe("generated types for GET /api/v1/insights/stats", () => {
  it("types window as a string-literal union and asOf as a string", () => {
    expectTypeOf<StatsOperation["parameters"]["query"]["window"]>().toEqualTypeOf<"last90Days" | "allTime">();
    expectTypeOf<StatsOperation["parameters"]["query"]["asOf"]>().toEqualTypeOf<string>();
    expectTypeOf<InsightsStatsDto>().toEqualTypeOf<components["schemas"]["InsightsStatsDto"]>();
    expectTypeOf<InsightsStatsDto["window"]>().toEqualTypeOf<"last90Days" | "allTime">();
  });
});

describe("statsWindow", () => {
  it("maps the page window to the API enum", () => {
    expect(statsWindow({ window: "90d", asOf: "2026-09-30" })).toBe("last90Days");
    expect(statsWindow({ window: "all", asOf: "2026-09-30" })).toBe("allTime");
  });
});

describe("formatMedianDays", () => {
  it("writes whole and half days with the right singular or plural", () => {
    expect(formatMedianDays(0)).toBe("0 days");
    expect(formatMedianDays(1)).toBe("1 day");
    expect(formatMedianDays(2.5)).toBe("2.5 days");
    expect(formatMedianDays(10)).toBe("10 days");
    expect(formatMedianDays(1.5)).toBe("1.5 days");
  });
});

function dto(overrides: Partial<InsightsStatsDto> = {}): InsightsStatsDto {
  return {
    asOf: "2026-09-30",
    window: "last90Days",
    windowStart: "2026-07-03",
    responseTime: { medianDays: 10, sampleSize: 14 },
    weeklyApplications: [
      { weekStart: "2026-09-14", count: 2 },
      { weekStart: "2026-09-21", count: 0 },
      { weekStart: "2026-09-28", count: 1 },
    ],
    searchRunYield: {
      runs: [
        { startedAt: "2026-09-10T12:00:00Z", newPostings: 4 },
        { startedAt: "2026-09-20T12:00:00Z", newPostings: 9 },
      ],
      truncated: false,
    },
    ...overrides,
  };
}

describe("buildStatsView", () => {
  it("shows the median in days with how many responses it is based on", () => {
    const view = buildStatsView(dto());

    expect(view.state).toBe("ok");
    expect(view.median.state).toBe("ok");
    expect(view.median.value).toBe("10 days");
    expect(view.median.sampleText).toBe("14 responses");
    expect(view.median.sampleSize).toBe(14);
  });

  it("uses the singular for one response", () => {
    const view = buildStatsView(dto({ responseTime: { medianDays: 3, sampleSize: 1 } }));

    expect(view.median.value).toBe("3 days");
    expect(view.median.sampleText).toBe("1 response");
  });

  it("says 'No data' for an empty sample instead of a number", () => {
    const view = buildStatsView(dto({ responseTime: { medianDays: null, sampleSize: 0 } }));

    expect(view.median.state).toBe("no_data");
    expect(view.median.value).toBe("No data");
    expect(view.median.days).toBeNull();
    expect(view.median.sampleText).toMatch(/no responses/i);
  });

  it("keeps a zero-day median as a number, not 'No data'", () => {
    const view = buildStatsView(dto({ responseTime: { medianDays: 0, sampleSize: 3 } }));

    expect(view.median.state).toBe("ok");
    expect(view.median.value).toBe("0 days");
  });

  it("turns weekly buckets into chart items with UTC date labels and keeps the zero weeks", () => {
    const view = buildStatsView(dto());

    expect(view.weekly.items).toEqual([
      { label: "2026-09-14", fullLabel: "Week of 2026-09-14", value: 2 },
      { label: "2026-09-21", fullLabel: "Week of 2026-09-21", value: 0 },
      { label: "2026-09-28", fullLabel: "Week of 2026-09-28", value: 1 },
    ]);
    expect(view.weekly.total).toBe(3);
    expect(view.weekly.empty).toBe(false);
  });

  it("flags an empty weekly series (all-time with no applications)", () => {
    const view = buildStatsView(dto({ weeklyApplications: [] }));

    expect(view.weekly.items).toEqual([]);
    expect(view.weekly.empty).toBe(true);
  });

  it("treats all-zero weeks as data, not as empty", () => {
    const view = buildStatsView(dto({ weeklyApplications: [{ weekStart: "2026-09-28", count: 0 }] }));

    expect(view.weekly.empty).toBe(false);
    expect(view.weekly.total).toBe(0);
  });

  it("turns search runs into sparkline values and table rows, oldest first", () => {
    const view = buildStatsView(dto());

    expect(view.runs.values).toEqual([4, 9]);
    expect(view.runs.rows).toEqual([
      { date: "2026-09-10", newPostings: 4 },
      { date: "2026-09-20", newPostings: 9 },
    ]);
    expect(view.runs.truncated).toBe(false);
    expect(view.runs.note).toBeNull();
    expect(view.runs.empty).toBe(false);
  });

  it("labels a capped run series 'showing latest 120 runs'", () => {
    const runs = Array.from({ length: MAX_RUNS_SHOWN }, (_, i) => ({ startedAt: "2026-09-10T12:00:00Z", newPostings: i }));
    const view = buildStatsView(dto({ searchRunYield: { runs, truncated: true } }));

    expect(MAX_RUNS_SHOWN).toBe(120);
    expect(view.runs.truncated).toBe(true);
    expect(view.runs.note).toMatch(/showing latest 120 runs/i);
  });

  it("flags an empty run series", () => {
    const view = buildStatsView(dto({ searchRunYield: { runs: [], truncated: false } }));

    expect(view.runs.empty).toBe(true);
    expect(view.runs.values).toEqual([]);
  });

  it("is the unavailable view for a missing DTO, with each part marked unavailable", () => {
    const view = buildStatsView(null);

    expect(view).toEqual(UNAVAILABLE_STATS);
    expect(view.state).toBe("unavailable");
    expect(view.median.state).toBe("unavailable");
    expect(view.median.value).toBe("Unavailable");
    expect(view.weekly.items).toEqual([]);
    expect(view.runs.values).toEqual([]);
  });
});

describe("loadInsightsStats (typed fetch)", () => {
  const ctx: LensContext = { window: "90d", asOf: "2026-09-30" };
  const request = new Request("https://localhost:5003/insights");
  const session = { accessToken: "mock-access-token-stats", sessionId: "session-stats" };

  function mockClient(get: ReturnType<typeof vi.fn>) {
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({ GET: get } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  }

  it("calls the generated path with the window enum and asOf, the owner's token and an abort signal", async () => {
    const get = vi.fn().mockResolvedValue({ data: dto(), error: undefined, response: { status: 200 } });
    mockClient(get);

    const result = await loadInsightsStats({ request, session, ctx });

    expect(apiClientModule.createApiClient).toHaveBeenCalledWith("mock-access-token-stats");
    expect(get).toHaveBeenCalledTimes(1);
    const [path, init] = get.mock.calls[0];
    expect(path).toBe("/api/v1/insights/stats");
    expect(init.params).toEqual({ query: { window: "last90Days", asOf: "2026-09-30" } });
    expect(init.signal).toBeInstanceOf(AbortSignal);
    expect(result.state).toBe("ok");
    expect(result.view.median.value).toBe("10 days");
  });

  it("sends allTime for the all-time window", async () => {
    const get = vi.fn().mockResolvedValue({ data: dto({ window: "allTime", windowStart: null }), error: undefined, response: { status: 200 } });
    mockClient(get);

    await loadInsightsStats({ request, session, ctx: { window: "all", asOf: "2026-09-30" } });

    expect(get.mock.calls[0][1].params).toEqual({ query: { window: "allTime", asOf: "2026-09-30" } });
  });

  it("degrades to the unavailable view on a server error, a 400, a 404 or a network error, without throwing", async () => {
    for (const outcome of [
      { data: undefined, error: { message: "boom" }, response: { status: 500 } },
      { data: undefined, error: { message: "bad" }, response: { status: 400 } },
      { data: undefined, error: { message: "gone" }, response: { status: 404 } },
    ]) {
      mockClient(vi.fn().mockResolvedValue(outcome));
      const result = await loadInsightsStats({ request, session, ctx });
      expect(result.state).toBe("unavailable");
      expect(result.view).toEqual(UNAVAILABLE_STATS);
    }

    mockClient(vi.fn().mockRejectedValue(new Error("connect ECONNREFUSED")));
    expect((await loadInsightsStats({ request, session, ctx })).state).toBe("unavailable");
  });

  it("reports a 401 or 403 as an auth failure and leaves it to the loader to hand to assertApiOk", async () => {
    mockClient(vi.fn().mockResolvedValue({ data: undefined, error: { message: "no" }, response: { status: 401 } }));
    const unauthorized = await loadInsightsStats({ request, session, ctx });
    expect(unauthorized.state).toBe("unavailable");
    expect(unauthorized.authFailure).toEqual({ status: 401 });

    mockClient(vi.fn().mockResolvedValue({ data: undefined, error: { message: "no" }, response: { status: 403 } }));
    expect((await loadInsightsStats({ request, session, ctx })).authFailure).toEqual({ status: 403 });
  });

  it("gives up after 5 seconds and aborts the request", async () => {
    vi.useFakeTimers();
    try {
      let signal: AbortSignal | undefined;
      mockClient(
        vi.fn().mockImplementation((_path: string, init: { signal: AbortSignal }) => {
          signal = init.signal;
          return new Promise(() => {});
        })
      );

      const pending = loadInsightsStats({ request, session, ctx });
      await vi.advanceTimersByTimeAsync(5_100);
      const result = await pending;

      expect(result.state).toBe("unavailable");
      expect(signal?.aborted).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});
