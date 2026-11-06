import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData } from "react-router";
import { parseSessionCookie, getSession } from "../lib/session.server.js";
import { EmptyState } from "../ui.js";
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
  const data = useLoaderData<typeof loader>();
  const csrfToken = data?.csrfToken ?? "";

  return (
    <main
      className="status-page status-page-empty"
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
              className="button-neutral"
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
