import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createApiClient, fetchOData } from "../../app/lib/api-client.server";
import { exchangeCodeForTokens, getOidcConfig, refreshTokens, setOidcConfigForTesting } from "../../app/lib/oidc.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 013 US1 (FR-004): the web passes its request's X-Correlation-ID to the API and to Identity's server-side
// endpoints (discovery, code grant, refresh), and never logs the Authorization value.

const ID = "test-corr-web-0003";
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

describe("outbound correlation header", () => {
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

  it("createApiClient sends the request's id", async () => {
    const seen = stubFetch(() => json({ ok: false, errorCode: "not_found", message: "x", correlationId: ID }, 404));

    await runWithRequestContext({ correlationId: ID, nonce: "" }, async () => {
      const api = createApiClient("fictional-token");
      await api.GET("/api/v1/postings/{id}", { params: { path: { id: "0197a1b2-0000-7000-8000-000000000001" } } });
    });

    expect(seen).toHaveLength(1);
    expect(seen[0].headers.get("x-correlation-id")).toBe(ID);
    expect(seen[0].headers.get("authorization")).toBe("Bearer fictional-token");
  });

  it("fetchOData sends the request's id", async () => {
    const seen = stubFetch(() => json({ value: [] }));

    await runWithRequestContext({ correlationId: ID, nonce: "" }, () => fetchOData("/odata/Postings", "$top=1", "fictional-token"));

    expect(seen[0].headers.get("x-correlation-id")).toBe(ID);
  });

  it("sends no header outside a request", async () => {
    const seen = stubFetch(() => json({ value: [] }));

    await fetchOData("/odata/Postings", "", "fictional-token");

    expect(seen[0].headers.get("x-correlation-id")).toBeNull();
  });

  it("Identity discovery, code grant and refresh all carry the id", async () => {
    const seen = stubFetch(oidcResponder);

    await runWithRequestContext({ correlationId: ID, nonce: "" }, async () => {
      await getOidcConfig();
      await refreshTokens("fictional-refresh").catch(() => undefined);
      await exchangeCodeForTokens(new URL("https://localhost:5003/auth/callback?code=c&state=s"), "verifier", "s").catch(() => undefined);
    });

    const discovery = seen.filter((s) => s.url.endsWith("/.well-known/openid-configuration"));
    const token = seen.filter((s) => s.url === `${ISSUER}/connect/token`);
    expect(discovery.length).toBeGreaterThan(0);
    expect(token.length).toBeGreaterThanOrEqual(2);
    for (const call of [...discovery, ...token]) {
      expect(call.headers.get("x-correlation-id")).toBe(ID);
    }
  });

  it("never logs the Authorization value", async () => {
    const written: string[] = [];
    vi.spyOn(process.stdout, "write").mockImplementation((c: string | Uint8Array) => (written.push(String(c)), true));
    vi.spyOn(process.stderr, "write").mockImplementation((c: string | Uint8Array) => (written.push(String(c)), true));
    stubFetch(() => json({ ok: false, errorCode: "x", message: "x" }, 401));

    await runWithRequestContext({ correlationId: ID, nonce: "" }, async () => {
      await createApiClient("SENTINEL-AUTH-7f3a").GET("/api/v1/postings/{id}", { params: { path: { id: "x" } } });
      await fetchOData("/odata/Postings", "", "SENTINEL-AUTH-7f3a");
    });

    expect(written.join("")).not.toContain("SENTINEL-AUTH-7f3a");
  });
});
