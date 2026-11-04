import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useSearchParams, Link } from "react-router";
import { requireUserSession } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import {
  CRITERION_CATEGORY_METADATA,
  isCriterionCategory,
} from "../lib/operational-enums.js";
import { buildODataQuery, toODataDateTime, type FilterExpression, type ODataDateTime } from "../lib/odata-client.js";
import { loadODataList, parsePaging, parseSort } from "../lib/odata-list.server.js";
import { AppShell, DataTable, Badge, Button, type DataTableColumn } from "../ui.js";

export interface CriterionODataModel {
  id: string;
  name: string;
  category: string;
  ruleText: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
}

export const ALLOWED_SORT_FIELDS = ["name", "category", "effectiveFrom", "effectiveTo", "isActive"] as const;

export const meta: MetaFunction = () => [{ title: "Screening Criteria - RoleRadar" }];

/** Criteria in force on a date: started on or before it, and either open-ended or ending after it (end date exclusive, like Criterion.IsEffectiveAt). */
function effectiveOnFilter(date: ODataDateTime): FilterExpression {
  return {
    type: "binary",
    operator: "and",
    left: { type: "comparison", operator: "le", field: "effectiveFrom", value: date },
    right: {
      type: "binary",
      operator: "or",
      left: { type: "comparison", operator: "eq", field: "effectiveTo", value: null },
      right: { type: "comparison", operator: "gt", field: "effectiveTo", value: date },
    },
  };
}

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const { limit, offset } = parsePaging(url);
  const { sort, direction } = parseSort(url, ALLOWED_SORT_FIELDS, { field: "name", direction: "asc" });

  // An unparseable effectiveDate is dropped rather than forwarded to the API.
  const rawEffectiveDate = (url.searchParams.get("effectiveDate") ?? "").trim();
  const effectiveOn = toODataDateTime(rawEffectiveDate);

  const queryString = buildODataQuery({
    filter: effectiveOn ? effectiveOnFilter(effectiveOn) : undefined,
    orderBy: [{ field: sort, direction }],
    top: limit,
    skip: offset,
    count: true,
  });

  const { items, totalCount } = await loadODataList<CriterionODataModel>({
    path: "/odata/Criteria",
    queryString,
    request,
    session,
    failureMessage: "Failed to load screening criteria",
  });

  return {
    items,
    totalCount,
    limit,
    offset,
    sort,
    direction,
    effectiveDate: effectiveOn ? rawEffectiveDate : "",
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function CriteriaIndex() {
  const data = useLoaderData<typeof loader>();
  const [searchParams, setSearchParams] = useSearchParams();
  const { theme, toggleTheme } = useTheme();

  const navItems = getNavItems("/criteria");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const columns: DataTableColumn<CriterionODataModel>[] = [
    {
      key: "name",
      header: "Rule Name",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <span style={{ fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text)" }}>
          {row.name}
        </span>
      ),
    },
    {
      key: "category",
      header: "Category / Weight",
      sortable: true,
      sortValue: () => 0,
      render: (row) => {
        const meta = isCriterionCategory(row.category)
          ? CRITERION_CATEGORY_METADATA[row.category]
          : { label: row.category, tone: "neutral" as const };
        return <Badge tone={meta.tone}>{meta.label}</Badge>;
      },
    },
    {
      key: "ruleText",
      header: "Evaluation Rule",
      render: (row) => (
        <span
          style={{
            whiteSpace: "pre-wrap",
            display: "inline-block",
            color: "var(--ds-text)",
            maxWidth: "25rem",
          }}
        >
          {row.ruleText}
        </span>
      ),
    },
    {
      key: "effectiveFrom",
      header: "Effective From",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span style={{ whiteSpace: "nowrap" }}>{formatDate(row.effectiveFrom)}</span>,
    },
    {
      key: "effectiveTo",
      header: "Effective To",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <span style={{ whiteSpace: "nowrap" }}>{row.effectiveTo ? formatDate(row.effectiveTo) : "Present"}</span>
      ),
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <Badge tone={row.isActive ? "success" : "neutral"}>
          {row.isActive ? "Active" : "Inactive"}
        </Badge>
      ),
    },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Screening Criteria"
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
          caption="Screening Evaluation Rules"
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
          emptyMessage="No criteria found."
        />
      </div>
    </AppShell>
  );
}
