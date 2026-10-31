import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import {
  encryptSession,
  decryptSession,
  getSessionEncryptionKey,
  setSessionEncryptionKeyForTesting,
  saveSession,
  getSession,
  getValidSession,
  createSessionCookie,
  destroySessionCookie,
  parseSessionCookie,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("session.server", () => {
  const dummyKey = crypto.randomBytes(32);

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
  });

  it("encrypts and decrypts session data with AES-256-GCM authenticated encryption", () => {
    const session: UserSession = {
      sessionId: crypto.randomUUID(),
      ownerSub: "owner-user-sub-001",
      accessToken: "mock-access-token-secret-xyz",
      refreshToken: "mock-refresh-token-secret-abc",
      idToken: "mock-id-token-header-payload",
      expiresAt: Date.now() + 3600_000,
      createdAt: Date.now(),
      csrfToken: crypto.randomBytes(16).toString("hex"),
    };

    const record = encryptSession(session);
    expect(record.ciphertext).toBeDefined();
    expect(record.iv).toBeDefined();
    expect(record.tag).toBeDefined();

    // Verify ciphertext does not contain plain text tokens
    expect(record.ciphertext).not.toContain("mock-access-token");
    expect(record.ciphertext).not.toContain("mock-refresh-token");

    const decrypted = decryptSession(record);
    expect(decrypted).toEqual(session);
  });

  it("fails closed when key is missing or invalid outside development", () => {
    const originalEnv = process.env.NODE_ENV;
    const originalKey = process.env.SESSION_ENCRYPTION_KEY;

    try {
      setSessionEncryptionKeyForTesting(null);
      process.env.NODE_ENV = "production";
      process.env.SESSION_ENCRYPTION_KEY = "";

      expect(() => getSessionEncryptionKey()).toThrow(
        /SESSION_ENCRYPTION_KEY is required outside Development/
      );

      // Invalid key length (not 32 bytes base64)
      process.env.SESSION_ENCRYPTION_KEY = Buffer.from("short-key").toString("base64");
      expect(() => getSessionEncryptionKey()).toThrow(
        /SESSION_ENCRYPTION_KEY must be exactly 32 bytes base64/
      );
    } finally {
      process.env.NODE_ENV = originalEnv;
      process.env.SESSION_ENCRYPTION_KEY = originalKey;
      setSessionEncryptionKeyForTesting(dummyKey);
    }
  });

  it("coalesces 10 concurrent requests near token expiry into exactly 1 refresh call (single-flight)", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-user-sub-001",
      accessToken: "initial-access-token",
      refreshToken: "initial-refresh-token",
      expiresAt: Date.now() + 30_000, // < 60s remaining triggers refresh
      createdAt: Date.now(),
      csrfToken: "csrf-token-123",
    };

    saveSession(session);

    let refreshCallCount = 0;
    const refreshFn = vi.fn(async (_refreshToken: string) => {
      refreshCallCount++;
      // Simulate network delay to ensure concurrency overlap
      await new Promise((resolve) => setTimeout(resolve, 50));
      return {
        accessToken: `refreshed-access-token-${refreshCallCount}`,
        refreshToken: `new-refresh-token-${refreshCallCount}`,
        expiresAt: Date.now() + 600_000,
      };
    });

    // Fire 10 concurrent requests simultaneously
    const requests = Array.from({ length: 10 }, () =>
      getValidSession(sessionId, refreshFn)
    );

    const results = await Promise.all(requests);

    // Exactly one refresh call must occur across all 10 requests
    expect(refreshCallCount).toBe(1);
    expect(refreshFn).toHaveBeenCalledTimes(1);

    for (const res of results) {
      expect(res).not.toBeNull();
      expect(res?.accessToken).toBe("refreshed-access-token-1");
      expect(res?.refreshToken).toBe("new-refresh-token-1");
    }
  });

  it("destroys the session and returns null when token refresh fails instead of looping", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-user-sub-001",
      accessToken: "expired-access-token",
      refreshToken: "rejected-refresh-token",
      expiresAt: Date.now() + 10_000, // near expiry
      createdAt: Date.now(),
      csrfToken: "csrf-token-123",
    };

    saveSession(session);

    const failingRefreshFn = vi.fn(async () => {
      throw new Error("Invalid grant: refresh token expired or revoked");
    });

    const result = await getValidSession(sessionId, failingRefreshFn);

    expect(result).toBeNull();
    // Verify session store eviction
    expect(getSession(sessionId)).toBeNull();
  });

  it("formats session cookies with __Host- prefix, HttpOnly, Secure, SameSite=Lax, and opaque ID only", () => {
    const opaqueId = crypto.randomUUID();
    const cookie = createSessionCookie(opaqueId, true);

    expect(cookie.name).toBe(SECURE_COOKIE_NAME);
    expect(cookie.value).toBe(opaqueId);
    expect(cookie.header).toContain("__Host-roleradar_session=");
    expect(cookie.header).toContain("HttpOnly");
    expect(cookie.header).toContain("Secure");
    expect(cookie.header).toContain("SameSite=Lax");
    expect(cookie.header).toContain("Path=/");

    // Tokens must never appear in cookie value or header
    expect(cookie.header).not.toContain("access_token");
    expect(cookie.header).not.toContain("bearer");

    const parsed = parseSessionCookie(`other=123; ${cookie.name}=${opaqueId}; foo=bar`);
    expect(parsed).toBe(opaqueId);

    const destroyed = destroySessionCookie(true);
    expect(destroyed.header).toContain("Max-Age=0");
  });

  it("in production, staging, or unset NODE_ENV accepts ONLY __Host-roleradar_session and ignores planted unprefixed cookies", () => {
    const originalEnv = process.env.NODE_ENV;
    const originalCookieSecure = process.env.COOKIE_SECURE;
    delete process.env.COOKIE_SECURE;

    try {
      const plantedId = crypto.randomUUID();
      const secureId = crypto.randomUUID();

      const cookieHeaderUnprefixed = `roleradar_session=${plantedId}; other=abc`;
      const cookieHeaderSecure = `__Host-roleradar_session=${secureId}; other=abc`;
      const cookieHeaderBoth = `roleradar_session=${plantedId}; __Host-roleradar_session=${secureId}`;

      // Production strictly requires __Host- and ignores unprefixed
      process.env.NODE_ENV = "production";
      expect(parseSessionCookie(cookieHeaderUnprefixed)).toBeNull();
      expect(parseSessionCookie(cookieHeaderSecure)).toBe(secureId);
      expect(parseSessionCookie(cookieHeaderBoth)).toBe(secureId);

      // Staging strictly requires __Host- and ignores unprefixed
      process.env.NODE_ENV = "staging";
      expect(parseSessionCookie(cookieHeaderUnprefixed)).toBeNull();
      expect(parseSessionCookie(cookieHeaderSecure)).toBe(secureId);
      expect(parseSessionCookie(cookieHeaderBoth)).toBe(secureId);

      // Unset NODE_ENV strictly requires __Host- and ignores unprefixed
      delete process.env.NODE_ENV;
      expect(parseSessionCookie(cookieHeaderUnprefixed)).toBeNull();
      expect(parseSessionCookie(cookieHeaderSecure)).toBe(secureId);
      expect(parseSessionCookie(cookieHeaderBoth)).toBe(secureId);

      // Development accepts unprefixed cookie
      process.env.NODE_ENV = "development";
      expect(parseSessionCookie(cookieHeaderUnprefixed)).toBe(plantedId);
      expect(parseSessionCookie(cookieHeaderSecure)).toBe(secureId);

      // Test accepts unprefixed cookie
      process.env.NODE_ENV = "test";
      expect(parseSessionCookie(cookieHeaderUnprefixed)).toBe(plantedId);
      expect(parseSessionCookie(cookieHeaderSecure)).toBe(secureId);
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

  it("enforces absolute session lifetime (default 12 hours) and drops expired sessions on access", () => {
    const originalEnv = process.env.NODE_ENV;
    const originalLifetime = process.env.SESSION_ABSOLUTE_LIFETIME_MS;

    try {
      const sessionId = crypto.randomUUID();
      const thirteenHoursAgo = Date.now() - 13 * 3600_000;
      const session: UserSession = {
        sessionId,
        ownerSub: "owner-001",
        accessToken: "valid-access-token",
        refreshToken: "valid-refresh-token",
        expiresAt: Date.now() + 3600_000, // Token is still "valid"
        createdAt: thirteenHoursAgo, // Created 13 hours ago (exceeds 12-hour absolute lifetime)
        csrfToken: "csrf-token-abc",
      };

      saveSession(session);

      // On access, session exceeding absolute lifetime must be dropped from store and return null
      const retrieved = getSession(sessionId);
      expect(retrieved).toBeNull();

      // Verify configurable absolute lifetime
      process.env.SESSION_ABSOLUTE_LIFETIME_MS = "1000"; // 1 second
      const shortLivedId = crypto.randomUUID();
      const shortSession: UserSession = {
        sessionId: shortLivedId,
        ownerSub: "owner-002",
        accessToken: "valid-access-token",
        refreshToken: "valid-refresh-token",
        expiresAt: Date.now() + 3600_000,
        createdAt: Date.now() - 1500, // 1.5 seconds ago
        csrfToken: "csrf-token-def",
      };

      saveSession(shortSession);
      expect(getSession(shortLivedId)).toBeNull();
    } finally {
      process.env.NODE_ENV = originalEnv;
      if (originalLifetime !== undefined) {
        process.env.SESSION_ABSOLUTE_LIFETIME_MS = originalLifetime;
      } else {
        delete process.env.SESSION_ABSOLUTE_LIFETIME_MS;
      }
    }
  });
});
