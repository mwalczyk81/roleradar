import ky from "ky";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fetchWithPolicy } from "../../app/lib/api-client.server";
import { log } from "../../app/lib/log.server";
import { RETRY_AFTER_BOUND_MS, retryAfterMs } from "../../app/lib/observability";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 014 G3 (US3, FR-042; contracts/web-outbound-policy.md): how the web's outbound policy treats Retry-After on a
// 503. A value within the bound (2000 ms) is honoured; a larger one means no retry, the 503 comes back at once. The API
// always sends `Retry-After: 5`, so a real 503 from the API is not retried by the web. A 503 without the header still
// retries. A fake fetch and fake timers stand in for the network and the clock.

const URL_UNDER_TEST = "https://api.test/odata/Postings?$top=1";
const TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const SPAN_ID = "00f067aa0ba902b7";

function stubFetch(plan: (call: number) => Response) {
  let calls = 0;
  const fake = vi.fn((): Promise<Response> => {
    calls += 1;
    return Promise.resolve(plan(calls));
  });
  vi.stubGlobal("fetch", fake);
  return fake;
}

function unavailable(retryAfter?: string): Response {
  const headers: Record<string, string> = { "Content-Type": "application/problem+json" };
  if (retryAfter !== undefined) {
    headers["Retry-After"] = retryAfter;
  }
  return new Response(JSON.stringify({ status: 503 }), { status: 503, headers });
}

function ok(): Response {
  return new Response(JSON.stringify({ value: [] }), { status: 200, headers: { "Content-Type": "application/json" } });
}

function get(): Request {
  return new Request(URL_UNDER_TEST, { method: "GET" });
}

/** Starts the call and records its result without awaiting it, so a test can step the fake clock. */
function track(request: Request): { result: () => Response | undefined; done: Promise<Response> } {
  let response: Response | undefined;
  const done = fetchWithPolicy(request).then((value) => {
    response = value;
    return value;
  });
  return { result: () => response, done };
}

describe("Retry-After on a 503 (the first red test of G3)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("(a) fetchWithPolicy makes exactly one attempt for a GET answered 503 with Retry-After: 5 and returns the 503 at once", async () => {
    const fake = stubFetch(() => unavailable("5"));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(0);

    expect(call.result()?.status).toBe(503);
    expect(fake).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("(b) characterization of ky 2.2.0: maxRetryAfter CLAMPS the wait instead of refusing the retry", async () => {
    // The pinned version's behavior, so that a future ky that changes it fails loudly here. With a 2000 ms cap and
    // Retry-After: 5, ky still retries (3 attempts in all) and waits the capped 2 s each time, which is why the policy
    // needs a retry.shouldRetry veto of its own. The result recorded in tasks.md Notes (T064).
    const started = Date.now();
    const callTimes: number[] = [];
    const fake = stubFetch(() => {
      callTimes.push(Date.now() - started);
      return unavailable("5");
    });

    const pending = ky(new Request(URL_UNDER_TEST), {
      retry: { limit: 2, methods: ["get"], statusCodes: [503], maxRetryAfter: 2000 },
      fetch: (input, init) => globalThis.fetch(input, init),
    }).then(
      () => "resolved" as const,
      () => "rejected" as const
    );
    await vi.advanceTimersByTimeAsync(10_000);
    await pending;

    expect(fake).toHaveBeenCalledTimes(3);
    expect(callTimes).toEqual([0, 2000, 4000]);
  });
});

