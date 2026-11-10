import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { backoffMs, shouldRetry } from "../../app/lib/observability";
import { fetchWithPolicy } from "../../app/lib/api-client.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";
import { loadInsightsCounts } from "../../app/lib/insights-counts.server";
import { loadInsightsStats } from "../../app/lib/insights-stats.server";
import type { InsightsStatsDto } from "../../app/lib/insights-stats";
import type { LensContext } from "../../app/lib/insights-lenses";

// Feature 013 US3 (FR-026, FR-027, SC-007): the outbound policy of the web server. Reads (GET, HEAD) are attempted at most
// 3 times, on a network error, a 5 s per-attempt timeout, 502, 503 or 504, with a 200 ms and then a 500 ms backoff. Writes
// (POST, PATCH, PUT, DELETE) are sent exactly once, with a 10 s timeout. 400, 401, 404, 409 and 500 are never retried. A
// caller's abort is not retried and propagates. Every attempt carries the same traceparent. A fake fetch and fake
// timers stand in for the network and the clock.

const URL_UNDER_TEST = "https://api.test/odata/Postings?$top=1";
const TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const SPAN_ID = "00f067aa0ba902b7";

type Step = number | "network" | "hang";

function scriptedFetch(steps: readonly Step[]) {
  const requests: Request[] = [];
  const fake = vi.fn((input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request = new Request(input, init);
    requests.push(request);
    const step = steps[Math.min(requests.length - 1, steps.length - 1)];
    if (step === "network") {
      return Promise.reject(new TypeError("fetch failed"));
    }
    if (step === "hang") {
      // Never settles and ignores the abort signal, like a socket that swallows packets.
      return new Promise<Response>(() => undefined);
    }
    return Promise.resolve(new Response(JSON.stringify({ value: [] }), { status: step, headers: { "Content-Type": "application/json" } }));
  });
  vi.stubGlobal("fetch", fake);
  return { fake, requests };
}

type Outcome = { response: Response } | { error: unknown };

function start(request: Request): Promise<Outcome> {
  return fetchWithPolicy(request).then(
    (response) => ({ response }),
    (error: unknown) => ({ error })
  );
}

async function settled(promise: Promise<Outcome>): Promise<Outcome> {
  await vi.advanceTimersByTimeAsync(60_000);
  return promise;
}

function get(signal?: AbortSignal): Request {
  return new Request(URL_UNDER_TEST, { method: "GET", signal });
}

function write(method: string): Request {
  return new Request("https://api.test/api/v1/postings/1/transition", {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ toDisposition: "scored" }),
  });
}

describe("shouldRetry", () => {
  it.each(["GET", "HEAD", "get"])("retries a %s on a network error, a timeout, 502, 503 and 504 before the third attempt", (method) => {
    for (const failure of ["network", "timeout", 502, 503, 504] as const) {
      expect(shouldRetry(method, failure, 1), `${method} ${failure} after 1`).toBe(true);
      expect(shouldRetry(method, failure, 2), `${method} ${failure} after 2`).toBe(true);
      expect(shouldRetry(method, failure, 3), `${method} ${failure} after 3`).toBe(false);
    }
  });

  it.each([400, 401, 403, 404, 409, 500, 501, 200])("never retries status %i, even for a GET", (status) => {
    expect(shouldRetry("GET", status, 1)).toBe(false);
  });

  it.each(["POST", "PATCH", "PUT", "DELETE", "patch"])("never retries a %s, whatever went wrong", (method) => {
    for (const failure of ["network", "timeout", 502, 503, 504] as const) {
      expect(shouldRetry(method, failure, 1)).toBe(false);
    }
  });
});

describe("backoffMs", () => {
  it("waits 200 ms after the first attempt and 500 ms after the second", () => {
    expect(backoffMs(1)).toBe(200);
    expect(backoffMs(2)).toBe(500);
  });
});

