import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData, Link } from "react-router";
import { ArrowLeft } from "lucide-react";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { getEntityRoute } from "../lib/operational-enums.js";
import { AppShell, Card, Badge, Button } from "../ui.js";

export async function loader({ request, params }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const noteId = params.id;

  if (!noteId) {
    throw new Response("Note ID is required", { status: 400 });
  }

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/notes/{id}", {
    params: {
      path: {
        id: noteId,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (response.status === 404 || !data || error) {
    throw new Response("Note Not Found", { status: 404 });
  }

  return {
    note: data,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function NoteDetail() {
  const data = useLoaderData<typeof loader>();
  const { theme, toggleTheme } = useTheme();

  const navItems = getNavItems("/notes");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const note = data.note;
  const linkedRoute = getEntityRoute(note.linkedEntityType, note.linkedEntityId);

  return (
    <AppShell
      brand="RoleRadar"
      title={`Note: ${note.heading}`}
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Back Link */}
        <div>
          <Link
            to="/notes"
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "var(--ds-space-1)",
              color: "var(--ds-text-secondary)",
              textDecoration: "none",
              fontSize: "var(--ds-text-sm)",
            }}
          >
            <ArrowLeft size={16} />
            <span>Back to Notes</span>
          </Link>
        </div>

        {/* Note Card */}
        <Card style={{ padding: "var(--ds-space-6)" }}>
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-4)" }}>
            <h2
              style={{
                margin: 0,
                fontSize: "var(--ds-text-2xl)",
                color: "var(--ds-text)",
                fontWeight: "var(--ds-weight-bold)",
              }}
            >
              {note.heading}
            </h2>

            {/* Metadata row */}
            <div
              style={{
                display: "flex",
                flexWrap: "wrap",
                gap: "var(--ds-space-3)",
                alignItems: "center",
                color: "var(--ds-text-secondary)",
                fontSize: "var(--ds-text-sm)",
              }}
            >
              <span>Created {formatDate(note.createdAt)}</span>
              <span>• Updated {formatDate(note.updatedAt)}</span>
              {linkedRoute && note.linkedEntityId ? (
                <>
                  <span>•</span>
                  <Link
                    to={linkedRoute}
                    style={{
                      color: "var(--ds-primary)",
                      textDecoration: "none",
                      fontWeight: "var(--ds-weight-medium)",
                    }}
                  >
                    View Linked {note.linkedEntityType ? (note.linkedEntityType.toLowerCase() === "posting" ? "Job Opportunity" : note.linkedEntityType.charAt(0).toUpperCase() + note.linkedEntityType.slice(1)) : "Entity"} →
                  </Link>
                </>
              ) : note.linkedEntityType ? (
                <>
                  <span>•</span>
                  <span>Linked {note.linkedEntityType} ({note.linkedEntityId ?? "Unspecified"})</span>
                </>
              ) : null}
            </div>

            {/* Tags */}
            {note.tags && note.tags.length > 0 ? (
              <div style={{ display: "flex", gap: "var(--ds-space-2)", flexWrap: "wrap" }}>
                {note.tags.map((tag: string) => (
                  <Badge key={tag} tone="neutral">
                    {tag}
                  </Badge>
                ))}
              </div>
            ) : null}

            {/* Note Content: preserved whitespace, rendered as React text node only */}
            <div
              data-testid="note-content"
              style={{
                whiteSpace: "pre-wrap",
                fontFamily: "var(--ds-font-sans)",
                fontSize: "var(--ds-text-base)",
                lineHeight: 1.6,
                color: "var(--ds-text)",
                backgroundColor: "var(--ds-surface-sunken)",
                padding: "var(--ds-space-4)",
                borderRadius: "var(--ds-radius-md)",
                border: "var(--ds-border-thin) solid var(--ds-border-subtle)",
              }}
            >
              {note.content}
            </div>
          </div>
        </Card>
      </div>
    </AppShell>
  );
}
