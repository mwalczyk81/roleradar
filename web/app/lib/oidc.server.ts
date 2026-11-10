import * as client from "openid-client";
import { log } from "./log.server.js";
import { withCorrelationHeader } from "./api-client.server.js";

interface RoleRadarOidcGlobal {
  __ROLERADAR_OIDC_CONFIG_PROMISE__?: Promise<client.Configuration> | null;
}

const oidcGlobal = globalThis as unknown as RoleRadarOidcGlobal;

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
      "RoleRadar Web refused to start: ROLERADAR_WEB_CLIENT_SECRET is required outside Development.";
    log.fatal(msg);
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

/**
 * Every server-side call to Identity (discovery, code grant, refresh) carries the request's X-Correlation-ID
 * (feature 013). Reads globalThis.fetch per call so tests can stub it.
 */
const identityFetch: client.CustomFetch = (url, options) =>
  globalThis.fetch(url, {
    ...options,
    body: toBodyInit(options.body),
    headers: withCorrelationHeader(new Headers(options.headers)),
  });

/** openid-client may hand over a Uint8Array view on any buffer; copy it so it is a standard BodyInit. */
function toBodyInit(body: client.FetchBody): BodyInit | null | undefined {
  return body instanceof Uint8Array ? new Uint8Array(body) : body;
}

export async function getOidcConfig(): Promise<client.Configuration> {
  if (!oidcGlobal.__ROLERADAR_OIDC_CONFIG_PROMISE__) {
    const issuer = new URL(getOidcIssuerUri());
    const clientId = getWebClientId();
    const clientSecret = getWebClientSecret();

    oidcGlobal.__ROLERADAR_OIDC_CONFIG_PROMISE__ = client
      .discovery(issuer, clientId, clientSecret, undefined, { [client.customFetch]: identityFetch })
      .then((config) => {
        config[client.customFetch] = identityFetch;
        return config;
      });
  }
  return await oidcGlobal.__ROLERADAR_OIDC_CONFIG_PROMISE__;
}

export function setOidcConfigForTesting(
  config: client.Configuration | null
): void {
  oidcGlobal.__ROLERADAR_OIDC_CONFIG_PROMISE__ = config ? Promise.resolve(config) : null;
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
