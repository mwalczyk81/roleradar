import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useSearchParams, Link } from "react-router";
import { requireUserSession } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { buildODataQuery } from "../lib/odata-client.js";
import { loadODataList, parsePaging, parseSort } from "../lib/odata-list.server.js";
import { AppShell, DataTable, Badge, Button, type DataTableColumn } from "../ui.js";

export interface SearchRunODataModel {
  id: string;
  runDate: string;
  newPostings: number;
  summary: string | null;
}

// Only stored columns are sortable; the summary is free text and is displayed only.
export const ALLOWED_SORT_FIELDS = ["runDate", "newPostings"] as const;

export const meta: MetaFunction = () => [{ title: "Search Runs - RoleRadar" }];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const { limit, offset } = parsePaging(url);
  const { sort, direction } = parseSort(url, ALLOWED_SORT_FIELDS, { field: "runDate", direction: "desc" });

  const queryString = buildODataQuery({
    orderBy: [{ field: sort, direction }],
    top: limit,
    skip: offset,
    count: true,
  });

  const { items, totalCount } = await loadODataList<SearchRunODataModel>({
    path: "/odata/SearchRuns",
    queryString,
    request,
    session,
    failureMessage: "Failed to load search runs",
  });

  return {
    items,
    totalCount,
    limit,
    offset,
    sort,
    direction,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function SearchRunsIndex() {
  const data = useLoaderData<typeof loader>();
  const [searchParams, setSearchParams] = useSearchParams();
  const { theme, toggleTheme } = useTheme();

  const navItems = getNavItems("/search-runs");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const columns: DataTableColumn<SearchRunODataModel>[] = [
    {
      key: "runDate",
      header: "Run Date",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <span style={{ fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text)", whiteSpace: "nowrap" }}>
          {formatDate(row.runDate)}
        </span>
      ),
    },
    {
      key: "newPostings",
      header: "New Postings",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <Badge tone={row.newPostings > 0 ? "success" : "neutral"}>
          {row.newPostings} new
        </Badge>
      ),
    },
    {
      key: "summary",
      header: "Batch Summary",
      render: (row) => (
        <span
          style={{
            whiteSpace: "pre-wrap",
            display: "inline-block",
            color: "var(--ds-text-secondary)",
            maxWidth: "24rem",
          }}
        >
          {row.summary || "—"}
        </span>
      ),
    },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Search Runs"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        <DataTable
          sortScope="all"
          caption="Automated Search Runs History"
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
          emptyMessage="No search runs found."
        />
      </div>
    </AppShell>
  );
}
