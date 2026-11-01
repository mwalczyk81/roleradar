import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { action as postingDetailAction } from "../../app/routes/postings.$id.js";
import { action as applicationDetailAction } from "../../app/routes/applications.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("Application Actions (T047)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiPost = vi.fn();
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
      POST: mockApiPost,
      GET: mockApiGet,
    } as any);
  });

  function createTestSession(): { sessionId: string; csrfToken: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const csrfToken = "valid-csrf-token-12345";
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token-apps",
      refreshToken: "mock-refresh-token-apps",
      idToken: "mock-id-token-apps",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken,
    };
    saveSession(session);
    return { sessionId, csrfToken, session };
  }

  function unwrapActionResult(result: any): { status: number; data: any } {
    if (result && result.type === "DataWithResponseInit") {
      return { status: result.init?.status ?? 200, data: result.data };
    }
    if (result instanceof Response) {
      return { status: result.status, data: result };
    }
    return { status: 200, data: result };
  }

  describe("RecordApplicationAction (POST /applications)", () => {
    it("submits RecordApplicationRequestDto with real schema fields and updates posting to applied", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const postingId = crypto.randomUUID();
      const appId = crypto.randomUUID();

      const createdAppDto = {
        id: appId,
        postingId,
        companyId: crypto.randomUUID(),
        status: "applied",
        appliedAt: "2026-10-31",
        events: [
          { eventType: "applied", eventDate: "2026-10-31", notes: "Submitted via portal" },
        ],
      };

      mockApiPost.mockResolvedValueOnce({
        data: createdAppDto,
        error: undefined,
        response: { status: 201 } as Response,
      });

      // Refetch of updated posting after recording application
      mockApiGet.mockResolvedValueOnce({
        data: {
          id: postingId,
          title: "Senior SRE",
          disposition: "applied",
          versionToken: crypto.randomUUID(),
        },
        error: undefined,
        response: { status: 200 } as Response,
      });

      const formData = new FormData();
      formData.append("intent", "create_application");
      formData.append("_csrf", csrfToken);
      formData.append("postingId", postingId);
      formData.append("submittedDate", "2026-10-31");
      formData.append("resumeVariant", "tailored");
      formData.append("desiredSalaryMin", "180000");
      formData.append("desiredSalaryMax", "210000");
      formData.append("desiredSalaryCurrency", "USD");
      formData.append("targetStartDate", "2026-12-01");
      formData.append("resumePointer", "s3://resumes/tailored-sre.pdf");
      formData.append("confirmationCode", "CONF-98765");

      const request = new Request(`https://localhost:5003/postings/${postingId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await postingDetailAction({ request, params: { id: postingId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(200);
      expect(unwrapped.data.ok).toBe(true);
      expect(unwrapped.data.application?.id).toBe(appId);
      expect(unwrapped.data.posting?.disposition).toBe("applied");

      // Verify exact fields sent to POST /api/v1/applications
      const postCalls = mockApiPost.mock.calls.filter((c) => c[0] === "/api/v1/applications");
      expect(postCalls).toHaveLength(1);
      const body = postCalls[0][1]?.body;
      expect(body).toEqual({
        postingId,
        submittedDate: "2026-10-31",
        resumeVariant: "tailored",
        desiredSalaryMin: 180000,
        desiredSalaryMax: 210000,
        desiredSalaryCurrency: "USD",
        targetStartDate: "2026-12-01",
        resumePointer: "s3://resumes/tailored-sre.pdf",
        confirmationCode: "CONF-98765",
        overrideDecided: false,
        overrideReason: null,
      });
    });

    it("rejects invalid CSRF with 403", async () => {
      const { sessionId } = createTestSession();
      const postingId = crypto.randomUUID();

      const formData = new FormData();
      formData.append("intent", "create_application");
      formData.append("_csrf", "bad-token");
      formData.append("postingId", postingId);
      formData.append("submittedDate", "2026-10-31");
      formData.append("resumeVariant", "tailored");

      const request = new Request(`https://localhost:5003/postings/${postingId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await postingDetailAction({ request, params: { id: postingId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(403);
      expect(unwrapped.data.ok).toBe(false);
      expect(unwrapped.data.errorCode).toBe("CSRF_INVALID");
    });

    it("returns 400 with inline field errors when API validation fails", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const postingId = crypto.randomUUID();

      mockApiPost.mockResolvedValueOnce({
        data: undefined,
        error: {
          errorCode: "VALIDATION_FAILED",
          message: "SubmittedDate must be in YYYY-MM-DD format.",
          details: [
            { field: "submittedDate", message: "SubmittedDate must be in YYYY-MM-DD format: 'invalid-date'." },
          ],
        },
        response: { status: 400 } as Response,
      });

      const formData = new FormData();
      formData.append("intent", "create_application");
      formData.append("_csrf", csrfToken);
      formData.append("postingId", postingId);
      formData.append("submittedDate", "invalid-date");
      formData.append("resumeVariant", "tailored");

      const request = new Request(`https://localhost:5003/postings/${postingId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await postingDetailAction({ request, params: { id: postingId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(400);
      expect(unwrapped.data.ok).toBe(false);
      expect(unwrapped.data.fieldErrors?.submittedDate).toBeDefined();
    });

    it("returns 409 conflict when application already exists or conflict occurs", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const postingId = crypto.randomUUID();

      mockApiPost.mockResolvedValueOnce({
        data: undefined,
        error: {
          code: "INVALID_TRANSITION",
          message: "An application already exists for this posting.",
        },
        response: { status: 409 } as Response,
      });

      const formData = new FormData();
      formData.append("intent", "create_application");
      formData.append("_csrf", csrfToken);
      formData.append("postingId", postingId);
      formData.append("submittedDate", "2026-10-31");
      formData.append("resumeVariant", "tailored");

      const request = new Request(`https://localhost:5003/postings/${postingId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await postingDetailAction({ request, params: { id: postingId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(409);
      expect(unwrapped.data.ok).toBe(false);
      expect(unwrapped.data.isConflict).toBe(true);
    });

    it("requires overrideDecided and overrideReason when posting disposition is decided (e.g. screened_out or closed)", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const postingId = crypto.randomUUID();

      const formData = new FormData();
      formData.append("intent", "create_application");
      formData.append("_csrf", csrfToken);
      formData.append("postingId", postingId);
      formData.append("currentDisposition", "screened_out");
      formData.append("submittedDate", "2026-10-31");
      formData.append("resumeVariant", "tailored");
      // Omit overrideDecided and overrideReason

      const request = new Request(`https://localhost:5003/postings/${postingId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await postingDetailAction({ request, params: { id: postingId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(400);
      expect(unwrapped.data.ok).toBe(false);
      expect(unwrapped.data.fieldErrors?.overrideReason).toBeDefined();
    });
  });

  describe("AppendApplicationEventAction (POST /applications/:id/events)", () => {
    it("submits AppendApplicationEventRequestDto and returns updated application detail", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const appId = crypto.randomUUID();

      const updatedAppDto = {
        id: appId,
        postingId: crypto.randomUUID(),
        companyId: crypto.randomUUID(),
        status: "interview",
        appliedAt: "2026-10-15",
        events: [
          { eventType: "applied", eventDate: "2026-10-15", notes: "Applied on site" },
          { eventType: "interview", eventDate: "2026-10-25", notes: "Round 1 interview" },
        ],
      };

      mockApiPost.mockResolvedValueOnce({
        data: updatedAppDto,
        error: undefined,
        response: { status: 200 } as Response,
      });

      const formData = new FormData();
      formData.append("_csrf", csrfToken);
      formData.append("eventType", "interview");
      formData.append("eventDate", "2026-10-25");
      formData.append("notes", "Round 1 interview");
      formData.append("eventSource", "recruiter_email");

      const request = new Request(`https://localhost:5003/applications/${appId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await applicationDetailAction({ request, params: { id: appId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(200);
      expect(unwrapped.data.ok).toBe(true);
      expect(unwrapped.data.application?.events).toHaveLength(2);

      const postCalls = mockApiPost.mock.calls.filter((c) => c[0] === "/api/v1/applications/{id}/events");
      expect(postCalls).toHaveLength(1);
      expect(postCalls[0][1]?.body).toEqual({
        eventType: "interview",
        eventDate: "2026-10-25",
        notes: "Round 1 interview",
        eventSource: "recruiter_email",
      });
    });

    it("validates eventType against allowed domain values", async () => {
      const { sessionId, csrfToken } = createTestSession();
      const appId = crypto.randomUUID();

      const formData = new FormData();
      formData.append("_csrf", csrfToken);
      formData.append("eventType", "invalid_custom_event");
      formData.append("eventDate", "2026-10-25");

      const request = new Request(`https://localhost:5003/applications/${appId}`, {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const result = await applicationDetailAction({ request, params: { id: appId }, context: {} } as any);
      const unwrapped = unwrapActionResult(result);

      expect(unwrapped.status).toBe(400);
      expect(unwrapped.data.ok).toBe(false);
      expect(unwrapped.data.fieldErrors?.eventType).toBeDefined();
    });
  });
});
