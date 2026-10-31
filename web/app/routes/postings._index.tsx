import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link } from "react-router";

export const meta: MetaFunction = () => [
  { title: "Job Postings - RoleRadar" },
];
import { requireUserSession } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { AppShell } from "../ds/components/src/layout/AppShell.jsx";
import { Toolbar } from "../ds/components/src/data/Toolbar.jsx";
import { DataTable, type DataTableColumn } from "../ds/components/src/data/DataTable.jsx";
import { Badge } from "../ds/components/src/feedback/Badge.jsx";
import type { components } from "../../../contracts/client/api-types.js";

type PostingListItemDto = components["schemas"]["PostingListItemDto"];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  // Filters are strictly: disposition, companyId, resurfacePolicy, minScore, q, limit, offset
  const disposition = url.searchParams.get("disposition") || undefined;
  const companyId = url.searchParams.get("companyId") || undefined;
  const resurfacePolicy = url.searchParams.get("resurfacePolicy") || undefined;
  const minScoreRaw = url.searchParams.get("minScore");
  const minScore = minScoreRaw ? Number(minScoreRaw) : undefined;
  const q = url.searchParams.get("q") || undefined;
  const limitRaw = url.searchParams.get("limit");
  const limit = limitRaw ? Number(limitRaw) : 25;
  const offsetRaw = url.searchParams.get("offset");
  const offset = offsetRaw ? Number(offsetRaw) : 0;

  // Sorting is page-local only; do NOT pass sort parameters to the API
  const queryParams: {
    disposition?: string;
    companyId?: string;
    resurfacePolicy?: string;
    minScore?: number;
    q?: string;
    limit?: number;
    offset?: number;
  } = {
    limit,
    offset,
  };

  if (disposition) queryParams.disposition = disposition;
  if (companyId) queryParams.companyId = companyId;
  if (resurfacePolicy) queryParams.resurfacePolicy = resurfacePolicy;
  if (minScore !== undefined && !Number.isNaN(minScore)) queryParams.minScore = minScore;
  if (q) queryParams.q = q;

  const client = createApiClient(session.accessToken);
  const { data, error, response } = await client.GET("/api/v1/postings", {
    params: {
      query: queryParams,
    },
  });

  if (response.status === 403) {
    throw new Response(null, {
      status: 302,
      headers: { Location: "/not-authorized" },
    });
  }

  if (error || !data) {
    throw new Response(error?.message ?? "Failed to fetch postings", {
      status: response.status || 500,
    });
  }

  // Invariant: Never return tokens or session object to browser
  return {
    items: data.items,
    totalCount: data.totalCount,
    limit: data.limit,
    offset: data.offset,
    csrfToken: session.csrfToken,
    userLabel: session.ownerSub,
  };
}

export default function PostingsIndex() {
  const data = useLoaderData<typeof loader>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  const currentSearch = searchParams.get("q") || "";
  const currentDisposition = searchParams.get("disposition") || "";
  const currentMinScore = searchParams.get("minScore") || "";
  const currentPolicy = searchParams.get("resurfacePolicy") || "";

  // Build active filters list for Toolbar chips
  const activeFilters = [];
  if (currentDisposition) {
    activeFilters.push({
      id: "disposition",
      label: "Disposition",
      value: currentDisposition,
      onRemove: () => {
        const next = new URLSearchParams(searchParams);
        next.delete("disposition");
        next.set("offset", "0");
        setSearchParams(next);
      },
    });
  }
  if (currentMinScore) {
    activeFilters.push({
      id: "minScore",
      label: "Min Score",
      value: currentMinScore,
      onRemove: () => {
        const next = new URLSearchParams(searchParams);
        next.delete("minScore");
        next.set("offset", "0");
        setSearchParams(next);
      },
    });
  }
  if (currentPolicy) {
    activeFilters.push({
      id: "resurfacePolicy",
      label: "Policy",
      value: currentPolicy,
      onRemove: () => {
        const next = new URLSearchParams(searchParams);
        next.delete("resurfacePolicy");
        next.set("offset", "0");
        setSearchParams(next);
      },
    });
  }

  const columns: DataTableColumn<PostingListItemDto>[] = [
    {
      key: "companyName",
      header: "Company",
      sortable: true,
      render: (row) => <span>{row.companyName}</span>,
    },
    {
      key: "title",
      header: "Role Title",
      sortable: true,
      render: (row) => (
        <Link
          to={`/postings/${row.id}`}
          style={{
            color: "var(--ds-primary)",
            textDecoration: "none",
            fontWeight: "var(--ds-weight-medium)",
          }}
        >
          {row.title}
        </Link>
      ),
    },
    {
      key: "location",
      header: "Location",
      sortable: true,
      render: (row) => <span>{row.location ?? "—"}</span>,
    },
    {
      key: "disposition",
      header: "Disposition",
      sortable: true,
      render: (row) => {
        const variant =
          row.disposition === "screened_in" || row.disposition === "offer"
            ? "success"
            : row.disposition === "rejected" || row.disposition === "screened_out"
            ? "danger"
            : row.disposition === "interviewing"
            ? "warning"
            : "neutral";
        return <Badge variant={variant}>{row.disposition}</Badge>;
      },
    },
    {
      key: "score",
      header: "Score",
      sortable: true,
      numeric: true,
      render: (row) => <span>{row.score !== null ? row.score : "—"}</span>,
    },
    {
      key: "createdAt",
      header: "Added",
      sortable: true,
      render: (row) => {
        try {
          return <span>{new Date(row.createdAt).toLocaleDateString()}</span>;
        } catch {
          return <span>{row.createdAt}</span>;
        }
      },
    },
  ];

  const navItems = [
    { label: "Postings", href: "/postings", active: true },
    { label: "Companies", href: "/companies", active: false },
    { label: "Applications", href: "/applications", active: false },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title="Job Postings"
      userLabel={data.userLabel}
      navItems={navItems}
      signOut={
        <form method="post" action="/auth/logout">
          <input type="hidden" name="_csrf" value={data.csrfToken} />
          <button
            type="submit"
            style={{
              padding: "var(--ds-space-1) var(--ds-space-3)",
              backgroundColor: "transparent",
              color: "var(--ds-text-secondary)",
              border: "thin solid var(--ds-border)",
              borderRadius: "var(--ds-radius-md)",
              cursor: "pointer",
              fontSize: "var(--ds-text-xs)",
            }}
          >
            Sign Out
          </button>
        </form>
      }
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-4)" }}>
        <Toolbar
          searchPlaceholder="Search postings by role, keyword..."
          searchLabel="Search postings"
          searchValue={currentSearch}
          onSearchChange={(qVal) => {
            const next = new URLSearchParams(searchParams);
            if (qVal && qVal.trim().length > 0) {
              next.set("q", qVal.trim());
            } else {
              next.delete("q");
            }
            next.set("offset", "0");
            setSearchParams(next);
          }}
          filters={activeFilters}
          onClearAll={() => {
            const next = new URLSearchParams(searchParams);
            next.delete("disposition");
            next.delete("minScore");
            next.delete("resurfacePolicy");
            next.set("offset", "0");
            setSearchParams(next);
          }}
        />

        <DataTable
          caption="Job Postings"
          columns={columns}
          data={data.items}
          keyField="id"
          defaultSortColumn="score"
          defaultSortDirection="desc"
          onRowClick={(row) => {
            navigate(`/postings/${row.id}`);
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
          emptyMessage="No job postings match your current filters."
        />
      </div>
    </AppShell>
  );
}
