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

describe("GET /postings loader (OData Migration T032)", () => {
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
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
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

  it("passes exact filter parameters and server sorting to /odata/Postings", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: {
        value: [
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
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          },
        ],
        "@odata.count": 1,
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

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [path, queryString, token] = mockFetchOData.mock.calls[0];
    expect(path).toBe("/odata/Postings");
    expect(token).toBe("mock-access-token-999");

    // Query string assertions
    const decodedQuery = decodeURIComponent(queryString);
    expect(queryString).toContain("$filter=");
    expect(decodedQuery).toContain("disposition eq 'scored'");
    expect(decodedQuery).toContain("companyId eq 'comp-1'");
    expect(decodedQuery).toContain("resurfacePolicy eq 'if_reopened'");
    expect(decodedQuery).toContain("score ge 80");
    expect(decodedQuery).toContain("contains(tolower(title),'engineer')");
    expect(decodedQuery).toContain("$orderby=score desc");
    expect(queryString).toContain("$top=25");
    expect(queryString).toContain("$skip=0");
    expect(queryString).toContain("$count=true");

    // Loader return data structure
    expect(result.items).toHaveLength(1);
    expect(result.items[0].title).toBe("Staff Engineer");
    expect(result.totalCount).toBe(1);
    expect(result.csrfToken).toBe("mock-csrf-token-555");
    expect(result.sort).toBe("score");
    expect(result.direction).toBe("desc");

    // Invariant: Never return tokens to browser
    const serialized = JSON.stringify(result);
    expect(serialized).not.toContain("mock-access-token-999");
    expect(serialized).not.toContain("mock-refresh-token-888");
    expect(serialized).not.toContain("mock-id-token-777");
  });

  it("clamps limit to 1..100, offset to >= 0, and ignores NaN minScore", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
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

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    expect(queryString).toContain("$top=100");
    expect(queryString).toContain("$skip=0");
    expect(queryString).not.toContain("score ge");
  });

  it("handles 403 Forbidden from API by throwing a redirect to /not-authorized without looping", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: undefined,
      error: { message: "Forbidden", status: 403 },
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
    } catch (e) {
      if (e instanceof Response) {
        thrown = e;
      }
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  it("handles 401 Unauthorized from API by destroying session and redirecting to login with returnTo", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: undefined,
      error: { message: "Unauthorized", status: 401 },
      response: { status: 401 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(postingsLoader, request);
    } catch (e) {
      if (e instanceof Response) {
        thrown = e;
      }
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toContain("/connect/authorize");
    expect(thrown?.headers.get("Set-Cookie")).toBeDefined();
  });

  it("sets userLabel to preferred_username when available in session", async () => {
    const { sessionId, session } = createTestSession();
    session.ownerUsername = "matt-engineer";
    saveSession(session);

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.userLabel).toBe("matt-engineer");
  });

  it("falls back to text 'Signed in' and never reveals sub GUID when preferred_username is absent", async () => {
    const { sessionId, session } = createTestSession();
    delete session.ownerUsername;
    saveSession(session);

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.userLabel).toBe("Signed in");
  });

  it("defaults to score desc ordering when no sort parameter is provided", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.sort).toBeUndefined();

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    expect(decodeURIComponent(queryString)).toContain("$orderby=score desc");
  });

  it("falls back to default score desc ordering when unallowlisted sort is provided", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings?sort=unallowlisted_column", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingsLoader, request);
    expect(result.sort).toBeUndefined();

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    expect(decodeURIComponent(queryString)).toContain("$orderby=score desc");
  });

  it("ignores invalid disposition and resurfacePolicy parameters with type guards", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings?disposition=hacked_disp&resurfacePolicy=bad_policy", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    await callLoader(postingsLoader, request);

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    expect(decodeURIComponent(queryString)).not.toContain("disposition eq");
    expect(decodeURIComponent(queryString)).not.toContain("resurfacePolicy eq");
  });
});
