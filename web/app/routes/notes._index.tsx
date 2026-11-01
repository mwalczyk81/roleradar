import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link, Form } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { getEntityRoute } from "../lib/operational-enums.js";
import { AppShell, DataTable, Badge, Button, Input, type DataTableColumn } from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type NoteDto = components["schemas"]["NoteDto"];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const q = (url.searchParams.get("q") ?? "").trim();
  const rawLimit = Number(url.searchParams.get("limit"));
  const rawOffset = Number(url.searchParams.get("offset"));

  const limit = Number.isNaN(rawLimit) || rawLimit <= 0 ? 25 : Math.min(100, Math.max(1, Math.floor(rawLimit)));
  const offset = Number.isNaN(rawOffset) || rawOffset < 0 ? 0 : Math.max(0, Math.floor(rawOffset));

  if (!q) {
    return {
      items: [] as NoteDto[],
      totalCount: 0,
      limit,
      offset,
      q: "",
      csrfToken: session.csrfToken,
      userLabel: session.ownerUsername || "Signed in",
    };
  }

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/notes", {
    params: {
      query: {
        q,
        limit,
        offset,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to search notes", {
      status: response.status || 500,
    });
  }

  return {
    items: data.items,
    totalCount: data.totalCount,
    limit: data.limit,
    offset: data.offset,
    q,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function NotesIndex() {
  const data = useLoaderData<typeof loader>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
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

  const columns: DataTableColumn<NoteDto>[] = [
    {
      key: "heading",
      header: "Heading",
      render: (row) => (
        <Link
          to={`/notes/${row.id}`}
          style={{
            fontWeight: "var(--ds-weight-medium)",
            color: "var(--ds-primary)",
            textDecoration: "none",
          }}
        >
          {row.heading}
        </Link>
      ),
    },
    {
      key: "content",
      header: "Preview",
      render: (row) => (
        <span
          style={{
            whiteSpace: "pre-wrap",
            display: "inline-block",
            color: "var(--ds-text-secondary)",
            maxWidth: "22.5rem",
            overflow: "hidden",
            textOverflow: "ellipsis",
          }}
        >
          {row.content}
        </span>
      ),
    },
    {
      key: "tags",
      header: "Tags",
      render: (row) => (
        <div style={{ display: "flex", gap: "var(--ds-space-1)", flexWrap: "wrap" }}>
          {row.tags && row.tags.length > 0 ? (
            row.tags.map((tag: string) => (
              <Badge key={tag} tone="neutral">
                {tag}
              </Badge>
            ))
          ) : (
            <span>—</span>
          )}
        </div>
      ),
    },
    {
      key: "linkedEntityType",
      header: "Linked Entity",
      render: (row) => {
        const route = getEntityRoute(row.linkedEntityType, row.linkedEntityId);
        if (route && row.linkedEntityId) {
          const label = `${row.linkedEntityType ? row.linkedEntityType.charAt(0).toUpperCase() + row.linkedEntityType.slice(1) : "Entity"} (${row.linkedEntityId.slice(0, 8)}...)`;
          return (
            <Link
              to={route}
              style={{
                color: "var(--ds-primary)",
                textDecoration: "none",
                fontWeight: "var(--ds-weight-medium)",
              }}
            >
              {label}
            </Link>
          );
        }

        if (row.linkedEntityType) {
          return <span>{row.linkedEntityType}</span>;
        }

        return <span>—</span>;
      },
    },
    {
      key: "updatedAt",
      header: "Updated",
      render: (row) => <span>{formatDate(row.updatedAt)}</span>,
    },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Notes"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Search Bar */}
        <Form method="get" style={{ display: "flex", gap: "var(--ds-space-2)", maxWidth: "30rem" }}>
          <Input
            name="q"
            defaultValue={data.q}
            placeholder="Search notes by query..."
            aria-label="Search notes"
          />
          <Button type="submit" variant="secondary">
            Search
          </Button>
        </Form>

        <DataTable
          caption="Narrative Notes"
          columns={columns}
          data={data.items}
          keyField="id"
          onRowClick={(row) => {
            navigate(`/notes/${row.id}`);
          }}
          pagination={{
            total: data.totalCount,
            limit: data.limit,
            offset: data.offset,
            onPageChange: (newOffset) => {
              const next = new URLSearchParams(searchParams);
              next.set("offset", String(newOffset));
              setSearchParams(next);
            },
          }}
          emptyMessage="No notes found."
        />
      </div>
    </AppShell>
  );
}
