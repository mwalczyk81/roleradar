import { describe, it, expect, beforeEach } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as indexLoader } from "../../app/routes/_index.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("GET / (_index)", () => {
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

  it("redirects authenticated visitors to /postings", async () => {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-sub",
      accessToken: "token",
      refreshToken: "refresh",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "csrf",
    };

    saveSession(session);

    const request = new Request("https://localhost:5003/", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const response = await callLoader<Response>(indexLoader, request);

    expect(response.status).toBe(302);
    expect(response.headers.get("Location")).toBe("/postings");
  });

  it("redirects unauthenticated visitors to Identity login", async () => {
    const request = new Request("https://localhost:5003/");

    let thrownResponse: Response | null = null;
    try {
      await callLoader<Response>(indexLoader, request);
    } catch (err: unknown) {
      if (err instanceof Response) {
        thrownResponse = err;
      }
    }

    expect(thrownResponse).not.toBeNull();
    expect(thrownResponse?.status).toBe(302);
    expect(thrownResponse?.headers.get("Location")).toContain("/connect/authorize");
  });
});
