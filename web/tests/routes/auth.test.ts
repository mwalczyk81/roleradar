import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import { loader as callbackLoader } from "../../app/routes/auth.callback.js";
import { action as logoutAction } from "../../app/routes/auth.logout.js";
import * as oidc from "../../app/lib/oidc.server.js";
import {
  getAuthStateCookieName,
  getCodeVerifierCookieName,
} from "../../app/lib/auth-guard.server.js";
import {
  getSession,
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("auth routes", () => {
  const dummyKey = crypto.randomBytes(32);

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
  });

  describe("GET /auth/callback", () => {
    it("exchanges code, rotates session id, sets __Host- cookie without leaking tokens, and redirects to /postings", async () => {
      const mockTokens = {
        access_token: "mock-bearer-access-token-12345",
        refresh_token: "mock-offline-refresh-token-67890",
        id_token: "mock-identity-token-payload",
        expires_in: 600,
        claims: () => ({ sub: "test-owner-sub", preferred_username: "matt" }),
      };

      vi.spyOn(oidc, "exchangeCodeForTokens").mockResolvedValueOnce(mockTokens as any);

      // Transient state cookie simulating code verifier & expected state stored during auth initiation
      const state = "test-state-abc";
      const codeVerifier = "test-code-verifier-xyz";
      const transientCookie = `${getAuthStateCookieName()}=${state}; ${getCodeVerifierCookieName()}=${codeVerifier}`;

      const request = new Request(
        `https://localhost:5003/auth/callback?code=valid-code&state=${state}`,
        {
          headers: {
            Cookie: transientCookie,
          },
        }
      );

      const response = (await callbackLoader({
        request,
        params: {},
        context: {},
      } as any)) as Response;

      // Must be a 302 redirect to /postings
      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toBe("/postings");

      // Verify session cookie header
      const setCookie = response.headers.get("Set-Cookie");
      expect(setCookie).toBeTruthy();
      expect(setCookie).toContain(`${SECURE_COOKIE_NAME}=`);
      expect(setCookie).toContain("HttpOnly");
      expect(setCookie).toContain("Secure");
      expect(setCookie).toContain("SameSite=Lax");

      // SECURITY INVARIANT: Tokens must NEVER appear in Set-Cookie header or response body
      expect(setCookie).not.toContain("mock-bearer-access-token");
      expect(setCookie).not.toContain("mock-offline-refresh-token");
      expect(setCookie).not.toContain("mock-identity-token");

      const bodyText = await response.text();
      expect(bodyText).not.toContain("mock-bearer-access-token");
      expect(bodyText).not.toContain("mock-offline-refresh-token");

      // Verify session is saved in server memory
      const match = setCookie?.match(new RegExp(`${SECURE_COOKIE_NAME}=([^;]+)`));
      const sessionId = match ? match[1] : "";
      expect(sessionId).toBeTruthy();

      const saved = getSession(sessionId);
      expect(saved).not.toBeNull();
      expect(saved?.accessToken).toBe("mock-bearer-access-token-12345");
      expect(saved?.ownerSub).toBe("test-owner-sub");
      expect(saved?.ownerUsername).toBe("matt");
    });

    it("fails login with 400 when id_token has no sub claim", async () => {
      const mockTokensNoSub = {
        access_token: "mock-bearer-token",
        refresh_token: "mock-refresh-token",
        id_token: "mock-id-token",
        expires_in: 600,
        claims: () => ({}), // Missing 'sub' claim
      };

      vi.spyOn(oidc, "exchangeCodeForTokens").mockResolvedValueOnce(mockTokensNoSub as any);

      const state = "test-state-no-sub";
      const codeVerifier = "test-verifier";
      const transientCookie = `${getAuthStateCookieName()}=${state}; ${getCodeVerifierCookieName()}=${codeVerifier}`;

      const request = new Request(
        `https://localhost:5003/auth/callback?code=valid-code&state=${state}`,
        {
          headers: {
            Cookie: transientCookie,
          },
        }
      );

      const response = (await callbackLoader({
        request,
        params: {},
        context: {},
      } as any)) as Response;

      expect(response.status).toBe(400);
      const text = await response.text();
      expect(text).toContain("missing valid subject claim");
    });

    it("redirects to original return-to path stored in auth-state cookie", async () => {
      const mockTokens = {
        access_token: "mock-access-token",
        refresh_token: "mock-refresh-token",
        id_token: "mock-id-token",
        expires_in: 600,
        claims: () => ({ sub: "test-owner-sub" }),
      };

      vi.spyOn(oidc, "exchangeCodeForTokens").mockResolvedValueOnce(mockTokens as any);

      const state = "test-state-return-to";
      const codeVerifier = "test-verifier-return-to";
      const returnTo = "/postings/role-999?sort=score";
      const statePayload = Buffer.from(
        JSON.stringify({ state, returnTo }),
        "utf8"
      ).toString("base64url");

      const request = new Request(
        `https://localhost:5003/auth/callback?code=valid-code&state=${state}`,
        {
          headers: {
            Cookie: `${getAuthStateCookieName()}=${statePayload}; ${getCodeVerifierCookieName()}=${codeVerifier}`,
          },
        }
      );

      const response = (await callbackLoader({
        request,
        params: {},
        context: {},
      } as any)) as Response;

      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toBe(returnTo);
    });

    it("handles callback replay when transient state cookie is missing: logs single clean line without stack trace and returns replay prompt", async () => {
      const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

      // No transient cookies (e.g. reloaded after first exchange consumed them)
      const request = new Request(
        "https://localhost:5003/auth/callback?code=already-used-code&state=used-state"
      );

      const result = await callbackLoader({
        request,
        params: {},
        context: {},
      } as any);

      expect(result).toEqual({ replay: true });
      expect(warnSpy).toHaveBeenCalledTimes(1);
      const logMessage = warnSpy.mock.calls[0][0];
      expect(logMessage).toContain("[WARN]");
      expect(logMessage).toContain("replay");
      expect(logMessage).not.toContain("Error:");
      expect(logMessage).not.toContain("    at ");
    });

    it("handles callback replay when exchangeCodeForTokens fails: logs single clean line without stack trace and returns replay prompt", async () => {
      const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

      vi.spyOn(oidc, "exchangeCodeForTokens").mockRejectedValueOnce(
        new Error("invalid_grant: authorization code has already been redeemed")
      );

      const state = "test-state-replay";
      const codeVerifier = "test-verifier-replay";
      const transientCookie = `${getAuthStateCookieName()}=${state}; ${getCodeVerifierCookieName()}=${codeVerifier}`;

      const request = new Request(
        `https://localhost:5003/auth/callback?code=already-used-code&state=${state}`,
        {
          headers: {
            Cookie: transientCookie,
          },
        }
      );

      const result = await callbackLoader({
        request,
        params: {},
        context: {},
      } as any);

      expect(result).toEqual({ replay: true });
      expect(warnSpy).toHaveBeenCalledTimes(1);
      const logMessage = warnSpy.mock.calls[0][0];
      expect(logMessage).toContain("[WARN]");
      expect(logMessage).toContain("invalid_grant");
      expect(logMessage).not.toContain("    at ");
    });
  });

  describe("POST /auth/logout", () => {
    it("enforces CSRF validation and rejects logout without valid _csrf", async () => {
      const sessionId = crypto.randomUUID();
      saveSession({
        sessionId,
        ownerSub: "owner",
        accessToken: "token",
        refreshToken: "refresh",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken: "expected-csrf-token",
      });

      const formData = new FormData();
      // Omitting _csrf
      const request = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        body: formData,
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const response = await logoutAction({
        request,
        params: {},
        context: {},
      } as any);

      // Rejection: 403 or 400 for CSRF invalidation
      expect([400, 403]).toContain(response.status);
    });

    it("destroys session, clears session cookie, and redirects to Identity end-session endpoint", async () => {
      const sessionId = crypto.randomUUID();
      const csrfToken = "valid-csrf-token-123";
      const session: UserSession = {
        sessionId,
        ownerSub: "owner-sub",
        accessToken: "token",
        refreshToken: "refresh",
        idToken: "sample-id-token",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken,
      };

      saveSession(session);

      const formData = new FormData();
      formData.append("_csrf", csrfToken);

      const request = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        body: formData,
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      vi.spyOn(oidc, "getEndSessionUrl").mockResolvedValueOnce(
        new URL("https://identity:5001/connect/endsession?post_logout_redirect_uri=https://localhost:5003/auth/logout")
      );

      const response = await logoutAction({
        request,
        params: {},
        context: {},
      } as any);

      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toContain("/connect/endsession");

      // Verify session expired in cookie
      const setCookie = response.headers.get("Set-Cookie");
      expect(setCookie).toContain("Max-Age=0");

      // Verify session destroyed in store
      expect(getSession(sessionId)).toBeNull();
    });

    it("when session is missing or expired, clears session cookie and redirects without requiring CSRF", async () => {
      // Missing session or expired session: no CSRF token provided
      const formData = new FormData();
      const request = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        body: formData,
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=nonexistent-or-expired-session`,
        },
      });

      vi.spyOn(oidc, "getEndSessionUrl").mockResolvedValueOnce(
        new URL("https://identity:5001/connect/endsession?post_logout_redirect_uri=https://localhost:5003/auth/logout")
      );

      const response = await logoutAction({
        request,
        params: {},
        context: {},
      } as any);

      // Must NOT fail with 403 CSRF error; must redirect and clear cookie
      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toContain("/connect/endsession");

      const setCookie = response.headers.get("Set-Cookie");
      expect(setCookie).toContain("Max-Age=0");
    });

    it("when getEndSessionUrl fails, logs single WARN line without tokens and falls back to /postings", async () => {
      const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

      const sessionId = crypto.randomUUID();
      const csrfToken = "valid-csrf-token-123";
      const session: UserSession = {
        sessionId,
        ownerSub: "owner-sub",
        accessToken: "sensitive-access-token-12345",
        refreshToken: "sensitive-refresh-token-67890",
        idToken: "sensitive-id-token-abcde",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken,
      };

      saveSession(session);

      const formData = new FormData();
      formData.append("_csrf", csrfToken);

      const request = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        body: formData,
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      vi.spyOn(oidc, "getEndSessionUrl").mockRejectedValueOnce(
        new Error("Identity end-session discovery unavailable")
      );

      const response = await logoutAction({
        request,
        params: {},
        context: {},
      } as any);

      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toBe("/postings");

      // Verify session expired in cookie
      const setCookie = response.headers.get("Set-Cookie");
      expect(setCookie).toContain("Max-Age=0");

      // Verify single clean WARN line logged with zero tokens leaked
      expect(warnSpy).toHaveBeenCalledTimes(1);
      const logMessage = warnSpy.mock.calls[0][0];
      expect(logMessage).toContain("[WARN]");
      expect(logMessage).not.toContain("sensitive-access-token");
      expect(logMessage).not.toContain("sensitive-refresh-token");
      expect(logMessage).not.toContain("sensitive-id-token");

      // Verify session destroyed in store
      expect(getSession(sessionId)).toBeNull();
    });
  });
});
