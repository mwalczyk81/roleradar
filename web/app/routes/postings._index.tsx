import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useNavigate, useSearchParams, Link, redirect } from "react-router";
import React from "react";
import { requireUserSession, initiateLoginRedirect } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { createApiClient } from "../lib/api-client.server.js";
import { AppShell, Toolbar, DataTable, Badge, Select, Input, Button, type DataTableColumn } from "../ui.js";
import { formatDate } from "../lib/format-date.js";
import {
  DISPOSITION_VALUES,
  DISPOSITION_METADATA,
  RESURFACE_POLICY_VALUES,
  RESURFACE_POLICY_LABELS,
  type PostingDisposition,
  type ResurfacePolicy,
} from "../lib/dispositions.js";
import type { components } from "../../../contracts/client/api-types.js";

type PostingListItemDto = components["schemas"]["PostingListItemDto"];

export const meta: MetaFunction = () => [
  { title: "Job Postings - RoleRadar" },
];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  // Filters are strictly: disposition, companyId, resurfacePolicy, minScore, q, limit, offset
  const disposition = url.searchParams.get("disposition") || undefined;
  const companyId = url.searchParams.get("companyId") || undefined;
  const resurfacePolicy = url.searchParams.get("resurfacePolicy") || undefined;

  const minScoreRaw = url.searchParams.get("minScore");
  let minScore: number | undefined;
  if (minScoreRaw !== null && minScoreRaw.trim() !== "") {
    const parsed = Number(minScoreRaw);
    if (!Number.isNaN(parsed)) {
      minScore = parsed;
    }
  }

  const q = url.searchParams.get("q") || undefined;

  const limitRaw = url.searchParams.get("limit");
  let limit = limitRaw ? Number(limitRaw) : 25;
  if (Number.isNaN(limit) || limit < 1) limit = 25;
  else if (limit > 100) limit = 100;

  const offsetRaw = url.searchParams.get("offset");
  let offset = offsetRaw ? Number(offsetRaw) : 0;
  if (Number.isNaN(offset) || offset < 0) offset = 0;

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
  if (minScore !== undefined) queryParams.minScore = minScore;
  if (q) queryParams.q = q;

  const client = createApiClient(session.accessToken);
  const { data, error, response } = await client.GET("/api/v1/postings", {
    params: {
      query: queryParams,
    },
  });

  if (response.status === 401) {
    await initiateLoginRedirect(request, session.sessionId);
  }

  if (response.status === 403) {
    throw redirect("/not-authorized");
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
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function PostingsIndex() {
  const data = useLoaderData<typeof loader>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  const { theme, toggleTheme } = useTheme();

  const currentSearch = searchParams.get("q") || "";
  const currentDisposition = searchParams.get("disposition") || "";
  const currentMinScore = searchParams.get("minScore") || "";
  const currentPolicy = searchParams.get("resurfacePolicy") || "";

  // Controlled min score input state
  const [minScoreInput, setMinScoreInput] = React.useState(currentMinScore);

  React.useEffect(() => {
    setMinScoreInput(currentMinScore);
  }, [currentMinScore]);

  // Build active filters list for Toolbar chips
  const activeFilters = [];
  if (currentDisposition) {
    const dispLabel = DISPOSITION_METADATA[currentDisposition as PostingDisposition]?.label ?? currentDisposition;
    activeFilters.push({
      id: "disposition",
      label: "Disposition",
      value: dispLabel,
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
    const policyLabel = RESURFACE_POLICY_LABELS[currentPolicy as ResurfacePolicy] ?? currentPolicy;
    activeFilters.push({
      id: "resurfacePolicy",
      label: "Policy",
      value: policyLabel,
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
        const dispMeta = DISPOSITION_METADATA[row.disposition as PostingDisposition] ?? {
          label: row.disposition,
          tone: "neutral",
        };
        return (
          <Badge tone={dispMeta.tone} data-tone={dispMeta.tone} data-testid="disposition-badge">
            {dispMeta.label}
          </Badge>
        );
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
      render: (row) => <span>{formatDate(row.createdAt)}</span>,
    },
  ];

  const navItems = [
    { label: "Postings", href: "/postings", active: true },
    { label: "Companies", href: "/companies", active: false },
    { label: "Applications", href: "/applications", active: false },
  ];

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={data.csrfToken} />
      <Button
        type="submit"
        variant="ghost"
        size="sm"
      >
        Sign Out
      </Button>
    </form>
  );

  return (
    <AppShell
      brand="RoleRadar"
      title="Job Postings"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        <Toolbar
          searchPlaceholder="Search postings..."
          searchLabel="Search job postings"
          searchValue={currentSearch}
          onSearchChange={(value: string) => {
            const next = new URLSearchParams(searchParams);
            if (value.trim()) {
              next.set("q", value.trim());
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
        >
          <div style={{ display: "flex", gap: "var(--ds-space-2)", alignItems: "center" }}>
            <Select
              aria-label="Filter by Disposition"
              value={currentDisposition}
              onChange={(e) => {
                const next = new URLSearchParams(searchParams);
                if (e.target.value) {
                  next.set("disposition", e.target.value);
                } else {
                  next.delete("disposition");
                }
                next.set("offset", "0");
                setSearchParams(next);
              }}
            >
              <option value="">All Dispositions</option>
              {DISPOSITION_VALUES.map((d) => (
                <option key={d} value={d}>
                  {DISPOSITION_METADATA[d].label} ({d})
                </option>
              ))}
            </Select>

            <Select
              aria-label="Filter by Resurface Policy"
              value={currentPolicy}
              onChange={(e) => {
                const next = new URLSearchParams(searchParams);
                if (e.target.value) {
                  next.set("resurfacePolicy", e.target.value);
                } else {
                  next.delete("resurfacePolicy");
                }
                next.set("offset", "0");
                setSearchParams(next);
              }}
            >
              <option value="">All Policies</option>
              {RESURFACE_POLICY_VALUES.map((pol) => (
                <option key={pol} value={pol}>
                  {RESURFACE_POLICY_LABELS[pol]} ({pol})
                </option>
              ))}
            </Select>

            <Input
              type="number"
              aria-label="Filter by Minimum Score"
              placeholder="Min Score"
              value={minScoreInput}
              style={{ width: "7rem" }}
              onChange={(e) => {
                const raw = e.target.value;
                setMinScoreInput(raw);
                const next = new URLSearchParams(searchParams);
                const val = raw.trim();
                if (val !== "" && !Number.isNaN(Number(val))) {
                  next.set("minScore", val);
                } else {
                  next.delete("minScore");
                }
                next.set("offset", "0");
                setSearchParams(next);
              }}
            />
          </div>
        </Toolbar>

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
