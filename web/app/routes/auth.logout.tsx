import type { ActionFunctionArgs } from "react-router";
import { Link } from "react-router";
import {
  parseSessionCookie,
  getSession,
  destroySession,
  destroySessionCookie,
} from "../lib/session.server.js";
import { validateCsrfToken } from "../lib/csrf.server.js";
import { getEndSessionUrl } from "../lib/oidc.server.js";
import { log } from "../lib/log.server.js";

export async function action({ request }: ActionFunctionArgs) {
  if (request.method !== "POST") {
    return new Response("Method Not Allowed", { status: 405 });
  }

  const cookieHeader = request.headers.get("Cookie");
  const sessionId = parseSessionCookie(cookieHeader);
  const session = sessionId ? getSession(sessionId) : null;

  // CSRF is only required when a valid session exists. If session is missing or expired,
  // still clear the session cookie and redirect to end-session or /postings.
  if (session) {
    const formData = await request.formData();
    const submittedCsrf = formData.get("_csrf") as string | null;

    if (!validateCsrfToken(submittedCsrf, session.csrfToken)) {
      return new Response("Invalid or missing CSRF token", { status: 403 });
    }
  }

  const idTokenHint = session?.idToken;
  if (sessionId) {
    destroySession(sessionId);
  }

  const expiredCookie = destroySessionCookie();

  let redirectUrl: string;
  try {
    const endSessionUrl = await getEndSessionUrl(idTokenHint);
    redirectUrl = endSessionUrl.toString();
  } catch {
    log.warn("Failed to resolve end-session URL; falling back to /postings.");
    redirectUrl = "/postings";
  }

  const headers = new Headers();
  headers.set("Location", redirectUrl);
  headers.append("Set-Cookie", expiredCookie.header);

  return new Response(null, {
    status: 302,
    headers,
  });
}

export async function loader() {
  return new Response(null, { status: 200 });
}

export default function AuthLogout() {
  return (
    <main className="signed-out-page">
      <h1>Signed Out</h1>
      <p>You have been signed out of RoleRadar.</p>
      <p className="signed-out-link">
        <Link to="/postings">Sign in</Link>
      </p>
    </main>
  );
}
