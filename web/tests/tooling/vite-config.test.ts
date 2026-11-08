// @vitest-environment node
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import viteConfigFn, {
  populateProcessEnv,
  resetEnvOverrideLoggedForTesting,
} from "../../vite.config.js";

describe("vite.config.ts TLS and environment loading", () => {
  const originalEnv = { ...process.env };
  let tempDir: string;
  let certFile: string;
  let keyFile: string;

  beforeEach(() => {
    process.env = { ...originalEnv };
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "vite-config-test-"));
    process.env.VITE_ENV_DIR = tempDir;
    certFile = path.join(tempDir, "test-cert.crt");
    keyFile = path.join(tempDir, "test-key.key");
    fs.writeFileSync(certFile, "MOCK CERTIFICATE CONTENT");
    fs.writeFileSync(keyFile, "MOCK KEY CONTENT");
  });

  afterEach(() => {
    process.env = { ...originalEnv };
    if (tempDir && fs.existsSync(tempDir)) {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
    vi.restoreAllMocks();
  });

  it("fails if vite.config.ts serves http when cert env vars are set", async () => {
    process.env.HTTPS_CERT_PATH = certFile;
    process.env.HTTPS_CERT_KEY_PATH = keyFile;

    const config = await viteConfigFn({ command: "serve", mode: "development" });

    // Assert that server is configured
    expect(config.server).toBeDefined();

    // Assert that server.https is configured (fails if serving plain HTTP)
    if (!config.server?.https) {
      throw new Error("vite.config.ts served HTTP when cert env vars were set; server.https must be configured.");
    }

    expect(config.server.https).toBeDefined();
    expect(typeof config.server.https).toBe("object");

    const httpsObj = config.server.https as { cert: Buffer; key: Buffer };
    expect(httpsObj.cert).toBeDefined();
    expect(httpsObj.key).toBeDefined();
    expect(httpsObj.cert.toString("utf8")).toBe("MOCK CERTIFICATE CONTENT");
    expect(httpsObj.key.toString("utf8")).toBe("MOCK KEY CONTENT");
    expect(config.server.port).toBe(5003);
  });

  it("fails with a clear error message if cert env vars are unset during dev serve", async () => {
    delete process.env.HTTPS_CERT_PATH;
    delete process.env.HTTPS_CERT_KEY_PATH;

    expect(() => viteConfigFn({ command: "serve", mode: "development" })).toThrowError(
      /RoleRadar Web dev server requires in-process TLS.*HTTPS_CERT_PATH and HTTPS_CERT_KEY_PATH must be set/
    );
  });

  it("fails with a clear error message if cert or key file is missing on disk during dev serve", async () => {
    process.env.HTTPS_CERT_PATH = path.join(tempDir, "missing-cert.crt");
    process.env.HTTPS_CERT_KEY_PATH = keyFile;

    expect(() => viteConfigFn({ command: "serve", mode: "development" })).toThrowError(
      /TLS certificate or key file not found/
    );
  });

  it("does not throw during build if cert env vars are missing", async () => {
    delete process.env.HTTPS_CERT_PATH;
    delete process.env.HTTPS_CERT_KEY_PATH;

    const config = await viteConfigFn({ command: "build", mode: "production" });
    expect(config.server?.port).toBe(5003);
    expect(config.server?.https).toBeUndefined();
  });

  it("loads environment variables into process.env without logging secret values", async () => {
    const logSpy = vi.spyOn(console, "log");
    const infoSpy = vi.spyOn(console, "info");
    const warnSpy = vi.spyOn(console, "warn");
    const errorSpy = vi.spyOn(console, "error");

    process.env.HTTPS_CERT_PATH = certFile;
    process.env.HTTPS_CERT_KEY_PATH = keyFile;
    process.env.TEST_SECRET_ENV_VAR = "secret-token-xyz-123";

    await viteConfigFn({ command: "serve", mode: "development" });

    // Assert that process.env has the variables
    expect(process.env.HTTPS_CERT_PATH).toBe(certFile);

    // Assert zero leakage of secret values to any console logger
    const allConsoleCalls = [
      ...logSpy.mock.calls,
      ...infoSpy.mock.calls,
      ...warnSpy.mock.calls,
      ...errorSpy.mock.calls,
    ].flat().map(String);

    for (const call of allConsoleCalls) {
      expect(call).not.toContain("secret-token-xyz-123");
    }
  });

  it("web/.env.local values beat inherited shell values in dev and logs variable names only", () => {
    const infoSpy = vi.spyOn(console, "info");
    resetEnvOverrideLoggedForTesting();

    const envLocalPath = path.join(tempDir, ".env.local");
    fs.writeFileSync(
      envLocalPath,
      "ROLERADAR_WEB_CLIENT_SECRET=secret-from-env-local\nPORT=5003\n"
    );

    // Stale inherited shell variable
    process.env.ROLERADAR_WEB_CLIENT_SECRET = "stale-secret-from-shell";
    process.env.PORT = "9999";

    populateProcessEnv("development", tempDir);

    // .env.local beats shell
    expect(process.env.ROLERADAR_WEB_CLIENT_SECRET).toBe("secret-from-env-local");
    expect(process.env.PORT).toBe("5003");

    // Assert one line listing NAMES only, never values
    const infoCalls = infoSpy.mock.calls.flat().map(String);
    const overrideMsg = infoCalls.find((c) => c.includes("Ignored inherited shell"));
    expect(overrideMsg).toBeDefined();
    expect(overrideMsg).toContain("ROLERADAR_WEB_CLIENT_SECRET");
    expect(overrideMsg).toContain("PORT");
    expect(overrideMsg).not.toContain("secret-from-env-local");
    expect(overrideMsg).not.toContain("stale-secret-from-shell");
  });
});
