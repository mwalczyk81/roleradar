import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { action as companyDetailAction } from "../../app/routes/companies.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callAction } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("Company Detail Action (T041)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiPatch = vi.fn();
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
      PATCH: mockApiPatch,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function createTestSession(): { sessionId: string; csrfToken: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const csrfToken = "valid-csrf-token-12345";
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token-comp",
      refreshToken: "mock-refresh-token-comp",
      idToken: "mock-id-token-comp",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken,
    };
    saveSession(session);
    return { sessionId, csrfToken, session };
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

  it("enforces CSRF validation and returns error on missing or invalid _csrf", async () => {
    const { sessionId } = createTestSession();
    const companyId = crypto.randomUUID();

    const formData = new FormData();
    formData.append("_csrf", "invalid-csrf-token");
    formData.append("name", "Updated Corp");
    formData.append("versionToken", crypto.randomUUID());

    const request = new Request(`https://localhost:5003/companies/${companyId}`, {
      method: "POST",
      body: formData,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const rawResult = await callAction(companyDetailAction, request, { id: companyId });

    const { status, data } = unwrapActionResult(rawResult);
    expect(status).toBe(403);
    expect(mockApiPatch).not.toHaveBeenCalled();
    expect(String(data.error)).toMatch(/csrf|not authorized/i);
  });

  it("sends valid UpdateCompanyRequestDto payload to PATCH /api/v1/companies/{id} with versionToken", async () => {
    const { sessionId, csrfToken } = createTestSession();
    const companyId = crypto.randomUUID();
    const versionToken = crypto.randomUUID();

    const updatedCompany = {
      id: companyId,
      name: "Acme Innovations Ltd",
      status: "target",
      ownershipType: "private",
      description: "Updated tech company description",
      headquarters: "San Jose, CA",
      categoryTags: ["saas", "ai"],
      careerSources: [
        {
          sourceType: "ats",
          atsProvider: "lever",
          atsTenant: "acme-innovations",
          sourceUrl: "https://jobs.lever.co/acme-innovations",
          fetchMethod: "api",
          active: true,
        },
      ],
      versionToken: crypto.randomUUID(),
    };

    mockApiPatch.mockResolvedValueOnce({
      data: updatedCompany,
      error: undefined,
      response: { status: 200 } as Response,
    });

    const formData = new FormData();
    formData.append("_csrf", csrfToken);
    formData.append("name", "Acme Innovations Ltd");
    formData.append("status", "target");
    formData.append("ownershipType", "private");
    formData.append("description", "Updated tech company description");
    formData.append("headquarters", "San Jose, CA");
    formData.append("categoryTags", JSON.stringify(["saas", "ai"]));
    formData.append(
      "careerSources",
      JSON.stringify([
        {
          sourceType: "ats",
          atsProvider: "lever",
          atsTenant: "acme-innovations",
          sourceUrl: "https://jobs.lever.co/acme-innovations",
          fetchMethod: "api",
          active: true,
        },
      ])
    );
    formData.append("versionToken", versionToken);

    const request = new Request(`https://localhost:5003/companies/${companyId}`, {
      method: "POST",
      body: formData,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const rawResult = await callAction(companyDetailAction, request, { id: companyId });

    const { status, data } = unwrapActionResult(rawResult);
    expect(status).toBe(200);
    expect(mockApiPatch).toHaveBeenCalledTimes(1);
    const [endpoint, options] = mockApiPatch.mock.calls[0];
    expect(endpoint).toBe("/api/v1/companies/{id}");
    expect(options.params.path).toEqual({ id: companyId });
    expect(options.body).toEqual({
      name: "Acme Innovations Ltd",
      status: "target",
      ownershipType: "private",
      description: "Updated tech company description",
      headquarters: "San Jose, CA",
      categoryTags: ["saas", "ai"],
      careerSources: [
        {
          sourceType: "ats",
          atsProvider: "lever",
          atsTenant: "acme-innovations",
          sourceUrl: "https://jobs.lever.co/acme-innovations",
          fetchMethod: "api",
          active: true,
        },
      ],
      versionToken,
    });

    expect(data.ok).toBe(true);
    expect(data.company).toEqual(updatedCompany);
  });

  it("maps 400 validation error from API to field errors next to form fields", async () => {
    const { sessionId, csrfToken } = createTestSession();
    const companyId = crypto.randomUUID();
    const versionToken = crypto.randomUUID();

    mockApiPatch.mockResolvedValueOnce({
      data: undefined,
      error: {
        ok: false,
        errorCode: "VALIDATION_ERROR",
        message: "Invalid company payload",
        details: [
          { field: "name", issue: "Company name cannot be blank." },
          { field: "status", issue: "Invalid status value." },
        ],
      },
      response: { status: 400 } as Response,
    });

    const formData = new FormData();
    formData.append("_csrf", csrfToken);
    formData.append("name", "");
    formData.append("status", "invalid-status");
    formData.append("versionToken", versionToken);

    const request = new Request(`https://localhost:5003/companies/${companyId}`, {
      method: "POST",
      body: formData,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const rawResult = await callAction(companyDetailAction, request, { id: companyId });

    const { status, data } = unwrapActionResult(rawResult);
    expect(status).toBe(400);
    expect(data.ok).toBe(false);
    expect(data.fieldErrors).toBeDefined();
    const fieldErrors = data.fieldErrors as Record<string, string>;
    expect(fieldErrors.name).toBe("Company name cannot be blank.");
    expect(fieldErrors.status).toBe("Invalid status value.");
  });

  it("handles 409 Concurrency Conflict and returns conflict error for toast reload trigger", async () => {
    const { sessionId, csrfToken } = createTestSession();
    const companyId = crypto.randomUUID();
    const versionToken = crypto.randomUUID();

    mockApiPatch.mockResolvedValueOnce({
      data: undefined,
      error: {
        ok: false,
        errorCode: "CONCURRENCY_CONFLICT",
        message: "The record was modified by another user or operation.",
      },
      response: { status: 409 } as Response,
    });

    const formData = new FormData();
    formData.append("_csrf", csrfToken);
    formData.append("name", "Stale Company Name");
    formData.append("versionToken", versionToken);

    const request = new Request(`https://localhost:5003/companies/${companyId}`, {
      method: "POST",
      body: formData,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const rawResult = await callAction(companyDetailAction, request, { id: companyId });

    const { status, data } = unwrapActionResult(rawResult);
    expect(status).toBe(409);
    expect(data.ok).toBe(false);
    expect(data.conflict).toBe(true);
    expect(String(data.error)).toMatch(/modified|conflict/i);
  });

  it("redirects to /not-authorized when API returns 403 Forbidden on update", async () => {
    const { sessionId, csrfToken } = createTestSession();
    const companyId = crypto.randomUUID();

    mockApiPatch.mockResolvedValueOnce({
      data: undefined,
      error: {
        ok: false,
        errorCode: "FORBIDDEN",
        message: "Forbidden",
      },
      response: { status: 403 } as Response,
    });

    const formData = new FormData();
    formData.append("_csrf", csrfToken);
    formData.append("name", "Updated Company");
    formData.append("versionToken", "comp-ver-1");

    const request = new Request(`https://localhost:5003/companies/${companyId}`, {
      method: "POST",
      body: formData,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callAction(companyDetailAction, request, { id: companyId });
    } catch (err: unknown) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    expect(thrown?.headers.get("Location")).toBe("/not-authorized");
  });
});
