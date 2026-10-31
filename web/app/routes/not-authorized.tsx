import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData } from "react-router";
import { parseSessionCookie, getSession } from "../lib/session.server.js";
import { EmptyState } from "../ds/components/src/feedback/EmptyState.jsx";
import { ShieldAlert } from "lucide-react";

export async function loader({ request }: LoaderFunctionArgs) {
  const cookieHeader = request.headers.get("Cookie");
  const sessionId = parseSessionCookie(cookieHeader);
  const session = sessionId ? getSession(sessionId) : null;

  // Return ONLY the csrfToken string; NEVER return the session object or any token field
  return {
    csrfToken: session?.csrfToken ?? "",
  };
}

export default function NotAuthorized() {
  let csrfToken = "";
  try {
    const data = useLoaderData<typeof loader>();
    if (data?.csrfToken) {
      csrfToken = data.csrfToken;
    }
  } catch {
    // Standalone render outside data router
  }

  return (
    <main
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "100vh",
        backgroundColor: "var(--ds-surface)",
        padding: "var(--ds-space-6)",
      }}
    >
      <EmptyState
        icon={<ShieldAlert size={28} />}
        title="Not Authorized"
        description="You do not have permission to access this resource. RoleRadar is configured for single-owner operation; verify you are signed in with the authorized account."
        action={
          <form method="post" action="/auth/logout">
            <input type="hidden" name="_csrf" value={csrfToken} />
            <button
              type="submit"
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                padding: "var(--ds-space-2) var(--ds-space-4)",
                borderRadius: "var(--ds-radius-md)",
                backgroundColor: "var(--ds-surface-hover)",
                color: "var(--ds-text)",
                border: "thin solid var(--ds-border)",
                cursor: "pointer",
                fontWeight: "var(--ds-weight-medium)",
                fontSize: "var(--ds-text-sm)",
              }}
            >
              Sign Out
            </button>
          </form>
        }
        secondaryAction={null}
      />
    </main>
  );
}
