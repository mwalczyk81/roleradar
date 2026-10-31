import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import { loader as postingsLoader } from "../../app/routes/postings._index.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("GET /postings loader (T028)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiGet = vi.fn();

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    vi.restoreAllMocks();
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: mockApiGet,
    } as any);
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
            disposition: "screened_in",
            resurfacePolicy: "manual_only",
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

    const url = "https://localhost:5003/postings?disposition=screened_in&companyId=comp-1&resurfacePolicy=manual_only&minScore=80&q=engineer&limit=25&offset=0&sort=score&direction=desc";
    const request = new Request(url, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await postingsLoader({ request, params: {}, context: {} } as any);

    expect(mockApiGet).toHaveBeenCalledTimes(1);
    const [endpoint, options] = mockApiGet.mock.calls[0];
    expect(endpoint).toBe("/api/v1/postings");

    // Exact query parameters sent to API
    expect(options.params.query).toEqual({
      disposition: "screened_in",
      companyId: "comp-1",
      resurfacePolicy: "manual_only",
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
      await postingsLoader({ request, params: {}, context: {} } as any);
    } catch (err: any) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });
});
