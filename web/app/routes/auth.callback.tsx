import type { LoaderFunctionArgs } from "react-router";
import crypto from "node:crypto";
import { exchangeCodeForTokens } from "../lib/oidc.server.js";
import {
  saveSession,
  createSessionCookie,
  isSecureCookieRequired,
  type UserSession,
} from "../lib/session.server.js";
import { generateCsrfToken } from "../lib/csrf.server.js";
import {
  parseAuthStateCookie,
  parseCodeVerifierCookie,
  getAuthStateCookieName,
  getCodeVerifierCookieName,
  sanitizeReturnTo,
} from "../lib/auth-guard.server.js";

export async function loader({ request }: LoaderFunctionArgs) {
  const url = new URL(request.url);
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");

  if (!code || !state) {
    return new Response("Missing code or state parameter", { status: 400 });
  }

  const cookieHeader = request.headers.get("Cookie");
  const rawStateCookie = parseAuthStateCookie(cookieHeader);
  const codeVerifier = parseCodeVerifierCookie(cookieHeader);

  let expectedState: string | null = null;
  let returnTo = "/postings";

  if (rawStateCookie) {
    try {
      const decoded = Buffer.from(rawStateCookie, "base64url").toString("utf8");
      const parsed = JSON.parse(decoded);
      if (parsed && typeof parsed.state === "string") {
        expectedState = parsed.state;
        returnTo = sanitizeReturnTo(parsed.returnTo);
      }
    } catch {
      // Fallback if rawStateCookie was stored unencoded
      expectedState = rawStateCookie;
    }
  }

  if (!expectedState || !codeVerifier || expectedState !== state) {
    return new Response("Invalid or expired authentication state", {
      status: 400,
    });
  }

  try {
    const tokenSet = await exchangeCodeForTokens(url, codeVerifier, state);

    if (!tokenSet.access_token) {
      return new Response("Identity response missing access token", {
        status: 502,
      });
    }

    // Session rotation: mint brand new session ID and CSRF token on login
    const sessionId = crypto.randomUUID();
    const csrfToken = generateCsrfToken();
    const helper = tokenSet as { claims?: () => Record<string, unknown> };
    const claims = typeof helper.claims === "function" ? helper.claims() : undefined;
    const ownerSub = typeof claims?.sub === "string" ? claims.sub : undefined;

    if (!ownerSub) {
      return new Response("Identity token missing valid subject claim", {
        status: 400,
      });
    }

    const session: UserSession = {
      sessionId,
      ownerSub,
      accessToken: tokenSet.access_token,
      refreshToken: tokenSet.refresh_token ?? "",
      idToken: tokenSet.id_token,
      expiresAt: Date.now() + (tokenSet.expires_in ?? 600) * 1000,
      createdAt: Date.now(),
      csrfToken,
    };

    saveSession(session);

    const sessionCookie = createSessionCookie(sessionId);

    const headers = new Headers();
    headers.set("Location", returnTo);
    headers.append("Set-Cookie", sessionCookie.header);

    // Clear transient PKCE cookies with secure attributes
    const secure = isSecureCookieRequired();
    const secureAttr = secure ? "; Secure" : "";
    const stateCookieName = getAuthStateCookieName();
    const verifierCookieName = getCodeVerifierCookieName();

    headers.append(
      "Set-Cookie",
      `${stateCookieName}=; Path=/; HttpOnly${secureAttr}; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT`
    );
    headers.append(
      "Set-Cookie",
      `${verifierCookieName}=; Path=/; HttpOnly${secureAttr}; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT`
    );

    return new Response(null, {
      status: 302,
      headers,
    });
  } catch (err) {
    console.error("[ERROR] Failed to exchange code for tokens:", err);
    return new Response("Authentication exchange failed", { status: 500 });
  }
}

export default function AuthCallback() {
  return null;
}
