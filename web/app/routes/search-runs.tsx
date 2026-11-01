import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData, useSearchParams, Link } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { AppShell, DataTable, Badge, Button, type DataTableColumn } from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type SearchRunDto = components["schemas"]["SearchRunDto"];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const rawLimit = Number(url.searchParams.get("limit"));
  const rawOffset = Number(url.searchParams.get("offset"));

  const limit = Number.isNaN(rawLimit) || rawLimit <= 0 ? 25 : Math.min(100, Math.max(1, Math.floor(rawLimit)));
  const offset = Number.isNaN(rawOffset) || rawOffset < 0 ? 0 : Math.max(0, Math.floor(rawOffset));

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/search-runs", {
    params: {
      query: {
        limit,
        offset,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to load search runs", {
      status: response.status || 500,
    });
  }

  return {
    items: data.items,
    totalCount: data.totalCount,
    limit: data.limit,
    offset: data.offset,
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

  const columns: DataTableColumn<SearchRunDto>[] = [
    {
      key: "runDate",
      header: "Run Date",
      render: (row) => (
        <span style={{ fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text)" }}>
          {formatDate(row.runDate)}
        </span>
      ),
    },
    {
      key: "companiesSearched",
      header: "Companies Searched",
      render: (row) => <span>{row.companiesSearched}</span>,
    },
    {
      key: "postingsFound",
      header: "Postings Found",
      render: (row) => <span>{row.postingsFound}</span>,
    },
    {
      key: "newPostings",
      header: "New Postings",
      render: (row) => (
        <Badge tone={row.newPostings > 0 ? "success" : "neutral"}>
          {row.newPostings} new
        </Badge>
      ),
    },
    {
      key: "errorsCount",
      header: "Errors",
      render: (row) => (
        <Badge tone={row.errorsCount > 0 ? "danger" : "neutral"}>
          {row.errorsCount}
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
          caption="Automated Search Runs History"
          columns={columns}
          data={data.items}
          keyField="id"
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
