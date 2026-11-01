import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as companiesListLoader } from "../../app/routes/companies._index.js";
import { loader as companyDetailLoader } from "../../app/routes/companies.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("Companies Loaders (T040)", () => {
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
      ownerSub: "owner-test-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token-comp",
      refreshToken: "mock-refresh-token-comp",
      idToken: "mock-id-token-comp",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-comp",
    };
    saveSession(session);
    return { sessionId, session };
  }

  describe("GET /companies list loader", () => {
    it("passes ONLY limit and offset to GET /api/v1/companies, clamping limit to 1..100, and ignores search/sort params", async () => {
      const { sessionId } = createTestSession();

      const mockCompany = {
        id: crypto.randomUUID(),
        name: "Acme Cloud",
        status: "target",
        ownershipType: "private",
        description: "Cloud infrastructure tools",
        headquarters: "Seattle, WA",
        categoryTags: ["cloud", "devops"],
        careerSources: [
          {
            sourceType: "ats",
            atsProvider: "greenhouse",
            atsTenant: "acmecloud",
            sourceUrl: "https://boards.greenhouse.io/acmecloud",
            fetchMethod: "api",
            active: true,
          },
        ],
        versionToken: crypto.randomUUID(),
      };

      mockApiGet.mockResolvedValueOnce({
        data: {
          items: [mockCompany],
          totalCount: 1,
          limit: 100,
          offset: 0,
        },
        error: undefined,
        response: { status: 200 } as Response,
      });

      // Pass attempted search, sort, and out-of-range limit/offset in query params
      const url = "https://localhost:5003/companies?limit=150&offset=-5&q=searchtext&sort=name&direction=asc";
      const request = new Request(url, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const result = await callLoader(companiesListLoader, request);

      expect(mockApiGet).toHaveBeenCalledTimes(1);
      const [endpoint, options] = mockApiGet.mock.calls[0];
      expect(endpoint).toBe("/api/v1/companies");

      // Scope fact: Only limit and offset exist on GET /api/v1/companies.
      // Limit clamped to 1..100 (150 -> 100), offset clamped to >= 0 (-5 -> 0).
      expect(options.params.query).toEqual({
        limit: 100,
        offset: 0,
      });

      expect(result.items).toEqual([mockCompany]);
      expect(result.totalCount).toBe(1);
      expect(result.limit).toBe(100);
      expect(result.offset).toBe(0);
      expect(result.csrfToken).toBe("mock-csrf-token-comp");
      expect(result.userLabel).toBe("testowner");

      // Tokens must NEVER leak to client
      const serialized = JSON.stringify(result);
      expect(serialized).not.toContain("mock-access-token-comp");
      expect(serialized).not.toContain("mock-refresh-token-comp");
      expect(serialized).not.toContain("mock-id-token-comp");
    });

    it("handles 401 Unauthorized from API by destroying session and redirecting to login with returnTo", async () => {
      const { sessionId } = createTestSession();

      mockApiGet.mockResolvedValueOnce({
        data: undefined,
        error: { ok: false, errorCode: "UNAUTHORIZED", message: "Token expired" },
        response: { status: 401 } as Response,
      });

      const request = new Request("https://localhost:5003/companies", {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      let thrown: Response | null = null;
      try {
        await callLoader(companiesListLoader, request);
      } catch (err: unknown) {
        if (err instanceof Response) thrown = err;
      }

      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(302);
      expect(thrown?.headers.get("Location")).toContain("/auth");
      expect(thrown?.headers.get("Set-Cookie")).toBeDefined();
    });

    it("handles 403 Forbidden from API by redirecting to /not-authorized", async () => {
      const { sessionId } = createTestSession();

      mockApiGet.mockResolvedValueOnce({
        data: undefined,
        error: { ok: false, errorCode: "FORBIDDEN", message: "Forbidden" },
        response: { status: 403 } as Response,
      });

      const request = new Request("https://localhost:5003/companies", {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      let thrown: Response | null = null;
      try {
        await callLoader(companiesListLoader, request);
      } catch (err: unknown) {
        if (err instanceof Response) thrown = err;
      }

      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(302);
      expect(thrown?.headers.get("Location")).toBe("/not-authorized");
    });
  });

  describe("GET /companies/:id detail loader", () => {
    it("fetches company detail by id from params and returns clean data without tokens", async () => {
      const { sessionId } = createTestSession();
      const companyId = crypto.randomUUID();

      const mockCompany = {
        id: companyId,
        name: "Northwest Systems",
        status: "watch",
        ownershipType: "public",
        description: "Enterprise software services",
        headquarters: "Portland, OR",
        categoryTags: ["enterprise"],
        careerSources: [],
        versionToken: crypto.randomUUID(),
      };

      mockApiGet.mockResolvedValueOnce({
        data: mockCompany,
        error: undefined,
        response: { status: 200 } as Response,
      });

      const request = new Request(`https://localhost:5003/companies/${companyId}`, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const result = await callLoader(companyDetailLoader, request, { id: companyId });

      expect(mockApiGet).toHaveBeenCalledTimes(1);
      const [endpoint, options] = mockApiGet.mock.calls[0];
      expect(endpoint).toBe("/api/v1/companies/{id}");
      expect(options.params.path).toEqual({ id: companyId });

      expect(result.company).toEqual(mockCompany);
      expect(result.csrfToken).toBe("mock-csrf-token-comp");
      expect(result.userLabel).toBe("testowner");

      const serialized = JSON.stringify(result);
      expect(serialized).not.toContain("mock-access-token-comp");
    });

    it("throws 404 Response when company is not found", async () => {
      const { sessionId } = createTestSession();
      const companyId = crypto.randomUUID();

      mockApiGet.mockResolvedValueOnce({
        data: undefined,
        error: { ok: false, errorCode: "NOT_FOUND", message: "Company not found" },
        response: { status: 404 } as Response,
      });

      const request = new Request(`https://localhost:5003/companies/${companyId}`, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      let thrown: Response | null = null;
      try {
        await callLoader(companyDetailLoader, request, { id: companyId });
      } catch (err: unknown) {
        if (err instanceof Response) thrown = err;
      }

      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(404);
    });

    it("handles 403 Forbidden from API by redirecting to /not-authorized", async () => {
      const { sessionId } = createTestSession();
      const companyId = crypto.randomUUID();

      mockApiGet.mockResolvedValueOnce({
        data: undefined,
        error: { ok: false, errorCode: "FORBIDDEN", message: "Forbidden" },
        response: { status: 403 } as Response,
      });

      const request = new Request(`https://localhost:5003/companies/${companyId}`, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      let thrown: Response | null = null;
      try {
        await callLoader(companyDetailLoader, request, { id: companyId });
      } catch (err: unknown) {
        if (err instanceof Response) thrown = err;
      }

      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(302);
      expect(thrown?.headers.get("Location")).toBe("/not-authorized");
    });
  });
});
