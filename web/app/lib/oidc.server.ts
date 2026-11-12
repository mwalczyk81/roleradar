import {
  ROOT_CONTEXT,
  SpanKind,
  SpanStatusCode,
  TraceFlags,
  context as otelContext,
  trace,
} from "@opentelemetry/api";
import * as client from "openid-client";
import { log } from "./log.server.js";
import { getRequestContext } from "./request-context.server.js";
import { getWebTracer, recordClientRequestDuration } from "./telemetry.server.js";
import { withTraceparent } from "./trace-context.server.js";

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
 * Every server-side call to Identity (discovery, code grant, refresh) carries the request's W3C traceparent
 * (feature 014) and records a child OpenTelemetry CLIENT span (feature 015). Reads globalThis.fetch per call so tests can stub it.
 */
const identityFetch: client.CustomFetch = async (url, options) => {
  const started = performance.now();
  const method = (options.method ?? "GET").toUpperCase();
  const parsedUrl = new URL(url);

  let parentCtx = otelContext.active();
  if (!trace.getSpan(parentCtx)) {
    const reqCtx = getRequestContext();
    if (reqCtx?.traceId && reqCtx.spanId) {
      parentCtx = trace.setSpanContext(ROOT_CONTEXT, {
        traceId: reqCtx.traceId,
        spanId: reqCtx.spanId,
        traceFlags: TraceFlags.SAMPLED,
      });
    }
  }

  const clientSpan = getWebTracer().startSpan(
    `${method} ${parsedUrl.pathname}`,
    {
      kind: SpanKind.CLIENT,
      attributes: {
        "http.request.method": method,
        "url.scheme": parsedUrl.protocol.replace(":", ""),
        "url.path": parsedUrl.pathname,
        "url.full": parsedUrl.toString(),
        "server.address": parsedUrl.hostname,
      },
    },
    parentCtx
  );

  try {
    const response = await globalThis.fetch(url, {
      ...options,
      body: toBodyInit(options.body),
      headers: withTraceparent(new Headers(options.headers)),
    });
    clientSpan.setAttribute("http.response.status_code", response.status);
    if (response.status >= 500) {
      clientSpan.setStatus({ code: SpanStatusCode.ERROR });
    }
    recordClientRequestDuration((performance.now() - started) / 1000, {
      method,
      statusCode: response.status,
      address: parsedUrl.hostname,
      port: Number(parsedUrl.port || (parsedUrl.protocol === "https:" ? 443 : 80)),
      scheme: parsedUrl.protocol.replace(":", ""),
    });
    return response;
  } catch (err) {
    if (err instanceof Error) {
      clientSpan.recordException(err);
    }
    clientSpan.setStatus({ code: SpanStatusCode.ERROR });
    recordClientRequestDuration((performance.now() - started) / 1000, {
      method,
      statusCode: 0,
      address: parsedUrl.hostname,
      port: Number(parsedUrl.port || (parsedUrl.protocol === "https:" ? 443 : 80)),
      scheme: parsedUrl.protocol.replace(":", ""),
      errorType: err instanceof Error ? err.name : "error",
    });
    throw err;
  } finally {
    clientSpan.end();
  }
};

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
