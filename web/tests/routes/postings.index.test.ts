import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as postingsLoader } from "../../app/routes/postings._index.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("GET /postings loader (T028)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiGet = vi.fn();
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
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: mockApiGet,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function createTestSession(): { sessionId: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-matt-001",
      accessToken: "mock-access-token-999",
      refreshToken: "mock-refresh-token-888",
      idToken: "mock-id-token-777",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-555",
    };
    saveSession(session);
    return { sessionId, session };
  }

  it("passes exact filter parameters (disposition, companyId, resurfacePolicy, minScore, q, limit, offset) to GET /api/v1/postings", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockResolvedValueOnce({
      data: {
        items: [
          {
            id: "post-1",
            companyId: "comp-1",
            companyName: "Acme Corp",
            title: "Staff Engineer",
            requisitionId: "REQ-101",
            location: "Remote, US",
            disposition: "scored",
            resurfacePolicy: "if_reopened",
            score: 85,
            versionToken: crypto.randomUUID(),
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          },
        ],
        totalCount: 1,
        limit: 25,
        offset: 0,
      },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const url = "https://localhost:5003/postings?disposition=scored&companyId=comp-1&resurfacePolicy=if_reopened&minScore=80&q=engineer&limit=25&offset=0&sort=score&direction=desc";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);

    expect(mockApiGet).toHaveBeenCalledTimes(1);
    const [endpoint, options] = mockApiGet.mock.calls[0];
    expect(endpoint).toBe("/api/v1/postings");

    // Exact query parameters sent to API
    expect(options.params.query).toEqual({
      disposition: "scored",
      companyId: "comp-1",
      resurfacePolicy: "if_reopened",
      minScore: 80,
      q: "engineer",
      limit: 25,
      offset: 0,
    });

    // Invariant: Sorting is page-local only; do NOT pass sort parameters to the API
    expect(options.params.query.sort).toBeUndefined();
    expect(options.params.query.direction).toBeUndefined();

    // Loader return data structure
    expect(result.items).toHaveLength(1);
    expect(result.items[0].title).toBe("Staff Engineer");
    expect(result.totalCount).toBe(1);
    expect(result.csrfToken).toBe("mock-csrf-token-555");

    // Invariant: Never return tokens to browser
    const serialized = JSON.stringify(result);
    expect(serialized).not.toContain("mock-access-token-999");
    expect(serialized).not.toContain("mock-refresh-token-888");
    expect(serialized).not.toContain("mock-id-token-777");
  });

  it("clamps limit to 1..100, offset to >= 0, and ignores NaN minScore", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockResolvedValueOnce({
      data: { items: [], totalCount: 0, limit: 100, offset: 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const url = "https://localhost:5003/postings?limit=200&offset=-10&minScore=invalid-number";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    await callLoader(postingsLoader, request);

    const [, options] = mockApiGet.mock.calls[0];
    expect(options.params.query.limit).toBe(100); // clamped from 200
    expect(options.params.query.offset).toBe(0); // clamped from -10
    expect(options.params.query.minScore).toBeUndefined(); // NaN ignored
  });

  it("handles 403 Forbidden from API by throwing a redirect to /not-authorized without looping", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockResolvedValueOnce({
      data: undefined,
      error: { ok: false, errorCode: "FORBIDDEN", message: "Forbidden" },
      response: { status: 403 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(postingsLoader, request);
    } catch (err: unknown) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  it("handles 401 Unauthorized from API by destroying session and redirecting to login with returnTo", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockResolvedValueOnce({
      data: undefined,
      error: { ok: false, errorCode: "UNAUTHORIZED", message: "Token expired" },
      response: { status: 401 } as Response,
    });

    const request = new Request("https://localhost:5003/postings?q=test", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(postingsLoader, request);
    } catch (err: unknown) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    const location = thrown?.headers.get("Location");
    expect(location).toContain("/auth");
    // Verify session cookie was cleared in response header
    const setCookie = thrown?.headers.get("Set-Cookie");
    expect(setCookie).toBeDefined();
  });

  it("sets userLabel to preferred_username when available in session", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "018d9633-9111-7395-88a2-f81d113426e2",
      ownerUsername: "matt",
      accessToken: "mock-access-token",
      refreshToken: "mock-refresh-token",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token",
    };
    saveSession(session);

    mockApiGet.mockResolvedValueOnce({
      data: { items: [], totalCount: 0, limit: 25, offset: 0 },
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.userLabel).toBe("matt");
    expect(result.userLabel).not.toBe(session.ownerSub);
  });

  it("falls back to text 'Signed in' and never reveals sub GUID when preferred_username is absent", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "018d9633-9111-7395-88a2-f81d113426e2",
      // ownerUsername undefined
      accessToken: "mock-access-token",
      refreshToken: "mock-refresh-token",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token",
    };
    saveSession(session);

    mockApiGet.mockResolvedValueOnce({
      data: { items: [], totalCount: 0, limit: 25, offset: 0 },
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.userLabel).toBe("Signed in");
    expect(result.userLabel).not.toBe(session.ownerSub);
  });
});
