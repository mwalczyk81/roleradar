import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as applicationsListLoader } from "../../app/routes/applications._index.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("Applications OData List Loader (T038)", () => {
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
      ownerSub: "owner-test-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token-apps",
      refreshToken: "mock-refresh-token-apps",
      idToken: "mock-id-token-apps",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-apps",
    };
    saveSession(session);
    return { sessionId, session };
  }

  it("passes exact filter parameters and server sorting to /odata/Applications", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: {
        value: [
          {
            id: "app-1",
            postingId: "post-1",
            companyId: "comp-1",
            postingTitle: "Staff Engineer",
            companyName: "Acme Corp",
            status: "applied",
            appliedAt: "2026-10-31T00:00:00Z",
            hasInterview: true,
            firstResponseAt: "2026-11-01T00:00:00Z",
            lastEventAt: "2026-11-01T00:00:00Z",
            ageInDays: 2,
          },
        ],
        "@odata.count": 1,
      },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const url = "https://localhost:5003/applications?status=applied&limit=50&offset=10&sort=postingTitle&direction=asc";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(applicationsListLoader, request);

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [path, queryString, token] = mockFetchOData.mock.calls[0];
    expect(path).toBe("/odata/Applications");
    expect(token).toBe("mock-access-token-apps");

    const decodedQuery = decodeURIComponent(queryString);
    expect(queryString).toContain("$filter=");
    expect(decodedQuery).toContain("status eq 'applied'");
    expect(queryString).toContain("$top=50");
    expect(queryString).toContain("$skip=10");
    expect(queryString).toContain("$count=true");
    expect(decodedQuery).toContain("$orderby=postingTitle asc");

    expect(result.items).toHaveLength(1);
    expect(result.items[0].postingTitle).toBe("Staff Engineer");
    expect(result.items[0].companyName).toBe("Acme Corp");
    expect(result.totalCount).toBe(1);
  });

  it("defaults to appliedAt desc order when URL has no sort parameter", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: {
        value: [],
        "@odata.count": 0,
      },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const url = "https://localhost:5003/applications";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    await callLoader(applicationsListLoader, request);

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    const decodedQuery = decodeURIComponent(queryString);
    expect(decodedQuery).toContain("$orderby=appliedAt desc");
  });

  it("clamps limit to 1..100 and offset to >= 0", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const url = "https://localhost:5003/applications?limit=500&offset=-10";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    await callLoader(applicationsListLoader, request);

    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    expect(queryString).toContain("$top=100");
    expect(queryString).toContain("$skip=0");
  });

  it("handles 401 from API by re-challenging authentication", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: undefined,
      error: "Unauthorized",
      response: { status: 401 } as Response,
    });

    const url = "https://localhost:5003/applications";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(applicationsListLoader, request);
    } catch (err) {
      if (err instanceof Response) {
        thrown = err;
      }
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toContain("/connect/authorize");
  });

  it("handles 403 Forbidden by redirecting to /not-authorized", async () => {
    const { sessionId } = createTestSession();

    mockFetchOData.mockResolvedValueOnce({
      data: undefined,
      error: "Forbidden",
      response: { status: 403 } as Response,
    });

    const url = "https://localhost:5003/applications";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(applicationsListLoader, request);
    } catch (err) {
      if (err instanceof Response) {
        thrown = err;
      }
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  // Helper shared by the branch-coverage tests below: runs the loader against a fresh session and
  // returns both the OData query string that was sent and the loader result.
  async function runLoader(urlSuffix: string, odataBody: unknown = { value: [], "@odata.count": 0 }) {
    const { sessionId } = createTestSession();
    mockFetchOData.mockResolvedValueOnce({
      data: odataBody,
      error: undefined,
      response: { status: 200 } as Response,
    });
    const request = new Request(`https://localhost:5003/applications${urlSuffix}`, {
      headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
    });
    const result = await callLoader(applicationsListLoader, request);
    const [, queryString] = mockFetchOData.mock.calls[0];
    return { result, decodedQuery: decodeURIComponent(queryString as string) };
  }

  it("turns the q search term into a case-insensitive contains filter on title or company", async () => {
    const { decodedQuery } = await runLoader("?q=Platform");

    expect(decodedQuery).toContain("contains(tolower(postingTitle),'platform')");
    expect(decodedQuery).toContain("contains(tolower(companyName),'platform')");
    expect(decodedQuery).toContain(" or ");
  });

  it("ANDs the status filter with the q search when both are present", async () => {
    const { decodedQuery } = await runLoader("?status=rejected&q=cloud");

    expect(decodedQuery).toContain("status eq 'rejected'");
    expect(decodedQuery).toContain("contains(tolower(postingTitle),'cloud')");
    expect(decodedQuery).toContain(" and ");
  });

  it("ignores a blank q and sends no $filter", async () => {
    const { decodedQuery } = await runLoader("?q=%20%20");

    expect(decodedQuery).not.toContain("$filter");
  });

  it("ignores an unknown status value instead of sending it to the API", async () => {
    const { decodedQuery, result } = await runLoader("?status=hacked");

    expect(decodedQuery).not.toContain("$filter");
    expect(result.statusFilter).toBe("");
  });

  it("ignores a sort field that is not on the allowlist and keeps the default order", async () => {
    const { decodedQuery, result } = await runLoader("?sort=ownerId&direction=asc");

    expect(decodedQuery).toContain("$orderby=appliedAt desc");
    expect(decodedQuery).not.toContain("ownerId");
    expect(result.sort).toBeUndefined();
  });

  it("applies an allowlisted sort with the requested direction, and defaults the direction to desc", async () => {
    const asc = await runLoader("?sort=lastEventAt&direction=asc");
    expect(asc.decodedQuery).toContain("$orderby=lastEventAt asc");
    expect(asc.result.direction).toBe("asc");

    mockFetchOData.mockClear();
    const desc = await runLoader("?sort=firstResponseAt&direction=sideways");
    expect(desc.decodedQuery).toContain("$orderby=firstResponseAt desc");
    expect(desc.result.direction).toBe("desc");
  });

  it("falls back to the default page size when limit is not a number or not positive", async () => {
    const notNumber = await runLoader("?limit=abc&offset=xyz");
    expect(notNumber.decodedQuery).toContain("$top=25");
    expect(notNumber.decodedQuery).toContain("$skip=0");
    expect(notNumber.result.limit).toBe(25);

    mockFetchOData.mockClear();
    const zero = await runLoader("?limit=0");
    expect(zero.decodedQuery).toContain("$top=25");
  });

  it("falls back to the row count when the response has no @odata.count", async () => {
    const { result } = await runLoader("", { value: [{ id: "a" }, { id: "b" }] });

    expect(result.totalCount).toBe(2);
    expect(result.items).toHaveLength(2);
  });

  it("treats a response with neither value nor count as an empty page", async () => {
    const { result } = await runLoader("", {});

    expect(result.items).toEqual([]);
    expect(result.totalCount).toBe(0);
  });

  it("throws a Response carrying the API status and message for non-auth failures", async () => {
    const { sessionId } = createTestSession();
    mockFetchOData.mockResolvedValueOnce({
      data: undefined,
      error: { message: "OData exploded" },
      response: { status: 500 } as Response,
    });
    const request = new Request("https://localhost:5003/applications", {
      headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(applicationsListLoader, request);
    } catch (err) {
      if (err instanceof Response) {
        thrown = err;
      }
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(500);
    expect(await thrown?.text()).toBe("OData exploded");
  });
});
