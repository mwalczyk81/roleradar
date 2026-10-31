import { redirect } from "react-router";
import { parse as parseCookie } from "cookie";
import {
  parseSessionCookie,
  getValidSession,
  destroySession,
  destroySessionCookie,
  isSecureCookieRequired,
  type UserSession,
} from "./session.server.js";
import { generateAuthorizationUrl, refreshTokens } from "./oidc.server.js";

export const SECURE_AUTH_STATE_COOKIE = "__Host-roleradar_auth_state";
export const INSECURE_AUTH_STATE_COOKIE = "roleradar_auth_state";

export const SECURE_CODE_VERIFIER_COOKIE = "__Host-roleradar_code_verifier";
export const INSECURE_CODE_VERIFIER_COOKIE = "roleradar_code_verifier";

// Retain legacy exports for backwards compatibility
export const AUTH_STATE_COOKIE = INSECURE_AUTH_STATE_COOKIE;
export const CODE_VERIFIER_COOKIE = INSECURE_CODE_VERIFIER_COOKIE;

export function getAuthStateCookieName(): string {
  return isSecureCookieRequired() ? SECURE_AUTH_STATE_COOKIE : INSECURE_AUTH_STATE_COOKIE;
}

export function getCodeVerifierCookieName(): string {
  return isSecureCookieRequired() ? SECURE_CODE_VERIFIER_COOKIE : INSECURE_CODE_VERIFIER_COOKIE;
}

export function parseAuthStateCookie(cookieHeader: string | null | undefined): string | null {
  if (!cookieHeader) return null;
  const parsed = parseCookie(cookieHeader);
  if (isSecureCookieRequired()) {
    return parsed[SECURE_AUTH_STATE_COOKIE] ?? null;
  }
  return parsed[SECURE_AUTH_STATE_COOKIE] ?? parsed[INSECURE_AUTH_STATE_COOKIE] ?? null;
}

export function parseCodeVerifierCookie(cookieHeader: string | null | undefined): string | null {
  if (!cookieHeader) return null;
  const parsed = parseCookie(cookieHeader);
  if (isSecureCookieRequired()) {
    return parsed[SECURE_CODE_VERIFIER_COOKIE] ?? null;
  }
  return parsed[SECURE_CODE_VERIFIER_COOKIE] ?? parsed[INSECURE_CODE_VERIFIER_COOKIE] ?? null;
}

export function sanitizeReturnTo(raw: string | null | undefined): string {
  if (!raw || typeof raw !== "string") {
    return "/postings";
  }
  // Reject ASCII control characters anywhere in the value (0x00-0x1F, 0x7F)
  for (let i = 0; i < raw.length; i++) {
    const code = raw.charCodeAt(i);
    if ((code >= 0 && code <= 0x1f) || code === 0x7f) {
      return "/postings";
    }
  }
  const trimmed = raw.trim();
  // Must start with single '/', reject '//', backslashes '\', and absolute URIs containing '://'
  if (
    !trimmed.startsWith("/") ||
    trimmed.startsWith("//") ||
    trimmed.includes("\\") ||
    trimmed.includes("://")
  ) {
    return "/postings";
  }
  return trimmed;
}

export async function initiateLoginRedirect(
  request: Request,
  clearSessionId?: string
): Promise<never> {
  if (clearSessionId) {
    destroySession(clearSessionId);
  }

  const url = new URL(request.url);
  const returnTo = sanitizeReturnTo(url.pathname + url.search);
  const { authUrl, codeVerifier, state } = await generateAuthorizationUrl();

  const statePayload = JSON.stringify({ state, returnTo });
  const encodedState = Buffer.from(statePayload, "utf8").toString("base64url");

  const secure = isSecureCookieRequired();
  const secureAttr = secure ? "; Secure" : "";

  const stateCookie = `${getAuthStateCookieName()}=${encodedState}; Path=/; HttpOnly${secureAttr}; SameSite=Lax; Max-Age=300`;
  const verifierCookie = `${getCodeVerifierCookieName()}=${codeVerifier}; Path=/; HttpOnly${secureAttr}; SameSite=Lax; Max-Age=300`;

  const headers = new Headers();
  headers.set("Location", authUrl.toString());
  headers.append("Set-Cookie", stateCookie);
  headers.append("Set-Cookie", verifierCookie);

  if (clearSessionId) {
    headers.append("Set-Cookie", destroySessionCookie(secure).header);
  }

  throw redirect(authUrl.toString(), { headers });
}

export async function requireUserSession(request: Request): Promise<UserSession> {
  const cookieHeader = request.headers.get("Cookie");
  const sessionId = parseSessionCookie(cookieHeader);

  if (sessionId) {
    const session = await getValidSession(sessionId, async (refreshToken: string) => {
      try {
        const tokenSet = await refreshTokens(refreshToken);
        if (!tokenSet.access_token) return null;
        return {
          accessToken: tokenSet.access_token,
          refreshToken: tokenSet.refresh_token,
          idToken: tokenSet.id_token,
          expiresAt: Date.now() + (tokenSet.expires_in ?? 600) * 1000,
        };
      } catch {
        return null;
      }
    });

    if (session) {
      return session;
    }
  }

  // Unauthenticated: capture requested return-to path and initiate PKCE authorization flow
  return await initiateLoginRedirect(request);
}
