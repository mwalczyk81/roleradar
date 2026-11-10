import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { action as postingDetailAction } from "../../app/routes/postings.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callAction } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("POST /postings/:id action (T035)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiPost = vi.fn();

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    setOidcConfigForTesting(
      new client.Configuration(
        {
          issuer: "https://identity:5001",
          authorization_endpoint: "https://identity:5001/connect/authorize",
          token_endpoint: "https://identity:5001/connect/token",
          end_session_endpoint: "https://identity:5001/connect/endsession",
        },
        "roleradar-web",
        "test-secret"
      )
    );
    vi.restoreAllMocks();
    mockApiPost.mockReset();
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      POST: mockApiPost,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function createTestSession(): { sessionId: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-matt-001",
      accessToken: "mock-access-token-action",
      refreshToken: "mock-refresh-token-action",
      idToken: "mock-id-token-action",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "valid-csrf-token-777",
    };
    saveSession(session);
    return { sessionId, session };
  }

  function unwrapActionResult(result: unknown): { status: number; data: Record<string, unknown> } {
    if (result && typeof result === "object" && "type" in result && (result as { type: string }).type === "DataWithResponseInit") {
      const dataInit = result as unknown as { init?: { status?: number }; data: Record<string, unknown> };
      return { status: dataInit.init?.status ?? 200, data: dataInit.data };
    }
    if (result instanceof Response) {
      return { status: result.status, data: {} };
    }
    return { status: 200, data: (result ?? {}) as Record<string, unknown> };
  }

  it("enforces CSRF validation and rejects request with invalid _csrf with 403", async () => {
    const { sessionId } = createTestSession();
    const formData = new FormData();
    formData.set("_csrf", "invalid-csrf");
    formData.set("toDisposition", "scored");
    formData.set("reason", "Good role");
    formData.set("versionToken", crypto.randomUUID());

    const request = new Request("https://localhost:5003/postings/post-1", {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: "post-1" });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(403);
    expect(data.ok).toBe(false);
    expect(data.errorCode).toBe("CSRF_INVALID");
    expect(String(data.message)).toContain("CSRF");
  });

  it("rejects transition to 'applied' disposition with 400 validation error", async () => {
    const { sessionId, session } = createTestSession();
    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "applied");
    formData.set("reason", "Applying directly");
    formData.set("versionToken", crypto.randomUUID());

    const request = new Request("https://localhost:5003/postings/post-1", {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: "post-1" });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(String(data.message)).toContain("applied");
  });

  it("maps valid transition form submission to POST /api/v1/postings/{id}/transition", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();
    const versionToken = crypto.randomUUID();
    const criterionId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: {
        id: postingId,
        companyId: crypto.randomUUID(),
        companyName: "Acme Corp",
        title: "Principal Engineer",
        requisitionId: "REQ-101",
        location: "Remote",
        disposition: "scored",
        resurfacePolicy: "if_reopened",
        score: 90,
        versionToken: crypto.randomUUID(),
        createdAt: "2026-10-31T10:00:00Z",
        updatedAt: "2026-10-31T11:00:00Z",
        url: null,
        compensation: { currency: "USD", rawText: null, hasSalary: false },
        signals: [],
        assessmentNote: null,
      },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "scored");
    formData.set("reason", "Strong alignment with experience");
    formData.set("criterionId", criterionId);
    formData.set("versionToken", versionToken);
    formData.set("resurfacePolicy", "if_reopened");
    formData.set("overrideReason", "Recruiter confirmed eligibility");

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: postingId });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(200);
    expect(data.ok).toBe(true);

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const [endpoint, options] = mockApiPost.mock.calls[0];
    expect(endpoint).toBe("/api/v1/postings/{id}/transition");
    expect(options.params.path.id).toBe(postingId);
    expect(options.body).toEqual({
      toDisposition: "scored",
      reason: "Strong alignment with experience",
      criterionId,
      versionToken,
      resurfacePolicy: "if_reopened",
      overrideReason: "Recruiter confirmed eligibility",
    });

    // Invariant: Tokens never leak in action return data
    const serialized = JSON.stringify(data);
    expect(serialized).not.toContain("mock-access-token-action");
    expect(serialized).not.toContain("mock-refresh-token-action");
  });

  it("maps 400 InvalidTransition error from API into form error state", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        status: 400,
        errorCode: "validation_error",
        detail: "Cannot transition from 'closed' to 'scored'.",
      },
      response: { status: 400 } as Response,
    });

    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "scored");
    formData.set("reason", "Attempting reopen");
    formData.set("versionToken", crypto.randomUUID());

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: postingId });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(data.errorCode).toBe("validation_error");
    expect(String(data.message)).toContain("Cannot transition");
  });

  it("maps 409 Concurrency Conflict from API into conflict alert state", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        status: 409,
        errorCode: "concurrency_conflict",
        detail: "The posting was modified by another process. Please reload.",
      },
      response: { status: 409 } as Response,
    });

    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "scored");
    formData.set("reason", "Updating stale posting");
    formData.set("versionToken", crypto.randomUUID());

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: postingId });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(409);
    expect(data.ok).toBe(false);
    expect(data.isConflict).toBe(true);
    expect(String(data.message)).toContain("modified by another process");
  });

  it("maps 500 unexpected API error from backend gracefully", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        status: 500,
        errorCode: "internal_error",
        detail: "Database connection failed unexpectedly",
      },
      response: { status: 500 } as Response,
    });

    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "scored");
    formData.set("reason", "Server test");
    formData.set("versionToken", crypto.randomUUID());

    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
      body: formData,
    });

    const result = await callAction(postingDetailAction, request, { id: postingId });

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(500);
    expect(data.ok).toBe(false);
    expect(String(data.message)).toContain("Database connection failed");
  });

  it("maps a 503 service_unavailable problem document to a 503 action result with the API's friendly message and no raw text (013 US3)", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        status: 503,
        errorCode: "service_unavailable",
        detail: "The data store is not reachable. Try again shortly.",
        traceId: "0af7651916cd43dd8448eb211c80319c",
      },
      response: { status: 503 } as Response,
    });

    const formData = new FormData();
    formData.set("_csrf", session.csrfToken);
    formData.set("toDisposition", "scored");
    formData.set("reason", "Outage test");
    formData.set("versionToken", crypto.randomUUID());
    const request = new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
      body: formData,
    });

    const { status, data } = unwrapActionResult(await callAction(postingDetailAction, request, { id: postingId }));

    expect(status).toBe(503);
    expect(data.ok).toBe(false);
    expect(data.errorCode).toBe("service_unavailable");
    expect(String(data.message)).toBe("The data store is not reachable. Try again shortly.");
    expect(JSON.stringify(data)).not.toMatch(/Mongo|stack|27017/);
  });

  // ---- 008 phase 4 (T050): eventDate, warnings, status handling ----

  function transitionRequest(sessionId: string, csrf: string, fields: Record<string, string>, postingId = "post-1") {
    const formData = new FormData();
    formData.set("_csrf", csrf);
    for (const [k, v] of Object.entries(fields)) formData.set(k, v);
    return new Request(`https://localhost:5003/postings/${postingId}`, {
      method: "POST",
      headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
      body: formData,
    });
  }

  const baseFields = { toDisposition: "rejected", reason: "Rejected by email", versionToken: "ver-1" };

  async function thrownFrom(run: () => Promise<unknown>): Promise<Response | null> {
    try {
      await run();
    } catch (err: unknown) {
      if (err instanceof Response) return err;
    }
    return null;
  }

  it("forwards a submitted eventDate to the API, trimmed", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValueOnce({
      data: { id: "post-1", disposition: "rejected", warnings: [], applicationId: "app-1" },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = transitionRequest(sessionId, session.csrfToken, { ...baseFields, eventDate: " 2026-09-15 " });
    const result = await callAction(postingDetailAction, request, { id: "post-1" });

    expect(unwrapActionResult(result).status).toBe(200);
    const [, options] = mockApiPost.mock.calls[0];
    expect(options.body.eventDate).toBe("2026-09-15");
  });

  it("sends no eventDate when the field is blank or absent", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValue({
      data: { id: "post-1", disposition: "rejected" },
      error: undefined,
      response: { status: 200 } as Response,
    });

    await callAction(postingDetailAction, transitionRequest(sessionId, session.csrfToken, { ...baseFields, eventDate: "  " }), { id: "post-1" });
    await callAction(postingDetailAction, transitionRequest(sessionId, session.csrfToken, baseFields), { id: "post-1" });

    for (const [, options] of mockApiPost.mock.calls) {
      expect(options.body.eventDate ?? null).toBeNull();
    }
  });

  it("returns the API posting with its warnings untouched, as typed action data", async () => {
    const { sessionId, session } = createTestSession();
    const posting = {
      id: "post-1",
      disposition: "lead",
      applicationId: "app-1",
      warnings: [{ code: "any_code_at_all", message: "Fictional warning text." }],
    };
    mockApiPost.mockResolvedValueOnce({ data: posting, error: undefined, response: { status: 200 } as Response });

    const result = await callAction(
      postingDetailAction,
      transitionRequest(sessionId, session.csrfToken, { toDisposition: "lead", reason: "Reopened", versionToken: "ver-1", overrideReason: "Recruiter reopened" }),
      { id: "post-1" }
    );

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(200);
    expect(data.ok).toBe(true);
    expect(data.posting).toEqual(posting);
  });

  it("maps a 400 with an eventDate detail into an inline field error", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        status: 400,
        errorCode: "validation_error",
        detail: "Validation failed.",
        errors: { eventDate: ["Event date must be a valid date in yyyy-MM-dd format."] },
      },
      response: { status: 400 } as Response,
    });

    const result = await callAction(
      postingDetailAction,
      transitionRequest(sessionId, session.csrfToken, { ...baseFields, eventDate: "09/15/2026" }),
      { id: "post-1" }
    );

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect((data.fieldErrors as Record<string, string>).eventDate).toBe("Event date must be a valid date in yyyy-MM-dd format.");
  });

  it("does not call the API when the CSRF token is wrong", async () => {
    const { sessionId } = createTestSession();
    const result = await callAction(
      postingDetailAction,
      transitionRequest(sessionId, "not-the-token", { ...baseFields, eventDate: "2026-09-15" }),
      { id: "post-1" }
    );

    expect(unwrapActionResult(result).status).toBe(403);
    expect(mockApiPost).not.toHaveBeenCalled();
  });

  it("redirects to /not-authorized when the API answers 403", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: { status: 403, errorCode: "forbidden" },
      response: { status: 403 } as Response,
    });

    const thrown = await thrownFrom(() =>
      callAction(postingDetailAction, transitionRequest(sessionId, session.csrfToken, baseFields), { id: "post-1" })
    );

    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });

  it("throws a 404 response when the API cannot find the posting", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: { status: 404, errorCode: "not_found", detail: "Posting not found." },
      response: { status: 404 } as Response,
    });

    const thrown = await thrownFrom(() =>
      callAction(postingDetailAction, transitionRequest(sessionId, session.csrfToken, baseFields), { id: "post-1" })
    );

    expect(thrown?.status).toBe(404);
  });

  it("redirects into the login flow once when the API answers 401", async () => {
    const { sessionId, session } = createTestSession();
    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: { status: 401, errorCode: "unauthorized" },
      response: { status: 401 } as Response,
    });

    const thrown = await thrownFrom(() =>
      callAction(postingDetailAction, transitionRequest(sessionId, session.csrfToken, baseFields), { id: "post-1" })
    );

    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toContain("/auth");
    expect(mockApiPost).toHaveBeenCalledTimes(1);
  });
});
