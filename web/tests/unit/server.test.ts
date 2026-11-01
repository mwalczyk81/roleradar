import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import http from "node:http";
import {
  createWebServer,
  applySecurityHeaders,
  generateCspNonce,
} from "../../server.js";

describe("server.ts TLS and CSP invariants", () => {
  const originalEnv = { ...process.env };

  beforeEach(() => {
    process.env = { ...originalEnv };
  });

  afterEach(() => {
    process.env = { ...originalEnv };
    vi.restoreAllMocks();
  });

  it("sets CSP with strict script-src and style-src-attr 'unsafe-inline'", () => {
    const res = new http.ServerResponse({} as unknown as http.IncomingMessage);
    const nonce = generateCspNonce();
    applySecurityHeaders(res, nonce);

    const csp = res.getHeader("Content-Security-Policy") as string;
    expect(csp).toBeDefined();
    expect(csp).toContain(`script-src 'self' 'nonce-${nonce}'`);
    expect(csp).toContain(`style-src 'self' 'nonce-${nonce}'`);
    expect(csp).toContain("style-src-attr 'unsafe-inline'");
    expect(csp).not.toContain("script-src 'unsafe-inline'");
  });

  it("outside development/test, refuses to start and exits with 1 when TLS cert or key is missing", () => {
    process.env.NODE_ENV = "production";
    process.env.SESSION_ENCRYPTION_KEY = Buffer.alloc(32, 1).toString("base64");
    process.env.ROLERADAR_WEB_CLIENT_SECRET = "test-secret";
    process.env.HTTPS_CERT_PATH = "/nonexistent/path/to/missing.crt";
    process.env.HTTPS_CERT_KEY_PATH = "/nonexistent/path/to/missing.key";

    const exitSpy = vi.spyOn(process, "exit").mockImplementation((code?: string | number | null | undefined) => {
      throw new Error(`process.exit called with ${code}`);
    });
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});

    expect(() => createWebServer()).toThrow("process.exit called with 1");
    expect(exitSpy).toHaveBeenCalledWith(1);
    expect(errorSpy).toHaveBeenCalledWith(
      expect.stringContaining("Production requires TLS")
    );
  });
});
