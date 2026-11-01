import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import {
  COMPANY_STATUS_METADATA,
  OWNERSHIP_TYPE_OPTIONS,
  isCompanyStatus,
} from "../lib/company-enums.js";
import { AppShell, DataTable, Badge, Button, type DataTableColumn } from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type CompanyDetailDto = components["schemas"]["CompanyDetailDto"];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const rawLimit = Number(url.searchParams.get("limit"));
  const rawOffset = Number(url.searchParams.get("offset"));

  // Clamp limit to 1..100 (default 25) and offset to >= 0 per scope facts
  const limit = Number.isNaN(rawLimit) || rawLimit <= 0 ? 25 : Math.min(100, Math.max(1, Math.floor(rawLimit)));
  const offset = Number.isNaN(rawOffset) || rawOffset < 0 ? 0 : Math.max(0, Math.floor(rawOffset));

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/companies", {
    params: {
      query: {
        limit,
        offset,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to load companies", {
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

  const columns: DataTableColumn<CompanyDetailDto>[] = [
    {
      key: "name",
      header: "Company",
      render: (row) => (
        <Link
          to={`/companies/${row.id}`}
          style={{
            fontWeight: "var(--ds-weight-medium)",
            color: "var(--ds-primary)",
            textDecoration: "none",
          }}
        >
          {row.name}
        </Link>
      ),
    },
    {
      key: "status",
      header: "Status",
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
    {
      key: "careerSources",
      header: "Career Sources",
      render: (row) => (
        <span>
          {row.careerSources.length} source{row.careerSources.length === 1 ? "" : "s"}
        </span>
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
        <DataTable
          caption="Tracked Companies"
          columns={columns}
          data={data.items}
          keyField="id"
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
