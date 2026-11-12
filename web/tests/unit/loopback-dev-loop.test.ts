import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import type { AddressInfo } from "node:net";
import { startServer } from "../../server.js";
import { getOidcConfig, refreshTokens, setOidcConfigForTesting } from "../../app/lib/oidc.server.js";

// Feature 015 Gate 5 (ADR 0049): the local AppHost runs the web app over loopback HTTP so no certificate has to be trusted.
// Two small, gated allowances make that possible, and neither widens the packaged stack:
//   1. HOST picks the bind address (the packaged image keeps 0.0.0.0, the AppHost sets 127.0.0.1).
//   2. An http: issuer is accepted only when its host is loopback (no environment check: demo and production take the same paths).

describe("server bind address (HOST)", () => {
  const originalEnv = { ...process.env };

  afterEach(() => {
    process.env = { ...originalEnv };
  });

  async function listenAndReadAddress(): Promise<string> {
    const server = startServer(0);
    await new Promise<void>((resolve) => server.once("listening", () => resolve()));
    const { address } = server.address() as AddressInfo;
    await new Promise<void>((resolve) => server.close(() => resolve()));
    return address;
  }

  it("binds loopback only when HOST is 127.0.0.1", async () => {
    process.env = { ...originalEnv, NODE_ENV: "test", HOST: "127.0.0.1" };
    expect(await listenAndReadAddress()).toBe("127.0.0.1");
  });

  it("keeps binding every interface when HOST is unset or blank (the packaged image)", async () => {
    process.env = { ...originalEnv, NODE_ENV: "test" };
    delete process.env.HOST;
    expect(await listenAndReadAddress()).toBe("0.0.0.0");

    process.env.HOST = "   ";
    expect(await listenAndReadAddress()).toBe("0.0.0.0");
  });
});

describe("OIDC discovery over loopback HTTP", () => {
  const originalEnv = { ...process.env };
  const realFetch = globalThis.fetch;

  function discoveryDocument(issuer: string) {
    return {
      issuer,
      authorization_endpoint: `${issuer}/connect/authorize`,
      token_endpoint: `${issuer}/connect/token`,
      jwks_uri: `${issuer}/.well-known/openid-configuration/jwks`,
      end_session_endpoint: `${issuer}/connect/endsession`,
      response_types_supported: ["code"],
      subject_types_supported: ["public"],
      id_token_signing_alg_values_supported: ["RS256"],
    };
  }

  function stubIdentity(issuer: string) {
    const calls: string[] = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      calls.push(url);
      if (url.endsWith("/.well-known/openid-configuration")) {
        return Response.json(discoveryDocument(issuer));
      }
      if (url.endsWith("/connect/token")) {
        return Response.json({ access_token: "fictional-access-token", token_type: "Bearer", expires_in: 600 });
      }
      return new Response("not found", { status: 404 });
    }) as typeof fetch;
    return calls;
  }

  beforeEach(() => {
    setOidcConfigForTesting(null);
    process.env = { ...originalEnv, NODE_ENV: "development", ROLERADAR_WEB_CLIENT_SECRET: "fictional-secret" };
  });

  afterEach(() => {
    globalThis.fetch = realFetch;
    setOidcConfigForTesting(null);
    process.env = { ...originalEnv };
  });

  it("discovers an http issuer on 127.0.0.1", async () => {
    process.env.ROLERADAR_IDENTITY_ISSUER_URI = "http://127.0.0.1:15001";
    stubIdentity("http://127.0.0.1:15001");

    const config = await getOidcConfig();

    expect(config.serverMetadata().issuer).toBe("http://127.0.0.1:15001");
  });

  it("also lets the token endpoint be called over that loopback http issuer", async () => {
    process.env.ROLERADAR_IDENTITY_ISSUER_URI = "http://127.0.0.1:15001";
    const calls = stubIdentity("http://127.0.0.1:15001");

    const tokens = await refreshTokens("fictional-refresh-token");

    expect(tokens.access_token).toBe("fictional-access-token");
    expect(calls).toContain("http://127.0.0.1:15001/connect/token");
  });

  it("still refuses an http issuer that is not loopback", async () => {
    process.env.ROLERADAR_IDENTITY_ISSUER_URI = "http://identity.example.test:5001";
    stubIdentity("http://identity.example.test:5001");

    await expect(getOidcConfig()).rejects.toThrow();
  });

  it("keeps working for an https issuer", async () => {
    process.env.ROLERADAR_IDENTITY_ISSUER_URI = "https://identity:5001";
    stubIdentity("https://identity:5001");

    const config = await getOidcConfig();

    expect(config.serverMetadata().issuer).toBe("https://identity:5001");
  });
});
