import { describe, it, expect, beforeEach, vi } from "vitest";
import * as client from "openid-client";
import {
  generateAuthorizationUrl,
  exchangeCodeForTokens,
  refreshTokens,
  getEndSessionUrl,
  setOidcConfigForTesting,
} from "../../app/lib/oidc.server.js";

vi.mock("openid-client", async (importOriginal) => {
  const actual = await importOriginal<typeof client>();
  return {
    ...actual,
    authorizationCodeGrant: vi.fn(),
    refreshTokenGrant: vi.fn(),
  };
});

describe("oidc.server", () => {
  // Create a minimal mock configuration
  const mockConfig = new client.Configuration(
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
    setOidcConfigForTesting(mockConfig);
    vi.clearAllMocks();
  });

  it("generates PKCE S256 code challenge, random state, and authorization URL targeting Identity", async () => {
    const { authUrl, codeVerifier, state } = await generateAuthorizationUrl();

    expect(authUrl).toBeInstanceOf(URL);
    expect(authUrl.origin).toBe("https://identity:5001");
    expect(authUrl.pathname).toBe("/connect/authorize");
    expect(authUrl.searchParams.get("client_id")).toBe("roleradar-web");
    expect(authUrl.searchParams.get("response_type")).toBe("code");
    expect(authUrl.searchParams.get("code_challenge_method")).toBe("S256");

    const codeChallenge = authUrl.searchParams.get("code_challenge");
    expect(codeChallenge).toBeTruthy();
    expect(codeVerifier).toBeTruthy();
    expect(state).toBeTruthy();

    // Verify requested scopes
    const scopes = authUrl.searchParams.get("scope")?.split(" ");
    expect(scopes).toContain("openid");
    expect(scopes).toContain("profile");
    expect(scopes).toContain("roleradar.api");
    expect(scopes).toContain("offline_access");
  });

  it("exchanges authorization code via client.authorizationCodeGrant with PKCE verifier", async () => {
    const mockTokenResponse = {
      access_token: "mock-new-access-token",
      refresh_token: "mock-new-refresh-token",
      id_token: "mock-new-id-token",
      expires_in: 600,
    } as unknown as Awaited<ReturnType<typeof client.authorizationCodeGrant>>;

    vi.mocked(client.authorizationCodeGrant).mockResolvedValueOnce(
      mockTokenResponse
    );

    const callbackUrl = new URL(
      "https://localhost:5003/auth/callback?code=test-code&state=test-state"
    );
    const tokens = await exchangeCodeForTokens(
      callbackUrl,
      "test-verifier",
      "test-state"
    );

    expect(client.authorizationCodeGrant).toHaveBeenCalledWith(
      mockConfig,
      callbackUrl,
      expect.objectContaining({
        pkceCodeVerifier: "test-verifier",
        expectedState: "test-state",
      })
    );
    expect(tokens.access_token).toBe("mock-new-access-token");
  });

  it("refreshes token via client.refreshTokenGrant", async () => {
    const mockTokenResponse = {
      access_token: "mock-refreshed-access-token",
      refresh_token: "mock-next-refresh-token",
      expires_in: 600,
    } as unknown as Awaited<ReturnType<typeof client.refreshTokenGrant>>;

    vi.mocked(client.refreshTokenGrant).mockResolvedValueOnce(
      mockTokenResponse
    );

    const tokens = await refreshTokens("current-refresh-token");
    expect(client.refreshTokenGrant).toHaveBeenCalledWith(
      mockConfig,
      "current-refresh-token"
    );
    expect(tokens.access_token).toBe("mock-refreshed-access-token");
  });

  it("builds end-session URL with post-logout redirect URI and id_token_hint", async () => {
    const endSessionUrl = await getEndSessionUrl("mock-id-token-hint");
    expect(endSessionUrl).toBeInstanceOf(URL);
    expect(endSessionUrl.pathname).toBe("/connect/endsession");
    expect(endSessionUrl.searchParams.get("post_logout_redirect_uri")).toBe(
      "https://localhost:5003/auth/logout"
    );
    expect(endSessionUrl.searchParams.get("id_token_hint")).toBe(
      "mock-id-token-hint"
    );
  });
});