describe("fetchWithPolicy", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("retries a GET that gets 503 twice and returns the 200, waiting 200 ms and then 500 ms", async () => {
    const { fake } = scriptedFetch([503, 503, 200]);

    const outcome = start(get());
    await vi.advanceTimersByTimeAsync(0);
    expect(fake).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(199);
    expect(fake).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(499);
    expect(fake).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake).toHaveBeenCalledTimes(3);

    const result = await outcome;
    expect("response" in result && result.response.status).toBe(200);
  });

  it("gives up after three attempts and returns the last 503 so the caller can map it", async () => {
    const { fake } = scriptedFetch([503]);

    const result = await settled(start(get()));

    expect(fake).toHaveBeenCalledTimes(3);
    expect("response" in result && result.response.status).toBe(503);
  });

  it.each([502, 503, 504])("retries a GET on %i", async (status) => {
    const { fake } = scriptedFetch([status, 200]);

    const result = await settled(start(get()));

    expect(fake).toHaveBeenCalledTimes(2);
    expect("response" in result && result.response.status).toBe(200);
  });

  it("retries a GET after a network error, and rethrows the error when all three attempts fail", async () => {
    const recovered = scriptedFetch(["network", 200]);
    const first = await settled(start(get()));
    expect(recovered.fake).toHaveBeenCalledTimes(2);
    expect("response" in first && first.response.status).toBe(200);

    const failing = scriptedFetch(["network"]);
    const second = await settled(start(get()));
    expect(failing.fake).toHaveBeenCalledTimes(3);
    expect("error" in second && second.error).toBeInstanceOf(TypeError);
  });

  it("times a GET attempt out after 5 s, even when fetch ignores the abort signal, and retries it", async () => {
    const { fake, requests } = scriptedFetch(["hang", 200]);

    const outcome = start(get());
    await vi.advanceTimersByTimeAsync(4_999);
    expect(fake).toHaveBeenCalledTimes(1);
    expect(requests[0].signal.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(requests[0].signal.aborted).toBe(true);

    await vi.advanceTimersByTimeAsync(200);
    expect(fake).toHaveBeenCalledTimes(2);
    const result = await outcome;
    expect("response" in result && result.response.status).toBe(200);
  });

  it("rejects after three timed-out GET attempts", async () => {
    const { fake } = scriptedFetch(["hang"]);

    const result = await settled(start(get()));

    expect(fake).toHaveBeenCalledTimes(3);
    expect("error" in result).toBe(true);
  });

  it.each(["POST", "PATCH", "PUT", "DELETE"])("sends a %s once and returns whatever it gets, even a 503", async (method) => {
    const { fake } = scriptedFetch([503, 200]);

    const result = await settled(start(write(method)));

    expect(fake).toHaveBeenCalledTimes(1);
    expect("response" in result && result.response.status).toBe(503);
  });

  it.each(["POST", "PATCH", "PUT", "DELETE"])("does not retry a %s after a network error", async (method) => {
    const { fake } = scriptedFetch(["network", 200]);

    const result = await settled(start(write(method)));

    expect(fake).toHaveBeenCalledTimes(1);
    expect("error" in result && result.error).toBeInstanceOf(TypeError);
  });

  it("gives a write 10 s, not 5 s, and sends it once when it times out", async () => {
    const { fake, requests } = scriptedFetch(["hang"]);

    const outcome = start(write("POST"));
    await vi.advanceTimersByTimeAsync(5_001);
    expect(requests[0].signal.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(4_998);
    expect(requests[0].signal.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(requests[0].signal.aborted).toBe(true);

    const result = await settled(outcome);
    expect(fake).toHaveBeenCalledTimes(1);
    expect("error" in result).toBe(true);
  });

  it.each([400, 401, 404, 409, 500])("does not retry a GET that gets %i", async (status) => {
    const { fake } = scriptedFetch([status, 200]);

    const result = await settled(start(get()));

    expect(fake).toHaveBeenCalledTimes(1);
    expect("response" in result && result.response.status).toBe(status);
  });

  it("does not retry when the caller aborts mid-flight, and propagates the abort", async () => {
    const { fake } = scriptedFetch(["hang"]);
    const controller = new AbortController();

    const outcome = start(get(controller.signal));
    await vi.advanceTimersByTimeAsync(1_000);
    controller.abort();
    const result = await settled(outcome);

    expect(fake).toHaveBeenCalledTimes(1);
    expect("error" in result).toBe(true);
  });

  it("does not retry when the caller aborts during a backoff", async () => {
    const { fake } = scriptedFetch([503, 200]);
    const controller = new AbortController();

    const outcome = start(get(controller.signal));
    await vi.advanceTimersByTimeAsync(100);
    controller.abort();
    const result = await settled(outcome);

    expect(fake).toHaveBeenCalledTimes(1);
    expect("error" in result).toBe(true);
  });

  it("does not start at all when the caller has already aborted", async () => {
    const { fake } = scriptedFetch([200]);
    const controller = new AbortController();
    controller.abort();

    const result = await settled(start(get(controller.signal)));

    expect(fake).not.toHaveBeenCalled();
    expect("error" in result).toBe(true);
  });

  it("puts the same traceparent on every attempt", async () => {
    const { requests } = scriptedFetch([503, 503, 200]);

    const result = await runWithRequestContext({ traceId: TRACE_ID, spanId: SPAN_ID, nonce: "" }, async () => settled(start(get())));

    expect("response" in result && result.response.status).toBe(200);
    expect(requests).toHaveLength(3);
    for (const request of requests) {
      expect(request.headers.get("traceparent")).toBe(`00-${TRACE_ID}-${SPAN_ID}-01`);
      expect(request.headers.has("x-correlation-id")).toBe(false);
    }
  });

  it("keeps the method, URL, headers and body of the original request", async () => {
    const { requests } = scriptedFetch([200]);
    const original = new Request("https://api.test/api/v1/postings/1/transition", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: "Bearer fictional-token" },
      body: JSON.stringify({ toDisposition: "scored" }),
    });

    await settled(start(original));

    expect(requests[0].method).toBe("POST");
    expect(requests[0].url).toBe("https://api.test/api/v1/postings/1/transition");
    expect(requests[0].headers.get("authorization")).toBe("Bearer fictional-token");
    expect(await requests[0].json()).toEqual({ toDisposition: "scored" });
  });
});

