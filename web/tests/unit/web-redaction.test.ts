import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { log } from "../../app/lib/log.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";
import { callLoader } from "../test-helpers.js";

// Feature 013 US1 (FR-012, FR-013): no secret reaches the web's log output, through the real failure paths and through
// structured fields. Every value is a fictional sentinel.

const SECRETS = {
  clientSecret: "SENTINEL-CLIENT-SECRET-7f3a",
  sessionKey: "U0VOVElORUwtU0VTU0lPTi1LRVktN2YzYQ==", // gitleaks:allow (fictional redaction test vector, not a credential)
  access: "SENTINEL-ACCESS-7f3a",
  refresh: "SENTINEL-REFRESH-7f3a",
  jwt: "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTCJ9.U0VOVElORUwtU0lH", // gitleaks:allow (fictional redaction test vector, not a credential)
};

function captureAll() {
  const written: string[] = [];
  const push = (chunk: string | Uint8Array) => {
    written.push(String(chunk));
    return true;
  };
  vi.spyOn(process.stdout, "write").mockImplementation(push);
  vi.spyOn(process.stderr, "write").mockImplementation(push);
  return written;
}

function assertNoSecrets(written: string[]) {
  const all = written.join("");
  for (const secret of Object.values(SECRETS)) {
    expect(all).not.toContain(secret);
  }
}

describe("web redaction", () => {
  const originalEnv = { ...process.env };

  beforeEach(() => {
    process.env = { ...originalEnv, ROLERADAR_LOG_FORMAT: "json" };
    vi.resetModules();
  });

  afterEach(() => {
    process.env = { ...originalEnv };
    vi.restoreAllMocks();
  });

  it("the session key failure paths log without the key", async () => {
    const written = captureAll();
    process.env.NODE_ENV = "production";
    process.env.SESSION_ENCRYPTION_KEY = SECRETS.sessionKey;
    const session = await import("../../app/lib/session.server");

    expect(() => session.getSessionEncryptionKey()).toThrow();
    expect(written.length).toBeGreaterThan(0);
    assertNoSecrets(written);
  });

  it("the client secret failure path logs without any secret", async () => {
    const written = captureAll();
    process.env.NODE_ENV = "production";
    delete process.env.ROLERADAR_WEB_CLIENT_SECRET;
    process.env.SESSION_ENCRYPTION_KEY = SECRETS.sessionKey;
    const oidc = await import("../../app/lib/oidc.server");

    expect(() => oidc.getWebClientSecret()).toThrow();
    expect(written.length).toBeGreaterThan(0);
    assertNoSecrets(written);
  });

  it("a token exchange failure whose message carries tokens is logged scrubbed", async () => {
    const written = captureAll();
    const oidc = await import("../../app/lib/oidc.server");
    vi.spyOn(oidc, "exchangeCodeForTokens").mockRejectedValueOnce(
      new Error(`invalid_grant: Bearer ${SECRETS.access} refresh_token=${SECRETS.refresh} id ${SECRETS.jwt}`)
    );
    const { loader } = await import("../../app/routes/auth.callback");
    const guard = await import("../../app/lib/auth-guard.server");
    const state = "fictional-state";
    const request = new Request(`https://localhost:5003/auth/callback?code=fictional-code&state=${state}`, {
      headers: { Cookie: `${guard.getAuthStateCookieName()}=${state}; ${guard.getCodeVerifierCookieName()}=fictional-verifier` },
    });

    await runWithRequestContext({ correlationId: "test-corr-web-0004", nonce: "" }, async () => {
      await callLoader(loader, request);
    });

    expect(written.length).toBeGreaterThan(0);
    assertNoSecrets(written);
  });

  it("structured fields carrying tokens are redacted by key and by shape", () => {
    const written = captureAll();

    log.warn("refresh failed", {
      props: { accessToken: SECRETS.access, refresh_token: SECRETS.refresh, upstream: `Bearer ${SECRETS.jwt}` },
      error: new Error(`client_secret=${SECRETS.clientSecret} rejected; Bearer ${SECRETS.access}`),
    });

    expect(written.length).toBe(1);
    const all = written.join("");
    for (const secret of [SECRETS.access, SECRETS.refresh, SECRETS.jwt]) {
      expect(all).not.toContain(secret);
    }
  });
});
