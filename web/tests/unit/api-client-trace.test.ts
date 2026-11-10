import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createApiClient, fetchOData } from "../../app/lib/api-client.server";
import { exchangeCodeForTokens, getOidcConfig, refreshTokens, setOidcConfigForTesting } from "../../app/lib/oidc.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 014 US2 (FR-023, FR-024): the web passes its request's W3C traceparent to the API and to Identity's
// server-side endpoints (discovery, code grant, refresh), never X-Correlation-ID, and never logs the Authorization value.

const TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const SPAN_ID = "00f067aa0ba902b7";
const TRACEPARENT = `00-${TRACE_ID}-${SPAN_ID}-01`;
const CONTEXT = { traceId: TRACE_ID, spanId: SPAN_ID, nonce: "" };
const ISSUER = "https://identity.test";

interface Seen {
  url: string;
  headers: Headers;
}

function stubFetch(respond: (url: string) => Response) {
  const seen: Seen[] = [];
  vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = new Request(input, init);
    seen.push({ url: request.url, headers: request.headers });
    return respond(request.url);
  });
  return seen;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function oidcResponder(url: string): Response {
  if (url.endsWith("/.well-known/openid-configuration")) {
    return json({
      issuer: ISSUER,
      authorization_endpoint: `${ISSUER}/connect/authorize`,
      token_endpoint: `${ISSUER}/connect/token`,
      end_session_endpoint: `${ISSUER}/connect/endsession`,
      jwks_uri: `${ISSUER}/.well-known/openid-configuration/jwks`,
      response_types_supported: ["code"],
      subject_types_supported: ["public"],
      id_token_signing_alg_values_supported: ["RS256"],
    });
  }
  return json({ access_token: "fictional-access", token_type: "Bearer", expires_in: 300 });
}

describe("outbound traceparent header", () => {
  const originalEnv = { ...process.env };

  beforeEach(() => {
    process.env = { ...originalEnv, NODE_ENV: "test", ROLERADAR_IDENTITY_ISSUER_URI: ISSUER, ROLERADAR_API_BASE_URL: "https://api.test" };
    setOidcConfigForTesting(null);
  });

  afterEach(() => {
    setOidcConfigForTesting(null);
    process.env = { ...originalEnv };
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("createApiClient sends the request's traceparent, and no X-Correlation-ID", async () => {
    const seen = stubFetch(() => json({ status: 404, errorCode: "not_found", detail: "x", traceId: TRACE_ID }, 404));

    await runWithRequestContext(CONTEXT, async () => {
      const api = createApiClient("fictional-token");
      await api.GET("/api/v1/postings/{id}", { params: { path: { id: "0197a1b2-0000-7000-8000-000000000001" } } });
    });

    expect(seen).toHaveLength(1);
    expect(seen[0].headers.get("traceparent")).toBe(TRACEPARENT);
    expect(seen[0].headers.has("x-correlation-id")).toBe(false);
    expect(seen[0].headers.get("authorization")).toBe("Bearer fictional-token");
  });

  it("fetchOData sends the request's traceparent", async () => {
    const seen = stubFetch(() => json({ value: [] }));

    await runWithRequestContext(CONTEXT, () => fetchOData("/odata/Postings", "$top=1", "fictional-token"));

    expect(seen[0].headers.get("traceparent")).toBe(TRACEPARENT);
  });

  it("sends no traceparent outside a request", async () => {
    const seen = stubFetch(() => json({ value: [] }));

    await fetchOData("/odata/Postings", "", "fictional-token");

    expect(seen[0].headers.get("traceparent")).toBeNull();
  });

  it("Identity discovery, code grant and refresh all carry the traceparent", async () => {
    const seen = stubFetch(oidcResponder);

    await runWithRequestContext(CONTEXT, async () => {
      await getOidcConfig();
      await refreshTokens("fictional-refresh").catch(() => undefined);
      await exchangeCodeForTokens(new URL("https://localhost:5003/auth/callback?code=c&state=s"), "verifier", "s").catch(() => undefined);
    });

    const discovery = seen.filter((s) => s.url.endsWith("/.well-known/openid-configuration"));
    const token = seen.filter((s) => s.url === `${ISSUER}/connect/token`);
    expect(discovery.length).toBeGreaterThan(0);
    expect(token.length).toBeGreaterThanOrEqual(2);
    for (const call of [...discovery, ...token]) {
      expect(call.headers.get("traceparent")).toBe(TRACEPARENT);
      expect(call.headers.has("tracestate")).toBe(false);
    }
  });

  it("the 503 warn record carries the traceId read from the problem document", async () => {
    const lines: string[] = [];
    vi.spyOn(process.stdout, "write").mockImplementation((c: string | Uint8Array) => (lines.push(String(c)), true));
    vi.spyOn(process.stderr, "write").mockImplementation((c: string | Uint8Array) => (lines.push(String(c)), true));
    process.env.ROLERADAR_LOG_FORMAT = "json";
    const apiTrace = "1111111111111111111111111111aaaa";
    stubFetch(() => json({ status: 503, errorCode: "service_unavailable", detail: "x", traceId: apiTrace }, 503));
    vi.useFakeTimers();

    try {
      const pending = runWithRequestContext(CONTEXT, async () => fetchOData("/odata/Postings", "", "fictional-token"));
      await vi.runAllTimersAsync();
      await pending;
    } finally {
      vi.useRealTimers();
    }

    const warn = lines.map((l) => JSON.parse(l) as Record<string, unknown>).filter((r) => r.event === "ApiServiceUnavailable");
    expect(warn).toHaveLength(1);
    expect(warn[0].traceId).toBe(apiTrace);
    expect("correlationId" in warn[0]).toBe(false);
  });

  it("never logs the Authorization value", async () => {
    const written: string[] = [];
    vi.spyOn(process.stdout, "write").mockImplementation((c: string | Uint8Array) => (written.push(String(c)), true));
    vi.spyOn(process.stderr, "write").mockImplementation((c: string | Uint8Array) => (written.push(String(c)), true));
    stubFetch(() => json({ status: 401, errorCode: "unauthorized" }, 401));

    await runWithRequestContext(CONTEXT, async () => {
      await createApiClient("SENTINEL-AUTH-7f3a").GET("/api/v1/postings/{id}", { params: { path: { id: "x" } } });
      await fetchOData("/odata/Postings", "", "SENTINEL-AUTH-7f3a");
    });

    expect(written.join("")).not.toContain("SENTINEL-AUTH-7f3a");
  });
});
