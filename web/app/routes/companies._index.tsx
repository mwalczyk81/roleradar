import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link, Form } from "react-router";
import { requireUserSession } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import {
  COMPANY_STATUS_METADATA,
  OWNERSHIP_TYPE_OPTIONS,
  isCompanyStatus,
} from "../lib/company-enums.js";
import { buildODataQuery } from "../lib/odata-client.js";
import { andFilters, containsAny, loadODataList, parsePaging, parseSort } from "../lib/odata-list.server.js";
import { AppShell, DataTable, Badge, Button, Input, type DataTableColumn } from "../ui.js";

export interface CompanyODataModel {
  id: string;
  name: string;
  status: string;
  ownershipType: string;
  headquarters: string | null;
  description: string | null;
  categoryTags: string[];
}

export const ALLOWED_SORT_FIELDS = ["name", "status", "ownershipType"] as const;

export const meta: MetaFunction = () => [{ title: "Companies - RoleRadar" }];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const { limit, offset } = parsePaging(url);
  const { sort, direction } = parseSort(url, ALLOWED_SORT_FIELDS, { field: "name", direction: "asc" });

  const q = (url.searchParams.get("q") ?? "").trim();

  const queryString = buildODataQuery({
    filter: andFilters([containsAny(["name"], q)].filter((part) => part !== undefined)),
    orderBy: [{ field: sort, direction }],
    top: limit,
    skip: offset,
    count: true,
  });

  const { items, totalCount } = await loadODataList<CompanyODataModel>({
    path: "/odata/Companies",
    queryString,
    request,
    session,
    failureMessage: "Failed to load companies",
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

export default function CompaniesIndex() {
  const data = useLoaderData<typeof loader>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { theme, toggleTheme } = useTheme();

  const navItems = getNavItems("/companies");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const columns: DataTableColumn<CompanyODataModel>[] = [
    {
      key: "name",
      header: "Company",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <Link
          to={`/companies/${row.id}`}
          style={{
            fontWeight: "var(--ds-weight-medium)",
            color: "var(--ds-primary)",
            textDecoration: "none",
            whiteSpace: "nowrap",
          }}
        >
          {row.name}
        </Link>
      ),
    },
    {
      key: "status",
      header: "Status",
      sortable: true,
      sortValue: () => 0,
      render: (row) => {
        const meta = isCompanyStatus(row.status)
          ? COMPANY_STATUS_METADATA[row.status]
          : { label: String(row.status ?? "—"), tone: "neutral" as const };
        return <Badge tone={meta.tone}>{meta.label}</Badge>;
      },
    },
    {
      key: "ownershipType",
      header: "Ownership",
      sortable: true,
      sortValue: () => 0,
      render: (row) => {
        const option = OWNERSHIP_TYPE_OPTIONS.find((o) => o.value === row.ownershipType);
        return <span>{option ? option.label : row.ownershipType}</span>;
      },
    },
    {
      key: "headquarters",
      header: "Headquarters",
      render: (row) => <span>{row.headquarters || "—"}</span>,
    },
    {
      key: "categoryTags",
      header: "Categories",
      render: (row) => (
        <div style={{ display: "flex", gap: "var(--ds-space-1)", flexWrap: "wrap" }}>
          {row.categoryTags && row.categoryTags.length > 0 ? (
            row.categoryTags.map((tag: string) => (
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
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Companies"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        <Form method="get" style={{ display: "flex", gap: "var(--ds-space-2)", maxWidth: "30rem" }}>
          <Input
            name="q"
            defaultValue={data.q}
            placeholder="Search companies by name..."
            aria-label="Search companies"
          />
          <Button type="submit" variant="secondary">
            Search
          </Button>
        </Form>

        <DataTable
          sortScope="all"
          caption="Tracked Companies"
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
            navigate(`/companies/${row.id}`);
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
          emptyMessage="No companies found."
        />
      </div>
    </AppShell>
  );
}
