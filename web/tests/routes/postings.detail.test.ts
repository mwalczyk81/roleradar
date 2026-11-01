import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as postingDetailLoader } from "../../app/routes/postings.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("GET /postings/:id loader (T031)", () => {
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
    } as any);
  });

  function createTestSession(): { sessionId: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-matt-001",
      accessToken: "mock-access-token-111",
      refreshToken: "mock-refresh-token-222",
      idToken: "mock-id-token-333",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-444",
    };
    saveSession(session);
    return { sessionId, session };
  }

  it("fetches posting detail and criteria, mapping all PostingDetailDto fields and never leaking tokens", async () => {
    const { sessionId } = createTestSession();
    const postingId = crypto.randomUUID();
    const companyId = crypto.randomUUID();
    const criterionId = crypto.randomUUID();

    const mockPosting = {
      id: postingId,
      companyId,
      companyName: "Acme Innovations",
      title: "Lead Platform Engineer",
      requisitionId: "REQ-7788",
      location: "San Francisco, CA / Remote",
      disposition: "lead",
      resurfacePolicy: "never",
      score: 91.5,
      versionToken: crypto.randomUUID(),
      createdAt: "2026-10-20T12:00:00Z",
      updatedAt: "2026-10-25T14:30:00Z",
      url: "https://acme.example.com/careers/lead-platform",
      compensation: {
        currency: "USD",
        rawText: "$190,000 - $230,000 base + equity",
      },
      signals: [
        {
          signalType: "tech_stack",
          evidence: "TypeScript, .NET 10, Docker, Linux",
          detectedAt: "2026-10-20T12:00:00Z",
        },
      ],
      assessmentNote: {
        id: crypto.randomUUID(),
        summary: "Excellent tech stack alignment and compensation match.",
        updatedAt: "2026-10-20T12:05:00Z",
      },
    };

    const mockCriteria = [
      {
        id: criterionId,
        name: "Remote Work Preference",
        weight: "mandatory",
        ruleText: "Must allow 100% remote within US.",
        effectiveFrom: "2026-01-01T00:00:00Z",
        effectiveTo: null,
        isActive: true,
      },
    ];

    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: mockPosting,
          error: undefined,
          response: { status: 200 } as Response,
        });
      }
      if (endpoint === "/api/v1/criteria") {
        return Promise.resolve({
          data: mockCriteria,
          error: undefined,
          response: { status: 200 } as Response,
        });
      }
      return Promise.resolve({ data: undefined, response: { status: 404 } as Response });
    });

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await postingDetailLoader({
      request,
      params: { id: postingId },
      context: {},
    } as any);

    expect(result.posting).toEqual(mockPosting);
    expect(result.criteria).toEqual(mockCriteria);
    expect(result.csrfToken).toBe("mock-csrf-token-444");
    expect(result.userLabel).toBe("Signed in");

    // Invariant: Never return tokens to browser
    const serialized = JSON.stringify(result);
    expect(serialized).not.toContain("mock-access-token-111");
    expect(serialized).not.toContain("mock-refresh-token-222");
    expect(serialized).not.toContain("mock-id-token-333");
  });

  it("handles 404 Not Found by throwing a 404 Response", async () => {
    const { sessionId } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: undefined,
          error: { ok: false, errorCode: "NOT_FOUND", message: "Not found" },
          response: { status: 404 } as Response,
        });
      }
      return Promise.resolve({ data: [], response: { status: 200 } as Response });
    });

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await postingDetailLoader({
        request,
        params: { id: postingId },
        context: {},
      } as any);
    } catch (err: any) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(404);
  });

  it("handles 401 Unauthorized from API by destroying session and redirecting to login with returnTo", async () => {
    const { sessionId } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: undefined,
          error: { ok: false, errorCode: "UNAUTHORIZED", message: "Token expired" },
          response: { status: 401 } as Response,
        });
      }
      return Promise.resolve({ data: [], response: { status: 200 } as Response });
    });

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await postingDetailLoader({
        request,
        params: { id: postingId },
        context: {},
      } as any);
    } catch (err: any) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    const location = thrown?.headers.get("Location");
    expect(location).toContain("/auth");
    expect(thrown?.headers.get("Set-Cookie")).toBeDefined();
  });
});