describe("Retry-After within and beyond the bound", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("names the bound as 2000 ms", () => {
    expect(RETRY_AFTER_BOUND_MS).toBe(2000);
  });

  it("a 503 with Retry-After: 1 waits about one second and then succeeds", async () => {
    const fake = stubFetch((call) => (call === 1 ? unavailable("1") : ok()));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(999);
    expect(fake).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(0);

    expect(call.result()?.status).toBe(200);
  });

  it("a 503 with Retry-After: 2 (exactly the bound) is honoured", async () => {
    const fake = stubFetch((call) => (call === 1 ? unavailable("2") : ok()));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(2_000);
    const response = await call.done;

    expect(fake).toHaveBeenCalledTimes(2);
    expect(response.status).toBe(200);
  });

  it("an HTTP-date Retry-After within the bound is honoured", async () => {
    const date = new Date(Date.now() + 1_000).toUTCString();
    const fake = stubFetch((call) => (call === 1 ? unavailable(date) : ok()));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(1_100);
    const response = await call.done;

    expect(fake).toHaveBeenCalledTimes(2);
    expect(response.status).toBe(200);
  });

  it("an HTTP-date Retry-After beyond the bound is not retried", async () => {
    const date = new Date(Date.now() + 30_000).toUTCString();
    const fake = stubFetch(() => unavailable(date));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(0);

    expect(call.result()?.status).toBe(503);
    expect(fake).toHaveBeenCalledTimes(1);
  });

  it("an HTTP-date in the past retries after the normal backoff", async () => {
    const date = new Date(Date.now() - 60_000).toUTCString();
    const fake = stubFetch((call) => (call === 1 ? unavailable(date) : ok()));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(1_000);
    const response = await call.done;

    expect(fake).toHaveBeenCalledTimes(2);
    expect(response.status).toBe(200);
  });

  it("a 503 without Retry-After retries three times in all, after 200 ms and then 500 ms", async () => {
    const fake = stubFetch(() => unavailable());

    const call = track(get());
    await vi.advanceTimersByTimeAsync(199);
    expect(fake).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(499);
    expect(fake).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(fake).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(10_000);

    expect(fake).toHaveBeenCalledTimes(3);
    expect(call.result()?.status).toBe(503);
  });

  it.each([502, 504])("a %i retries even when it carries a long Retry-After (only a 503 asks)", async (status) => {
    const fake = stubFetch((call) => (call === 1 ? new Response("{}", { status, headers: { "Retry-After": "5" } }) : ok()));

    const call = track(get());
    await vi.advanceTimersByTimeAsync(1_000);
    const response = await call.done;

    expect(fake).toHaveBeenCalledTimes(2);
    expect(response.status).toBe(200);
  });

  it("a write that gets a 503 with Retry-After is sent once", async () => {
    const fake = stubFetch(() => unavailable("1"));

    const call = track(new Request("https://api.test/api/v1/postings/1/transition", { method: "POST", body: "{}" }));
    await vi.advanceTimersByTimeAsync(5_000);
    const response = await call.done;

    expect(fake).toHaveBeenCalledTimes(1);
    expect(response.status).toBe(503);
  });

  it("the 503 warn record reports the attempt count, and one attempt when the retry was refused", async () => {
    const warn = vi.spyOn(log, "warn").mockImplementation(() => undefined);

    stubFetch(() => unavailable());
    const retried = track(get());
    await vi.advanceTimersByTimeAsync(5_000);
    await retried.done;

    stubFetch(() => unavailable("5"));
    const refused = track(get());
    await vi.advanceTimersByTimeAsync(5_000);
    await refused.done;

    const attempts = warn.mock.calls.map(([, fields]) => (fields as { props?: { attempts?: number } } | undefined)?.props?.attempts);
    expect(attempts).toEqual([3, 1]);
  });

  it("reads globalThis.fetch on every call, so a stub installed later is the one used", async () => {
    const first = stubFetch(() => ok());
    await fetchWithPolicy(get());
    const second = stubFetch(() => ok());
    await fetchWithPolicy(get());

    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);
  });

  it("carries one traceparent across a retried read that waited for Retry-After", async () => {
    const seen: Array<string | null> = [];
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
        seen.push(new Request(input, init).headers.get("traceparent"));
        return Promise.resolve(seen.length === 1 ? unavailable("1") : ok());
      })
    );

    const call = runWithRequestContext({ traceId: TRACE_ID, spanId: SPAN_ID, nonce: "" }, () => track(get()));
    await vi.advanceTimersByTimeAsync(1_000);
    await call.done;

    expect(seen).toEqual([`00-${TRACE_ID}-${SPAN_ID}-01`, `00-${TRACE_ID}-${SPAN_ID}-01`]);
  });
});

describe("retryAfterMs (the pure reader of the header)", () => {
  const now = Date.parse("2026-11-10T12:00:00Z");

  it.each([
    ["5", 5_000],
    ["0", 0],
    ["1", 1_000],
    ["  2  ", 2_000],
    ["Tue, 10 Nov 2026 12:00:03 GMT", 3_000],
    ["Tue, 10 Nov 2026 11:59:00 GMT", 0],
  ])("reads %j as %i ms", (header, expected) => {
    expect(retryAfterMs(header, now)).toBe(expected);
  });

  it.each([null, "", "soon", "-1", "1.5", "1e3", "NaN"])("reads %j as no usable value", (header) => {
    expect(retryAfterMs(header, now)).toBeUndefined();
  });
});
