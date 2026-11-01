import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as applicationsListLoader } from "../../app/routes/applications._index.js";
import { loader as applicationDetailLoader } from "../../app/routes/applications.$id.js";
import { resolveApplicationContext } from "../../app/lib/application-context.server.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("Applications Loaders & Bounded Context Resolution (T046)", () => {
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

  describe("GET /applications list loader", () => {
    it("passes ONLY status, limit, offset query params and clamps limit 1..100 and offset >= 0", async () => {
      const { sessionId } = createTestSession();

      const postingId1 = crypto.randomUUID();
      const companyId1 = crypto.randomUUID();
      const appId1 = crypto.randomUUID();

      const mockApp = {
        id: appId1,
        postingId: postingId1,
        companyId: companyId1,
        status: "applied",
        appliedAt: "2026-10-31",
        events: [
          {
            eventType: "applied",
            eventDate: "2026-10-31",
            notes: "Submitted via career site",
          },
        ],
      };

      // 1. Return application list
      mockApiGet.mockImplementation(async (path: string) => {
        if (path === "/api/v1/applications") {
          return {
            data: {
              items: [mockApp],
              totalCount: 1,
              limit: 25,
              offset: 0,
            },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/postings/{id}") {
          return {
            data: { id: postingId1, title: "Senior Staff Engineer", companyName: "Acme Corp" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/companies/{id}") {
          return {
            data: { id: companyId1, name: "Acme Corp" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        return { data: undefined, error: { message: "Not found" }, response: { status: 404 } as Response };
      });

      const url = "https://localhost:5003/applications?status=applied&limit=250&offset=-10&extra=malicious&sort=date";
      const request = new Request(url, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const result = await applicationsListLoader({ request, params: {}, context: {} } as any);

      // Verify query params sent to /api/v1/applications
      const appCalls = mockApiGet.mock.calls.filter((call) => call[0] === "/api/v1/applications");
      expect(appCalls).toHaveLength(1);
      const querySent = appCalls[0][1]?.params?.query;

      expect(querySent).toEqual({
        status: "applied",
        limit: 100, // Clamped from 250
        offset: 0,   // Clamped from -10
      });

      expect(result.items).toHaveLength(1);
      expect(result.postingMap[postingId1]?.title).toBe("Senior Staff Engineer");
      expect(result.companyMap[companyId1]?.name).toBe("Acme Corp");
    });

    it("deduplicates name resolution and sends no more lookups than distinct IDs on the page", async () => {
      const { sessionId } = createTestSession();

      const postingIdA = crypto.randomUUID();
      const postingIdB = crypto.randomUUID();
      const companyIdX = crypto.randomUUID();

      // 4 applications sharing 2 distinct postings and 1 distinct company
      const mockApps = [
        { id: crypto.randomUUID(), postingId: postingIdA, companyId: companyIdX, status: "applied", appliedAt: "2026-10-01", events: [] },
        { id: crypto.randomUUID(), postingId: postingIdA, companyId: companyIdX, status: "interview", appliedAt: "2026-10-02", events: [] },
        { id: crypto.randomUUID(), postingId: postingIdB, companyId: companyIdX, status: "offer", appliedAt: "2026-10-03", events: [] },
        { id: crypto.randomUUID(), postingId: postingIdB, companyId: companyIdX, status: "closed", appliedAt: "2026-10-04", events: [] },
      ];

      mockApiGet.mockImplementation(async (path: string, options: any) => {
        if (path === "/api/v1/applications") {
          return {
            data: { items: mockApps, totalCount: 4, limit: 25, offset: 0 },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/postings/{id}") {
          const id = options?.params?.path?.id;
          return {
            data: { id, title: `Posting ${id.slice(0, 8)}`, companyName: "Company X" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/companies/{id}") {
          const id = options?.params?.path?.id;
          return {
            data: { id, name: "Company X" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        return { data: undefined, error: { message: "Not found" }, response: { status: 404 } as Response };
      });

      const request = new Request("https://localhost:5003/applications", {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const result = await applicationsListLoader({ request, params: {}, context: {} } as any);

      // Verify lookups count
      const postingCalls = mockApiGet.mock.calls.filter((call) => call[0] === "/api/v1/postings/{id}");
      const companyCalls = mockApiGet.mock.calls.filter((call) => call[0] === "/api/v1/companies/{id}");

      // Exactly 2 distinct postings, exactly 1 distinct company
      expect(postingCalls).toHaveLength(2);
      expect(companyCalls).toHaveLength(1);

      expect(result.items).toHaveLength(4);
    });

    it("tolerates failed lookup (404/500/network error) and shows fallback or Unavailable without failing the list", async () => {
      const { sessionId } = createTestSession();

      const postingIdMissing = crypto.randomUUID();
      const companyIdFailing = crypto.randomUUID();

      const mockApps = [
        {
          id: crypto.randomUUID(),
          postingId: postingIdMissing,
          companyId: companyIdFailing,
          status: "applied",
          appliedAt: "2026-10-31",
          events: [],
        },
      ];

      mockApiGet.mockImplementation(async (path: string) => {
        if (path === "/api/v1/applications") {
          return {
            data: { items: mockApps, totalCount: 1, limit: 25, offset: 0 },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/postings/{id}") {
          return {
            data: undefined,
            error: { message: "Posting not found" },
            response: { status: 404 } as Response,
          };
        }
        if (path === "/api/v1/companies/{id}") {
          return {
            data: undefined,
            error: { message: "Internal server error" },
            response: { status: 500 } as Response,
          };
        }
        return { data: undefined, error: { message: "Error" }, response: { status: 500 } as Response };
      });

      const request = new Request("https://localhost:5003/applications", {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      // Must succeed and NOT throw
      const result = await applicationsListLoader({ request, params: {}, context: {} } as any);

      expect(result.items).toHaveLength(1);
      expect(result.postingMap[postingIdMissing]?.title).toMatch(/Unavailable/i);
      expect(result.companyMap[companyIdFailing]?.name).toMatch(/Unavailable/i);
    });
  });

  describe("resolveApplicationContext helper unit invariants", () => {
    it("caps concurrency to bounded limit (e.g. 5 in-flight)", async () => {
      let currentInFlight = 0;
      let maxObservedInFlight = 0;

      const mockClient = {
        GET: vi.fn().mockImplementation(async () => {
          currentInFlight++;
          maxObservedInFlight = Math.max(maxObservedInFlight, currentInFlight);
          // Simulate latency
          await new Promise((resolve) => setTimeout(resolve, 20));
          currentInFlight--;
          return {
            data: { title: "Title", name: "Name" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }),
      };

      // 15 distinct postings and 15 distinct companies (30 total lookups)
      const apps = Array.from({ length: 15 }, () => ({
        postingId: crypto.randomUUID(),
        companyId: crypto.randomUUID(),
      }));

      await resolveApplicationContext(mockClient as any, apps, { concurrency: 5 });

      expect(maxObservedInFlight).toBeLessThanOrEqual(5);
    });
  });

  describe("GET /applications/:id detail loader", () => {
    it("fetches ApplicationDetailDto and resolves associated posting and company details", async () => {
      const { sessionId } = createTestSession();

      const appId = crypto.randomUUID();
      const postingId = crypto.randomUUID();
      const companyId = crypto.randomUUID();

      const mockAppDetail = {
        id: appId,
        postingId,
        companyId,
        status: "interview",
        appliedAt: "2026-10-15",
        events: [
          { eventType: "applied", eventDate: "2026-10-15", notes: "Applied on site" },
          { eventType: "interview", eventDate: "2026-10-22", notes: "Technical screen" },
        ],
      };

      mockApiGet.mockImplementation(async (path: string, _options: any) => {
        if (path === "/api/v1/applications/{id}") {
          return {
            data: mockAppDetail,
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/postings/{id}") {
          return {
            data: { id: postingId, title: "Staff Backend Engineer", location: "Remote, US" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        if (path === "/api/v1/companies/{id}") {
          return {
            data: { id: companyId, name: "Global Systems Inc" },
            error: undefined,
            response: { status: 200 } as Response,
          };
        }
        return { data: undefined, error: { message: "Not found" }, response: { status: 404 } as Response };
      });

      const request = new Request(`https://localhost:5003/applications/${appId}`, {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const result = await applicationDetailLoader({ request, params: { id: appId }, context: {} } as any);

      expect(result.application.id).toBe(appId);
      expect(result.application.events).toHaveLength(2);
      expect(result.posting.title).toBe("Staff Backend Engineer");
      expect(result.company.name).toBe("Global Systems Inc");
    });
  });
});
