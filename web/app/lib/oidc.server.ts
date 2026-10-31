import * as client from "openid-client";

let oidcConfigPromise: Promise<client.Configuration> | null = null;

export function getOidcIssuerUri(): string {
  return process.env.ROLERADAR_IDENTITY_ISSUER_URI ?? "https://localhost:5001";
}

export function getWebClientId(): string {
  return process.env.ROLERADAR_WEB_CLIENT_ID ?? "roleradar-web";
}

export function getWebClientSecret(): string {
  const secret = process.env.ROLERADAR_WEB_CLIENT_SECRET;
  if (!secret || secret.trim().length === 0) {
    if (process.env.NODE_ENV === "development" || process.env.NODE_ENV === "test") {
      return "dev-web-client-secret-placeholder";
    }
    const msg =
      "[FATAL] RoleRadar Web refused to start: ROLERADAR_WEB_CLIENT_SECRET is required outside Development.";
    console.error(msg);
    throw new Error(msg);
  }
  return secret.trim();
}

export function getRedirectUri(): string {
  return (
    process.env.ROLERADAR_WEB_REDIRECT_URI ??
    "https://localhost:5003/auth/callback"
  );
}

export function getPostLogoutRedirectUri(): string {
  return (
    process.env.ROLERADAR_WEB_POST_LOGOUT_URI ??
    "https://localhost:5003/auth/logout"
  );
}

export async function getOidcConfig(): Promise<client.Configuration> {
  if (!oidcConfigPromise) {
    const issuer = new URL(getOidcIssuerUri());
    const clientId = getWebClientId();
    const clientSecret = getWebClientSecret();

    oidcConfigPromise = client.discovery(issuer, clientId, clientSecret);
  }
  return await oidcConfigPromise;
}

export function setOidcConfigForTesting(
  config: client.Configuration | null
): void {
  oidcConfigPromise = config ? Promise.resolve(config) : null;
}

export interface AuthorizationUrlParams {
  authUrl: URL;
  codeVerifier: string;
  state: string;
}

export async function generateAuthorizationUrl(): Promise<AuthorizationUrlParams> {
  const config = await getOidcConfig();
  const codeVerifier = client.randomPKCECodeVerifier();
  const codeChallenge = await client.calculatePKCECodeChallenge(codeVerifier);
  const state = client.randomState();

  const authUrl = client.buildAuthorizationUrl(config, {
    redirect_uri: getRedirectUri(),
    scope: "openid profile roleradar.api offline_access",
    code_challenge: codeChallenge,
    code_challenge_method: "S256",
    state,
  });

  return { authUrl, codeVerifier, state };
}

export async function exchangeCodeForTokens(
  callbackUrl: URL,
  codeVerifier: string,
  expectedState: string
): Promise<client.TokenEndpointResponse> {
  const config = await getOidcConfig();
  return await client.authorizationCodeGrant(config, callbackUrl, {
    pkceCodeVerifier: codeVerifier,
    expectedState,
  });
}

export async function refreshTokens(
  refreshToken: string
): Promise<client.TokenEndpointResponse> {
  const config = await getOidcConfig();
  return await client.refreshTokenGrant(config, refreshToken);
}

export async function getEndSessionUrl(idTokenHint?: string): Promise<URL> {
  const config = await getOidcConfig();
  const params: Record<string, string> = {
    post_logout_redirect_uri: getPostLogoutRedirectUri(),
  };
  if (idTokenHint) {
    params.id_token_hint = idTokenHint;
  }
  return client.buildEndSessionUrl(config, params);
}
