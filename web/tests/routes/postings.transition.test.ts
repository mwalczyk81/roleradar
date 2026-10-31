import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import { action as postingDetailAction } from "../../app/routes/postings.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
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
    vi.restoreAllMocks();
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      POST: mockApiPost,
    } as any);
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

  function unwrapActionResult(result: any): { status: number; data: any } {
    if (result && result.type === "DataWithResponseInit") {
      return { status: result.init?.status ?? 200, data: result.data };
    }
    if (result instanceof Response) {
      return { status: result.status, data: result };
    }
    return { status: 200, data: result };
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

    const result = await postingDetailAction({
      request,
      params: { id: "post-1" },
      context: {},
    } as any);

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(403);
    expect(data.ok).toBe(false);
    expect(data.errorCode).toBe("CSRF_INVALID");
    expect(data.message).toContain("CSRF");
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

    const result = await postingDetailAction({
      request,
      params: { id: "post-1" },
      context: {},
    } as any);

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(data.message).toContain("applied");
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

    const result = await postingDetailAction({
      request,
      params: { id: postingId },
      context: {},
    } as any);

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
        ok: false,
        errorCode: "INVALID_TRANSITION",
        message: "Cannot transition from 'closed' to 'scored'.",
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

    const result = await postingDetailAction({
      request,
      params: { id: postingId },
      context: {},
    } as any);

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(data.errorCode).toBe("INVALID_TRANSITION");
    expect(data.message).toContain("Cannot transition");
  });

  it("maps 409 Concurrency Conflict from API into conflict alert state", async () => {
    const { sessionId, session } = createTestSession();
    const postingId = crypto.randomUUID();

    mockApiPost.mockResolvedValueOnce({
      data: undefined,
      error: {
        ok: false,
        errorCode: "CONCURRENCY_CONFLICT",
        message: "The posting was modified by another process. Please reload.",
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

    const result = await postingDetailAction({
      request,
      params: { id: postingId },
      context: {},
    } as any);

    const { status, data } = unwrapActionResult(result);
    expect(status).toBe(409);
    expect(data.ok).toBe(false);
    expect(data.isConflict).toBe(true);
    expect(data.message).toContain("modified by another process");
  });
});