describe("the insights loaders go through the policy and keep their 5 s bound", () => {
  const session = { accessToken: "fictional-token", sessionId: "session-policy" };
  const request = new Request("https://localhost:5003/insights");
  const ctx: LensContext = { window: "all", asOf: "2026-09-30" };
  const stats: InsightsStatsDto = {
    asOf: "2026-09-30",
    window: "allTime",
    windowStart: null,
    responseTime: { medianDays: 10, sampleSize: 14 },
    weeklyApplications: [{ weekStart: "2026-09-28", count: 1 }],
    searchRunYield: { runs: [], truncated: false },
  };

  beforeEach(() => {
    vi.useFakeTimers();
    process.env.ROLERADAR_API_BASE_URL = "https://api.test";
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    delete process.env.ROLERADAR_API_BASE_URL;
  });

  function countsFetch(plan: (callNumber: number) => "ok" | 503 | "hang") {
    let calls = 0;
    const fake = vi.fn((): Promise<Response> => {
      calls += 1;
      const step = plan(calls);
      if (step === "hang") {
        return new Promise<Response>(() => undefined);
      }
      if (step === 503) {
        return Promise.resolve(new Response("{}", { status: 503 }));
      }
      return Promise.resolve(new Response(JSON.stringify({ value: [], "@odata.count": 5 }), { status: 200, headers: { "Content-Type": "application/json" } }));
    });
    vi.stubGlobal("fetch", fake);
    return fake;
  }

  function allCounts(counts: Awaited<ReturnType<typeof loadInsightsCounts>>): unknown[] {
    return [...Object.values(counts.lens), ...Object.values(counts.coverage)];
  }

  it("a count that gets a 503 is retried and still shows its number", async () => {
    const fake = countsFetch((call) => (call === 1 ? 503 : "ok"));

    const pending = loadInsightsCounts({ request, session, ctx });
    await vi.advanceTimersByTimeAsync(1_000);
    const counts = await pending;

    expect(fake).toHaveBeenCalledTimes(21);
    expect(allCounts(counts)).toHaveLength(20);
    expect(allCounts(counts).every((value) => value === 5)).toBe(true);
  });

  it("a count that never answers is cut off at 5 s, is not retried, and only its own number is unavailable", async () => {
    const fake = countsFetch((call) => (call === 1 ? "hang" : "ok"));

    const pending = loadInsightsCounts({ request, session, ctx });
    await vi.advanceTimersByTimeAsync(4_900);
    let done = false;
    void pending.then(() => {
      done = true;
    });
    await vi.advanceTimersByTimeAsync(0);
    expect(done).toBe(false);

    await vi.advanceTimersByTimeAsync(300);
    const counts = await pending;

    expect(fake).toHaveBeenCalledTimes(20);
    expect(allCounts(counts).filter((value) => value === null)).toHaveLength(1);
    expect(allCounts(counts).filter((value) => value === 5)).toHaveLength(19);
  });

  it("the stats request that gets a 503 is retried through the typed client", async () => {
    let calls = 0;
    const fake = vi.fn((): Promise<Response> => {
      calls += 1;
      return Promise.resolve(
        calls === 1
          ? new Response("{}", { status: 503 })
          : new Response(JSON.stringify(stats), { status: 200, headers: { "Content-Type": "application/json" } })
      );
    });
    vi.stubGlobal("fetch", fake);

    const pending = loadInsightsStats({ request, session, ctx });
    await vi.advanceTimersByTimeAsync(1_000);
    const result = await pending;

    expect(fake).toHaveBeenCalledTimes(2);
    expect(result.state).toBe("ok");
  });

  it("the stats request that never answers is cut off at 5 s and is not retried", async () => {
    const fake = vi.fn((): Promise<Response> => new Promise<Response>(() => undefined));
    vi.stubGlobal("fetch", fake);

    const pending = loadInsightsStats({ request, session, ctx });
    await vi.advanceTimersByTimeAsync(5_200);
    const result = await pending;
    await vi.advanceTimersByTimeAsync(5_000);

    expect(result.state).toBe("unavailable");
    expect(fake).toHaveBeenCalledTimes(1);
  });
});
