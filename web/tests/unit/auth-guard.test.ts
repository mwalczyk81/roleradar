import { describe, it, expect, beforeEach } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import {
  requireUserSession,
  sanitizeReturnTo,
  parseAuthStateCookie,
  parseCodeVerifierCookie,
  SECURE_AUTH_STATE_COOKIE,
  INSECURE_AUTH_STATE_COOKIE,
  SECURE_CODE_VERIFIER_COOKIE,
  INSECURE_CODE_VERIFIER_COOKIE,
} from "../../app/lib/auth-guard.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("auth-guard.server", () => {
  const dummyKey = crypto.randomBytes(32);
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
  });

  it("redirects unauthenticated requests to Identity with PKCE challenge and state in transient cookie", async () => {
    const request = new Request("https://localhost:5003/postings");

    let thrownResponse: Response | null = null;
    try {
      await requireUserSession(request);
    } catch (err: any) {
      if (err instanceof Response) {
        thrownResponse = err;
      } else {
        throw err;
      }
    }

    expect(thrownResponse).not.toBeNull();
    expect(thrownResponse?.status).toBe(302);

    const location = thrownResponse?.headers.get("Location");
    expect(location).toContain("/connect/authorize");
    expect(location).toContain("client_id=roleradar-web");
    expect(location).toContain("code_challenge=");

    // Transient cookies must store state and code_verifier for callback verification
    const setCookie = thrownResponse?.headers.get("Set-Cookie");
    expect(setCookie).toContain("roleradar_auth_state=");
    expect(setCookie).toContain("roleradar_code_verifier=");
  });

  it("returns active session for authenticated requests with valid session cookie", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-sub-123",
      accessToken: "access-token-123",
      refreshToken: "refresh-token-123",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "csrf-token-123",
    };

    saveSession(session);

    const request = new Request("https://localhost:5003/postings", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await requireUserSession(request);
    expect(result).not.toBeNull();
    expect(result.sessionId).toBe(sessionId);
    expect(result.ownerSub).toBe("owner-sub-123");
  });

  describe("sanitizeReturnTo & open-redirect defense", () => {
    it("preserves valid relative paths and query strings", () => {
      expect(sanitizeReturnTo("/postings")).toBe("/postings");
      expect(sanitizeReturnTo("/postings/123?q=lead&sort=score")).toBe(
        "/postings/123?q=lead&sort=score"
      );
      expect(sanitizeReturnTo("/companies/abc-def")).toBe("/companies/abc-def");
    });

    it("sanitizes open-redirect attempts to safe default /postings", () => {
      expect(sanitizeReturnTo("https://evil.com")).toBe("/postings");
      expect(sanitizeReturnTo("http://evil.com/postings")).toBe("/postings");
      expect(sanitizeReturnTo("//evil.com")).toBe("/postings");
      expect(sanitizeReturnTo("//evil.com/postings")).toBe("/postings");
      expect(sanitizeReturnTo("/\\evil.com")).toBe("/postings");
      expect(sanitizeReturnTo("///evil.com")).toBe("/postings");
      expect(sanitizeReturnTo("javascript:alert(1)")).toBe("/postings");
      expect(sanitizeReturnTo("data:text/html,evil")).toBe("/postings");
      expect(sanitizeReturnTo("")).toBe("/postings");
      expect(sanitizeReturnTo(null)).toBe("/postings");
      expect(sanitizeReturnTo(undefined)).toBe("/postings");
      // Rejects ASCII control characters anywhere in the value (0x00-0x1F, 0x7F)
      expect(sanitizeReturnTo("/\t/evil")).toBe("/postings");
      expect(sanitizeReturnTo("/\n/evil")).toBe("/postings");
      expect(sanitizeReturnTo("/\r/evil")).toBe("/postings");
      expect(sanitizeReturnTo("/postings\x00/evil")).toBe("/postings");
      expect(sanitizeReturnTo("/postings/evil\x7F")).toBe("/postings");
      expect(sanitizeReturnTo("/\x1B/escape")).toBe("/postings");
    });
  });

  describe("production auth-state & code-verifier cookie parsing", () => {
    it("in production, staging, or unset NODE_ENV accepts ONLY __Host- prefixed cookies and ignores planted unprefixed cookies", () => {
      const originalEnv = process.env.NODE_ENV;
      const originalCookieSecure = process.env.COOKIE_SECURE;
      delete process.env.COOKIE_SECURE;

      try {
        const plantedState = "planted-state";
        const secureState = "secure-state";
        const plantedVerifier = "planted-verifier";
        const secureVerifier = "secure-verifier";

        const unprefixedHeader = `${INSECURE_AUTH_STATE_COOKIE}=${plantedState}; ${INSECURE_CODE_VERIFIER_COOKIE}=${plantedVerifier}`;
        const secureHeader = `${SECURE_AUTH_STATE_COOKIE}=${secureState}; ${SECURE_CODE_VERIFIER_COOKIE}=${secureVerifier}`;

        // Production rejects unprefixed cookies
        process.env.NODE_ENV = "production";
        expect(parseAuthStateCookie(unprefixedHeader)).toBeNull();
        expect(parseCodeVerifierCookie(unprefixedHeader)).toBeNull();
        expect(parseAuthStateCookie(secureHeader)).toBe(secureState);
        expect(parseCodeVerifierCookie(secureHeader)).toBe(secureVerifier);

        // Staging rejects unprefixed cookies
        process.env.NODE_ENV = "staging";
        expect(parseAuthStateCookie(unprefixedHeader)).toBeNull();
        expect(parseCodeVerifierCookie(unprefixedHeader)).toBeNull();
        expect(parseAuthStateCookie(secureHeader)).toBe(secureState);
        expect(parseCodeVerifierCookie(secureHeader)).toBe(secureVerifier);

        // Unset NODE_ENV rejects unprefixed cookies
        delete process.env.NODE_ENV;
        expect(parseAuthStateCookie(unprefixedHeader)).toBeNull();
        expect(parseCodeVerifierCookie(unprefixedHeader)).toBeNull();
        expect(parseAuthStateCookie(secureHeader)).toBe(secureState);
        expect(parseCodeVerifierCookie(secureHeader)).toBe(secureVerifier);

        // Development mode accepts unprefixed cookies
        process.env.NODE_ENV = "development";
        expect(parseAuthStateCookie(unprefixedHeader)).toBe(plantedState);
        expect(parseCodeVerifierCookie(unprefixedHeader)).toBe(plantedVerifier);

        // Test mode accepts unprefixed cookies
        process.env.NODE_ENV = "test";
        expect(parseAuthStateCookie(unprefixedHeader)).toBe(plantedState);
        expect(parseCodeVerifierCookie(unprefixedHeader)).toBe(plantedVerifier);
      } finally {
        if (originalEnv !== undefined) {
          process.env.NODE_ENV = originalEnv;
        } else {
          delete process.env.NODE_ENV;
        }
        if (originalCookieSecure !== undefined) {
          process.env.COOKIE_SECURE = originalCookieSecure;
        } else {
          delete process.env.COOKIE_SECURE;
        }
      }
    });
  });
});
