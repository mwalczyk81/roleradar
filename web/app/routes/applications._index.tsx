import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import { resolveApplicationContext } from "../lib/application-context.server.js";
import {
  APPLICATION_OUTCOME_METADATA,
  APPLICATION_OUTCOME_OPTIONS,
  isApplicationOutcome,
} from "../lib/application-enums.js";
import {
  AppShell,
  DataTable,
  Badge,
  Button,
  Select,
  type DataTableColumn,
} from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type ApplicationDetailDto = components["schemas"]["ApplicationDetailDto"];

export const meta: MetaFunction = () => {
  return [{ title: "Applications - RoleRadar" }];
};

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const rawStatus = url.searchParams.get("status");
  const statusFilter = isApplicationOutcome(rawStatus) ? rawStatus : undefined;

  const rawLimit = Number(url.searchParams.get("limit"));
  const rawOffset = Number(url.searchParams.get("offset"));

  // Clamp limit to 1..100 (default 25) and offset to >= 0
  const limit = Number.isNaN(rawLimit) || rawLimit <= 0 ? 25 : Math.min(100, Math.max(1, Math.floor(rawLimit)));
  const offset = Number.isNaN(rawOffset) || rawOffset < 0 ? 0 : Math.max(0, Math.floor(rawOffset));

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/applications", {
    params: {
      query: {
        status: statusFilter,
        limit,
        offset,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to load applications", {
      status: response.status || 500,
    });
  }

  // Deduplicate and resolve names with bounded concurrency
  const { postingMap, companyMap } = await resolveApplicationContext(client, data.items);

  return {
    items: data.items,
    totalCount: data.totalCount,
    limit: data.limit,
    offset: data.offset,
    postingMap,
    companyMap,
    statusFilter: statusFilter || "",
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function ApplicationsIndex() {
  const data = useLoaderData<typeof loader>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { theme, toggleTheme } = useTheme();

  const navItems = getNavItems("/applications");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const columns: DataTableColumn<ApplicationDetailDto>[] = [
    {
      key: "company",
      header: "Company",
      render: (row) => {
        const company = data.companyMap[row.companyId];
        const companyName = company?.name || (row.companyId ? `Company ${row.companyId.slice(0, 8)}` : "—");
        return (
          <Link
            to={row.companyId ? `/companies/${row.companyId}` : "/companies"}
            style={{
              fontWeight: "var(--ds-weight-medium)",
              color: "var(--ds-primary)",
              textDecoration: "none",
            }}
          >
            {companyName}
          </Link>
        );
      },
    },
    {
      key: "posting",
      header: "Opportunity / Posting",
      render: (row) => {
        const posting = data.postingMap[row.postingId];
        const postingTitle = posting?.title || (row.postingId ? `Posting ${row.postingId.slice(0, 8)}` : "—");
        return (
          <Link
            to={`/applications/${row.id}`}
            style={{
              fontWeight: "var(--ds-weight-medium)",
              color: "var(--ds-text-primary)",
              textDecoration: "none",
            }}
          >
            {postingTitle}
          </Link>
        );
      },
    },
    {
      key: "status",
      header: "Status",
      render: (row) => {
        const meta = isApplicationOutcome(row.status)
          ? APPLICATION_OUTCOME_METADATA[row.status]
          : { label: String(row.status || "—"), tone: "neutral" as const };
        return <Badge tone={meta.tone}>{meta.label}</Badge>;
      },
    },
    {
      key: "appliedAt",
      header: "Applied Date",
      render: (row) => <span>{formatDate(row.appliedAt)}</span>,
    },
    {
      key: "events",
      header: "Timeline Events",
      render: (row) => (
        <span>
          {row.events.length} event{row.events.length === 1 ? "" : "s"}
        </span>
      ),
    },
    {
      key: "actions",
      header: "Action",
      render: (row) => (
        <Link
          to={`/applications/${row.id}`}
          style={{
            fontSize: "var(--ds-text-sm)",
            color: "var(--ds-primary)",
            textDecoration: "none",
          }}
        >
          View Timeline →
        </Link>
      ),
    },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Applications"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Status Filter Bar */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "var(--ds-space-4)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "var(--ds-space-2)" }}>
            <label
              htmlFor="status-filter-select"
              style={{ fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}
            >
              Filter by Status:
            </label>
            <div style={{ width: "12rem" }}>
              <Select
                id="status-filter-select"
                value={data.statusFilter}
                onChange={(e) => {
                  const val = e.target.value;
                  const next = new URLSearchParams(searchParams);
                  if (val) {
                    next.set("status", val);
                  } else {
                    next.delete("status");
                  }
                  next.set("offset", "0");
                  setSearchParams(next);
                }}
              >
                <option value="">All Statuses</option>
                {APPLICATION_OUTCOME_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </Select>
            </div>
          </div>
          <div style={{ fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}>
            Total Tracked: <strong>{data.totalCount}</strong>
          </div>
        </div>

        {/* Applications Data Table */}
        <DataTable
          caption="Tracked Job Applications"
          columns={columns}
          data={data.items}
          keyField="id"
          onRowClick={(row) => {
            navigate(`/applications/${row.id}`);
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
          emptyMessage="No applications recorded yet. Opportunities can be tracked from any active job posting."
        />
      </div>
    </AppShell>
  );
}
