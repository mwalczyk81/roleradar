import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fetchOData } from "../../app/lib/api-client.server";
import { loadODataList } from "../../app/lib/odata-list.server";
import { SERVICE_UNAVAILABLE_MESSAGE } from "../../app/lib/observability";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 013 US3 (FR-024), restated for feature 014 (RFC 9457): when the API answers 503 service_unavailable the web
// shows a friendly, retryable message of its own, never the problem document's text or an error's text, and writes one
// warn record that carries the problem document's traceId so the owner can find the API's side of it.

const RAW_TEXT = "MongoConnectionException: connect failed at db.fictional.invalid:27017";
const API_ID = "0af7651916cd43dd8448eb211c80319c";
const OWN_ID = "4bf92f3577b34da6a3ce929d0e0e4736";

function envelope(traceId?: string) {
  return { type: "https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#service_unavailable", title: "Service Unavailable", status: 503, detail: RAW_TEXT, errorCode: "service_unavailable", ...(traceId ? { traceId } : {}) };
}

function stubUnavailable(body: unknown) {
  const fake = vi.fn((): Promise<Response> =>
    Promise.resolve(new Response(typeof body === "string" ? body : JSON.stringify(body), { status: 503, headers: { "Content-Type": "application/problem+json" } }))
  );
  vi.stubGlobal("fetch", fake);
  return fake;
}

function captureLogs() {
  const lines: string[] = [];
  const sink = (chunk: string | Uint8Array) => (lines.push(String(chunk)), true);
  vi.spyOn(process.stdout, "write").mockImplementation(sink);
  vi.spyOn(process.stderr, "write").mockImplementation(sink);
  return lines;
}

function records(lines: string[]): Record<string, unknown>[] {
  return lines
    .join("")
    .split("\n")
    .filter((line) => line.startsWith("{"))
    .map((line): Record<string, unknown> => JSON.parse(line));
}

describe("a 503 service_unavailable envelope from the API", () => {
  const original = { ...process.env };

  beforeEach(() => {
    vi.useFakeTimers();
    process.env = { ...original, ROLERADAR_API_BASE_URL: "https://api.test", ROLERADAR_LOG_FORMAT: "json", ROLERADAR_LOG_LEVEL: "info" };
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    process.env = { ...original };
  });

  async function settle<T>(promise: Promise<T>): Promise<T> {
    await vi.advanceTimersByTimeAsync(5_000);
    return promise;
  }

  it("is the same friendly message in fetchOData, whatever text the envelope carries", async () => {
    const fake = stubUnavailable(envelope(API_ID));

    const result = await settle(fetchOData<unknown>("/odata/Postings", "$top=1", "fictional-token"));

    expect(result.error?.status).toBe(503);
    expect(result.error?.message).toBe(SERVICE_UNAVAILABLE_MESSAGE);
    expect(result.error?.message).not.toContain("Mongo");
    expect(fake).toHaveBeenCalledTimes(3);
  });

  it("is also friendly when the 503 has no envelope at all", async () => {
    stubUnavailable("upstream connect error");

    const result = await settle(fetchOData<unknown>("/odata/Postings", "", "fictional-token"));

    expect(result.error?.status).toBe(503);
    expect(result.error?.message).toBe(SERVICE_UNAVAILABLE_MESSAGE);
  });

  it("is a 503 Response with the friendly text from loadODataList, never the envelope's text", async () => {
    stubUnavailable(envelope(API_ID));

    const thrown = await settle(
      loadODataList<unknown>({
        path: "/odata/Postings",
        queryString: "$top=1",
        request: new Request("https://localhost:5003/postings"),
        session: { accessToken: "fictional-token", sessionId: "session-503" },
        failureMessage: "Failed to load postings",
      }).then(
        () => null,
        (error: unknown) => error
      )
    );

    expect(thrown).toBeInstanceOf(Response);
    if (!(thrown instanceof Response)) {
      return;
    }
    expect(thrown.status).toBe(503);
    expect(await thrown.text()).toBe(SERVICE_UNAVAILABLE_MESSAGE);
  });

  it("writes one warn record carrying the problem document's traceId, and nothing of its text", async () => {
    stubUnavailable(envelope(API_ID));
    const lines = captureLogs();

    await runWithRequestContext({ traceId: OWN_ID, spanId: "00f067aa0ba902b7", nonce: "" }, async () =>
      settle(fetchOData<unknown>("/odata/Postings", "$top=1&$filter=title eq 'x'", "fictional-token"))
    );

    const unavailable = records(lines).filter((r) => r.event === "ApiServiceUnavailable");
    expect(unavailable).toHaveLength(1);
    expect(unavailable[0].level).toBe("warn");
    expect(unavailable[0].traceId).toBe(API_ID);
    expect(unavailable[0].props).toMatchObject({ method: "GET", path: "/odata/Postings", status: 503, attempts: 3 });
    const text = lines.join("");
    expect(text).not.toContain("Mongo");
    expect(text).not.toContain("fictional.invalid");
    expect(text).not.toContain("filter");
    expect(text).not.toContain("fictional-token");
  });

  it("falls back to the request's own trace id when the problem document has none", async () => {
    stubUnavailable("upstream connect error");
    const lines = captureLogs();

    await runWithRequestContext({ traceId: OWN_ID, spanId: "00f067aa0ba902b7", nonce: "" }, async () =>
      settle(fetchOData<unknown>("/odata/Postings", "", "fictional-token"))
    );

    const unavailable = records(lines).filter((r) => r.event === "ApiServiceUnavailable");
    expect(unavailable).toHaveLength(1);
    expect(unavailable[0].traceId).toBe(OWN_ID);
  });

  it("is not logged as unavailable when a retry succeeds", async () => {
    let calls = 0;
    vi.stubGlobal("fetch", () => {
      calls += 1;
      return Promise.resolve(
        calls === 1
          ? new Response(JSON.stringify(envelope(API_ID)), { status: 503 })
          : new Response(JSON.stringify({ value: [] }), { status: 200, headers: { "Content-Type": "application/json" } })
      );
    });
    const lines = captureLogs();

    const result = await settle(fetchOData<unknown>("/odata/Postings", "", "fictional-token"));

    expect(result.error).toBeUndefined();
    expect(records(lines).filter((r) => r.event === "ApiServiceUnavailable")).toHaveLength(0);
  });
});
