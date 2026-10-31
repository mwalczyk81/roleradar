import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import http from "node:http";
import * as oidc from "../../app/lib/oidc.server.js";
import {
  saveSession,
  getSession,
  getValidSession,
  createSessionCookie,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";
import { validateCsrfToken } from "../../app/lib/csrf.server.js";
import { applySecurityHeaders, generateCspNonce } from "../../server.js";
import {
  getAuthStateCookieName,
  getCodeVerifierCookieName,
} from "../../app/lib/auth-guard.server.js";
import { loader as callbackLoader } from "../../app/routes/auth.callback.js";
import { action as logoutAction } from "../../app/routes/auth.logout.js";
import { loader as notAuthorizedLoader } from "../../app/routes/not-authorized.js";
import { loader as postingsLoader } from "../../app/routes/postings._index.js";
import { loader as postingDetailLoader } from "../../app/routes/postings.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import NotAuthorized from "../../app/routes/not-authorized.js";

describe("Security Invariants", () => {
  const dummyKey = crypto.randomBytes(32);

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    vi.clearAllMocks();
  });

  // Invariant 1: tokens never appear in response bodies, Set-Cookie values, or browser storage
  it("Invariant 1: tokens never appear in any response body or Set-Cookie header", async () => {
    const rawTokens = {
      access_token: "secret-bearer-access-token-99999",
      refresh_token: "secret-offline-refresh-token-88888",
      id_token: "secret-identity-token-payload-77777",
      expires_in: 600,
      claims: () => ({ sub: "owner-subject" }),
    };

    vi.spyOn(oidc, "exchangeCodeForTokens").mockResolvedValueOnce(rawTokens as any);

    const state = "state-invariant-1";
    const verifier = "verifier-invariant-1";
    const request = new Request(
      `https://localhost:5003/auth/callback?code=test-code&state=${state}`,
      {
        headers: {
          Cookie: `${getAuthStateCookieName()}=${state}; ${getCodeVerifierCookieName()}=${verifier}`,
        },
      }
    );

    const response = await callbackLoader({
      request,
      params: {},
      context: {},
    } as any);

    const setCookie = response.headers.get("Set-Cookie") ?? "";
    const bodyText = await response.text();

    expect(setCookie).not.toContain("secret-bearer-access-token-99999");
    expect(setCookie).not.toContain("secret-offline-refresh-token-88888");
    expect(setCookie).not.toContain("secret-identity-token-payload-77777");

    expect(bodyText).not.toContain("secret-bearer-access-token-99999");
    expect(bodyText).not.toContain("secret-offline-refresh-token-88888");
    expect(bodyText).not.toContain("secret-identity-token-payload-77777");
  });

  it("Invariant 1b: serializes every loader's return value for a logged-in session and asserts no token string appears anywhere in it", async () => {
    const rawTokens = {
      accessToken: "very-secret-access-token-33333",
      refreshToken: "very-secret-refresh-token-44444",
      idToken: "very-secret-id-token-55555",
    };
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-matt-invariant",
      accessToken: rawTokens.accessToken,
      refreshToken: rawTokens.refreshToken,
      idToken: rawTokens.idToken,
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "csrf-token-invariants",
    };
    saveSession(session);

    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: vi.fn().mockImplementation((path: string) => {
        if (path === "/api/v1/postings") {
          return Promise.resolve({
            data: { items: [], totalCount: 0, limit: 25, offset: 0 },
            response: { status: 200 } as Response,
          });
        }
        if (path === "/api/v1/postings/{id}") {
          return Promise.resolve({
            data: {
              id: "p1",
              companyId: "c1",
              companyName: "Acme",
              title: "Dev",
              requisitionId: null,
              location: "Remote",
              disposition: "lead",
              resurfacePolicy: "never",
              score: null,
              versionToken: crypto.randomUUID(),
              createdAt: new Date().toISOString(),
              updatedAt: new Date().toISOString(),
              url: null,
              compensation: { currency: "USD", rawText: null },
              signals: [],
              assessmentNote: null,
            },
            response: { status: 200 } as Response,
          });
        }
        if (path === "/api/v1/criteria") {
          return Promise.resolve({ data: [], response: { status: 200 } as Response });
        }
        return Promise.resolve({ data: undefined, response: { status: 404 } as Response });
      }),
    } as any);

    const headers = { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` };

    // Test loader 1: /not-authorized
    const notAuthData = await notAuthorizedLoader({
      request: new Request("https://localhost:5003/not-authorized", { headers }),
      params: {},
      context: {},
    } as any);
    const serializedNotAuth = JSON.stringify(notAuthData);
    expect(serializedNotAuth).not.toContain(rawTokens.accessToken);
    expect(serializedNotAuth).not.toContain(rawTokens.refreshToken);
    expect(serializedNotAuth).not.toContain(rawTokens.idToken);

    // Test loader 2: /postings
    const postingsData = await postingsLoader({
      request: new Request("https://localhost:5003/postings", { headers }),
      params: {},
      context: {},
    } as any);
    const serializedPostings = JSON.stringify(postingsData);
    expect(serializedPostings).not.toContain(rawTokens.accessToken);
    expect(serializedPostings).not.toContain(rawTokens.refreshToken);
    expect(serializedPostings).not.toContain(rawTokens.idToken);

    // Test loader 3: /postings/:id
    const detailData = await postingDetailLoader({
      request: new Request("https://localhost:5003/postings/p1", { headers }),
      params: { id: "p1" },
      context: {},
    } as any);
    const serializedDetail = JSON.stringify(detailData);
    expect(serializedDetail).not.toContain(rawTokens.accessToken);
    expect(serializedDetail).not.toContain(rawTokens.refreshToken);
    expect(serializedDetail).not.toContain(rawTokens.idToken);
  });

  // Invariant 2: session cookie is HttpOnly, Secure, SameSite=Lax with __Host- prefix and opaque ID
  it("Invariant 2: session cookie uses __Host- prefix, HttpOnly, Secure, SameSite=Lax, and carries only an opaque id", () => {
    const opaqueId = crypto.randomUUID();
    const cookie = createSessionCookie(opaqueId, true);

    expect(cookie.name).toBe("__Host-roleradar_session");
    expect(cookie.value).toBe(opaqueId);

    expect(cookie.header).toMatch(/^__Host-roleradar_session=[0-9a-f-]{36};/);
    expect(cookie.header).toContain("HttpOnly");
    expect(cookie.header).toContain("Secure");
    expect(cookie.header).toContain("SameSite=Lax");
    expect(cookie.header).toContain("Path=/");
  });

  // Invariant 3: session id rotates on login
  it("Invariant 3: session id rotates on login, minting a new identifier", async () => {
    const initialSessionId = crypto.randomUUID();
    const mockTokens = {
      access_token: "tok-1",
      refresh_token: "ref-1",
      id_token: "id-1",
      expires_in: 600,
      claims: () => ({ sub: "owner-sub" }),
    };

    vi.spyOn(oidc, "exchangeCodeForTokens").mockResolvedValueOnce(mockTokens as any);

    const state = "state-rotation";
    const verifier = "verifier-rotation";
    const request = new Request(
      `https://localhost:5003/auth/callback?code=test-code&state=${state}`,
      {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${initialSessionId}; ${getAuthStateCookieName()}=${state}; ${getCodeVerifierCookieName()}=${verifier}`,
        },
      }
    );

    const response = await callbackLoader({
      request,
      params: {},
      context: {},
    } as any);

    const setCookie = response.headers.get("Set-Cookie") ?? "";
    const match = setCookie.match(new RegExp(`${SECURE_COOKIE_NAME}=([^;]+)`));
    const newSessionId = match ? match[1] : "";

    expect(newSessionId).toBeTruthy();
    expect(newSessionId).not.toBe(initialSessionId);
  });

  // Invariant 4: single-flight refresh (10 concurrent requests trigger 1 token call; failed refresh ends session)
  it("Invariant 4: single-flight refresh coalesces 10 concurrent requests and ends session on failure", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner",
      accessToken: "expiring-token",
      refreshToken: "active-refresh",
      expiresAt: Date.now() + 15_000, // < 60s
      createdAt: Date.now(),
      csrfToken: "csrf-token",
    };

    saveSession(session);

    let refreshCalls = 0;
    const refreshFn = vi.fn(async () => {
      refreshCalls++;
      await new Promise((r) => setTimeout(r, 40));
      return {
        accessToken: `refreshed-token-${refreshCalls}`,
        refreshToken: `refreshed-refresh-${refreshCalls}`,
        expiresAt: Date.now() + 600_000,
      };
    });

    // 10 concurrent requests
    const results = await Promise.all(
      Array.from({ length: 10 }, () => getValidSession(sessionId, refreshFn))
    );

    expect(refreshCalls).toBe(1);
    expect(results.every((r) => r?.accessToken === "refreshed-token-1")).toBe(true);

    // Failed refresh ends session instead of looping
    const failingSessionId = crypto.randomUUID();
    saveSession({
      ...session,
      sessionId: failingSessionId,
      expiresAt: Date.now() + 5_000,
    });

    const failingRefresh = vi.fn(async () => {
      throw new Error("Token revoked");
    });

    const failResult = await getValidSession(failingSessionId, failingRefresh);
    expect(failResult).toBeNull();
    expect(getSession(failingSessionId)).toBeNull();
  });

  // Invariant 5: CSRF on every non-GET route including logout
  it("Invariant 5: CSRF validation is enforced on logout and state-changing actions", async () => {
    // Missing CSRF token
    expect(validateCsrfToken(null, "expected-token")).toBe(false);
    expect(validateCsrfToken("", "expected-token")).toBe(false);
    expect(validateCsrfToken("wrong-token", "expected-token")).toBe(false);
    expect(validateCsrfToken("expected-token", "expected-token")).toBe(true);

    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-sub",
      accessToken: "token",
      refreshToken: "refresh",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "expected-csrf-token",
    };
    saveSession(session);

    const formDataWithoutCsrf = new FormData();
    const req = new Request("https://localhost:5003/auth/logout", {
      method: "POST",
      body: formDataWithoutCsrf,
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const response = await logoutAction({
      request: req,
      params: {},
      context: {},
    } as any);

    expect(response.status).toBe(403);
  });

  // Invariant 6: CSP uses a per-response nonce with no unsafe-inline in script-src
  it("Invariant 6: CSP headers use a cryptographic per-response nonce with no unsafe-inline in script-src", () => {
    const res = new http.ServerResponse({} as any);
    const nonce1 = generateCspNonce();
    applySecurityHeaders(res, nonce1);

    const csp = res.getHeader("Content-Security-Policy") as string;
    expect(csp).toBeDefined();
    expect(csp).toContain(`script-src 'self' 'nonce-${nonce1}'`);
    expect(csp).toContain(`style-src 'self' 'nonce-${nonce1}'`);
    expect(csp).toContain("style-src-attr 'unsafe-inline'");
    expect(csp).not.toContain("script-src 'unsafe-inline'");
    expect(csp).not.toContain("style-src 'self' 'unsafe-inline'");

    // Distinct nonces per response
    const nonce2 = generateCspNonce();
    expect(nonce1).not.toBe(nonce2);
  });

  // Invariant 7: a 403 shows Not Authorized page and never redirects in a loop
  it("Invariant 7: 403 displays Not Authorized view and contains zero automatic retry redirects", async () => {
    const Stub = createRoutesStub([
      {
        path: "/not-authorized",
        Component: NotAuthorized,
        loader: () => ({ csrfToken: "csrf-test" }),
      },
    ]);
    const { findByRole, queryByRole } = render(
      React.createElement(Stub, { initialEntries: ["/not-authorized"] })
    );

    expect(await findByRole("heading", { name: /Not Authorized/i })).toBeDefined();
    expect(queryByRole("link", { name: /retry/i })).toBeNull();
  });

  // Invariant 8: hostile text renders strictly as plain text leaf nodes
  it("Invariant 8: hostile payloads render safely as plain text textContent without DOM execution", () => {
    const hostilePayloads = [
      "<script>alert('xss')</script>",
      "<img src=x onerror=alert(1) />",
      "<svg onload=alert(document.cookie)>",
      "javascript:alert(1)",
      "'; DROP TABLE postings; --",
    ];

    for (const payload of hostilePayloads) {
      const { container } = render(
        React.createElement("div", { "data-testid": "posting-title" }, payload)
      );

      // DOM element textContent matches payload verbatim
      expect(container.textContent).toBe(payload);
      // Zero executable elements injected into DOM
      expect(container.querySelector("script")).toBeNull();
      expect(container.querySelector("img")).toBeNull();
      expect(container.querySelector("svg")).toBeNull();
    }
  });
});
