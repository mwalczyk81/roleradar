import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { fetchOData } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import {
  APPLICATION_OUTCOME_METADATA,
  APPLICATION_OUTCOME_OPTIONS,
  isApplicationOutcome,
} from "../lib/application-enums.js";
import { buildODataQuery, type FilterExpression, type OrderByClause } from "../lib/odata-client.js";
import {
  AppShell,
  DataTable,
  Badge,
  Button,
  Select,
  type DataTableColumn,
} from "../ui.js";

export interface ApplicationODataModel {
  id: string;
  postingId: string;
  companyId: string;
  postingTitle: string;
  companyName: string;
  status: string;
  appliedAt: string;
  hasInterview: boolean;
  firstResponseAt: string | null;
  lastEventAt: string | null;
  ageInDays: number;
}

export const ALLOWED_SORT_FIELDS = [
  "postingTitle",
  "companyName",
  "status",
  "appliedAt",
  "hasInterview",
  "firstResponseAt",
  "lastEventAt",
  "ageInDays",
] as const;

export type AllowedSortField = (typeof ALLOWED_SORT_FIELDS)[number];

export function isAllowedSortField(val: string): val is AllowedSortField {
  return (ALLOWED_SORT_FIELDS as readonly string[]).includes(val);
}

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

  const sortRaw = url.searchParams.get("sort");
  const sort = sortRaw && isAllowedSortField(sortRaw) ? sortRaw : undefined;
  const direction: "asc" | "desc" = url.searchParams.get("direction") === "asc" ? "asc" : "desc";

  const filterParts: FilterExpression[] = [];

  if (statusFilter) {
    filterParts.push({
      type: "comparison",
      operator: "eq",
      field: "status",
      value: statusFilter,
    });
  }

  const q = url.searchParams.get("q");
  if (q && q.trim()) {
    const term = q.trim();
    filterParts.push({
      type: "binary",
      operator: "or",
      left: {
        type: "function",
        name: "contains",
        field: "postingTitle",
        value: term,
      },
      right: {
        type: "function",
        name: "contains",
        field: "companyName",
        value: term,
      },
    });
  }

  let filterExpr: FilterExpression | undefined;
  if (filterParts.length === 1) {
    filterExpr = filterParts[0];
  } else if (filterParts.length > 1) {
    filterExpr = filterParts.reduce((acc, part) => ({
      type: "binary",
      operator: "and",
      left: acc,
      right: part,
    }));
  }

  const orderByList: OrderByClause[] = [];
  if (sort) {
    orderByList.push({ field: sort, direction });
  } else {
    // Default order: when the URL has no sort, loader sends $orderby=appliedAt desc
    orderByList.push({ field: "appliedAt", direction: "desc" });
  }

  const queryString = buildODataQuery({
    filter: filterExpr,
    orderBy: orderByList.length > 0 ? orderByList : undefined,
    top: limit,
    skip: offset,
    count: true,
  });

  const { data, error, response } = await fetchOData<ApplicationODataModel>(
    "/odata/Applications",
    queryString,
    session.accessToken
  );

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to load applications", {
      status: response.status || 500,
    });
  }

  return {
    items: data.value ?? [],
    totalCount: data["@odata.count"] ?? (data.value ? data.value.length : 0),
    limit,
    offset,
    sort,
    direction,
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

  const columns: DataTableColumn<ApplicationODataModel>[] = [
    {
      key: "companyName",
      header: "Company",
      sortable: true,
      sortValue: () => 0,
      render: (row) => {
        const companyName = row.companyName || (row.companyId ? `Company ${row.companyId.slice(0, 8)}` : "—");
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
      key: "postingTitle",
      header: "Opportunity / Posting",
      sortable: true,
      sortValue: () => 0,
      render: (row) => {
        const postingTitle = row.postingTitle || (row.postingId ? `Posting ${row.postingId.slice(0, 8)}` : "—");
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
      sortable: true,
      sortValue: () => 0,
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
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span>{formatDate(row.appliedAt)}</span>,
    },
    {
      key: "hasInterview",
      header: "Interview",
      sortable: true,
      sortValue: () => 0,
      render: (row) =>
        row.hasInterview ? (
          <Badge tone="success">Yes</Badge>
        ) : (
          <Badge tone="neutral">No</Badge>
        ),
    },
    {
      key: "firstResponseAt",
      header: "First Response",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span>{row.firstResponseAt ? formatDate(row.firstResponseAt) : "—"}</span>,
    },
    {
      key: "lastEventAt",
      header: "Last Event",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span>{row.lastEventAt ? formatDate(row.lastEventAt) : "—"}</span>,
    },
    {
      key: "ageInDays",
      header: "Age (Days)",
      sortable: true,
      sortValue: () => 0,
      numeric: true,
      render: (row) => <span>{row.ageInDays}</span>,
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
            whiteSpace: "nowrap",
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
          sortScope="all"
          caption="Tracked Job Applications"
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
