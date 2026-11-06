import type { LoaderFunctionArgs } from "react-router";
import { Brand } from "../components/Brand.js";
import { MarkdownBody } from "../components/MarkdownBody.js";
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
      brand={<Brand />}
      title={`Note: ${note.heading}`}
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div className="page-stack">
        {/* Back Link */}
        <div>
          <Link
            to="/notes"
            className="back-link"
          >
            <ArrowLeft size={16} />
            <span>Back to Notes</span>
          </Link>
        </div>

        {/* Note Card */}
        <Card>
          <div className="card-stack">
            <h2 className="page-heading note-title">
              {note.heading}
            </h2>

            {/* Metadata row */}
            <div
              className="meta-row"
            >
              <span>Created {formatDate(note.createdAt)}</span>
              <span>• Updated {formatDate(note.updatedAt)}</span>
              {linkedRoute && note.linkedEntityId ? (
                <>
                  <span>•</span>
                  <Link
                    to={linkedRoute}
                    className="entity-link"
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
              <div className="tag-row tag-row-roomy">
                {note.tags.map((tag: string) => (
                  <Badge key={tag} tone="neutral">
                    {tag}
                  </Badge>
                ))}
              </div>
            ) : null}

            {/* Note body: untrusted Markdown, rendered by the locked-down MarkdownBody (ADR 0032). Classes only, no style attribute. */}
            <div data-testid="note-content" className="note-content">
              <MarkdownBody content={note.content} />
            </div>
          </div>
        </Card>
      </div>
    </AppShell>
  );
}
