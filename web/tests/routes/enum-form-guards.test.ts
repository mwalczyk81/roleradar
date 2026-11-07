import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { action as companyDetailAction } from "../../app/routes/companies.$id.js";
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

/**
 * Enum values now come from the generated API types, so an action cannot hand a raw form string to a typed field.
 * An unknown value is answered with a 400 inline field error (as the API would) and never reaches the API.
 */
describe("enum form guards (generated unions, ADR 0035)", () => {
  const mockPatch = vi.fn();
  const mockPost = vi.fn();
  const csrfToken = "valid-csrf-token-enum-guards";

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(crypto.randomBytes(32));
    setOidcConfigForTesting(
      new client.Configuration(
        {
          issuer: "https://identity:5001",
          authorization_endpoint: "https://identity:5001/connect/authorize",
          token_endpoint: "https://identity:5001/connect/token",
          end_session_endpoint: "https://identity:5001/connect/endsession",
        },
        "roleradar-web",
        "test-secret",
      ),
    );
    vi.restoreAllMocks();
    mockPatch.mockReset();
    mockPost.mockReset();
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      PATCH: mockPatch,
      POST: mockPost,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function sessionCookie(): string {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-enum-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token",
      refreshToken: "mock-refresh-token",
      idToken: "mock-id-token",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken,
    };
    saveSession(session);
    return `${SECURE_COOKIE_NAME}=${sessionId}`;
  }

  function unwrap(result: unknown): { status: number; data: Record<string, unknown> } {
    if (result && typeof result === "object" && "type" in result && (result as { type: string }).type === "DataWithResponseInit") {
      const init = result as unknown as { init?: { status?: number }; data: Record<string, unknown> };
      return { status: init.init?.status ?? 200, data: init.data };
    }
    return { status: 200, data: (result ?? {}) as Record<string, unknown> };
  }

  async function post(path: string, fields: Record<string, string>, action: Parameters<typeof callAction>[0], id: string) {
    const form = new FormData();
    form.set("_csrf", csrfToken);
    for (const [key, value] of Object.entries(fields)) {
      form.set(key, value);
    }
    const request = new Request(`https://localhost:5003${path}`, { method: "POST", body: form, headers: { Cookie: sessionCookie() } });
    return unwrap(await callAction(action, request, { id }));
  }

  it("answers an unknown company status with a 400 field error and no API call", async () => {
    const id = crypto.randomUUID();
    const { status, data } = await post(
      `/companies/${id}`,
      { name: "Fictional Corp", status: "frozen", ownershipType: "private", versionToken: crypto.randomUUID() },
      companyDetailAction,
      id,
    );
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(data.fieldErrors).toEqual({ status: "Status is not recognised." });
    expect(mockPatch).not.toHaveBeenCalled();
  });

  it("answers an unknown ownership type with a 400 field error and no API call", async () => {
    const id = crypto.randomUUID();
    const { status, data } = await post(
      `/companies/${id}`,
      { name: "Fictional Corp", status: "target", ownershipType: "cooperative", versionToken: crypto.randomUUID() },
      companyDetailAction,
      id,
    );
    expect(status).toBe(400);
    expect(data.fieldErrors).toEqual({ ownershipType: "Ownership type is not recognised." });
    expect(mockPatch).not.toHaveBeenCalled();
  });

  it("answers an unknown resume variant when recording an application with a 400 field error", async () => {
    const id = crypto.randomUUID();
    const { status, data } = await post(
      `/postings/${id}`,
      { intent: "create_application", submittedDate: "2026-11-01", resumeVariant: "fancy", currentDisposition: "lead" },
      postingDetailAction,
      id,
    );
    expect(status).toBe(400);
    expect(data.fieldErrors).toEqual({ resumeVariant: "Resume variant is not recognised." });
    expect(mockPost).not.toHaveBeenCalled();
  });

  it("answers an unknown target disposition with a 400 field error and no API call", async () => {
    const id = crypto.randomUUID();
    const { status, data } = await post(
      `/postings/${id}`,
      { toDisposition: "parked", reason: "Fictional reason", versionToken: crypto.randomUUID() },
      postingDetailAction,
      id,
    );
    expect(status).toBe(400);
    expect(data.fieldErrors).toEqual({ toDisposition: "Disposition is not recognised." });
    expect(mockPost).not.toHaveBeenCalled();
  });
});
