import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { Brand } from "../components/Brand.js";
import { useLoaderData, useNavigate, useSearchParams, Link, Form } from "react-router";
import { requireUserSession } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { buildODataQuery } from "../lib/odata-client.js";
import { containsAny, loadODataList, parsePaging, parseSort } from "../lib/odata-list.server.js";
import { AppShell, DataTable, Badge, Button, Input, type DataTableColumn } from "../ui.js";

// The list never carries the note body: the API projects `content` away in the database query unless a
// $select asks for it, and this route never does.
export interface NoteODataModel {
  id: string;
  heading: string;
  tags: string[];
  linkedEntityType: string;
  linkedEntityId: string | null;
  createdAt: string;
  updatedAt: string;
}

export const ALLOWED_SORT_FIELDS = ["heading", "createdAt", "updatedAt"] as const;

export const meta: MetaFunction = () => [{ title: "Notes - RoleRadar" }];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const { limit, offset } = parsePaging(url);
  // Most recently updated first, so the page is useful before anyone types a search term.
  const { sort, direction } = parseSort(url, ALLOWED_SORT_FIELDS, { field: "updatedAt", direction: "desc" });

  const q = (url.searchParams.get("q") ?? "").trim();

  const queryString = buildODataQuery({
    filter: containsAny(["heading"], q),
    orderBy: [{ field: sort, direction }],
    top: limit,
    skip: offset,
    count: true,
  });

  const { items, totalCount } = await loadODataList<NoteODataModel>({
    path: "/odata/Notes",
    queryString,
    request,
    session,
    failureMessage: "Failed to load notes",
  });

  return {
    items,
    totalCount,
    limit,
    offset,
    sort,
    direction,
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

  const columns: DataTableColumn<NoteODataModel>[] = [
    {
      key: "heading",
      header: "Heading",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <Link
          to={`/notes/${row.id}`}
          className="entity-link"
        >
          {row.heading}
        </Link>
      ),
    },
    {
      key: "tags",
      header: "Tags",
      render: (row) => (
        <div className="tag-row">
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
      header: "Area",
      // Plain text: the area is free text from the owner, never a route or a link.
      render: (row) => <span>{row.linkedEntityType || "—"}</span>,
    },
    {
      key: "createdAt",
      header: "Created",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span className="cell-nowrap">{formatDate(row.createdAt)}</span>,
    },
    {
      key: "updatedAt",
      header: "Updated",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span className="cell-nowrap">{formatDate(row.updatedAt)}</span>,
    },
  ];

  return (
    <AppShell
      brand={<Brand />}
      title="Notes"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div className="page-stack">
        {/* Search Bar */}
        <Form method="get" className="search-form">
          <Input
            name="q"
            defaultValue={data.q}
            placeholder="Search notes by heading..."
            aria-label="Search notes"
          />
          <Button type="submit" variant="secondary">
            Search
          </Button>
        </Form>

        <DataTable
          sortScope="all"
          caption="Narrative Notes"
          columns={columns}
          data={data.items}
          keyField="id"
          sortColumn={data.sort}
          sortDirection={data.direction}
          onSortChange={(col, dir) => {
            const next = new URLSearchParams(searchParams);
            next.set("sort", col);
            next.set("direction", dir);
            next.set("offset", "0");
            setSearchParams(next);
          }}
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
