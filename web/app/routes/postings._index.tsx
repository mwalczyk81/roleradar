import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { Brand } from "../components/Brand.js";
import { useLoaderData, useNavigate, useSearchParams, Link } from "react-router";
import React from "react";
import { getNavItems } from "../lib/nav.js";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { AppShell, Toolbar, DataTable, Badge, Select, Input, Button, type DataTableColumn } from "../ui.js";
import { formatDate } from "../lib/format-date.js";
import {
  DISPOSITION_VALUES,
  DISPOSITION_METADATA,
  RESURFACE_POLICY_VALUES,
  RESURFACE_POLICY_LABELS,
  isPostingDisposition,
  isResurfacePolicy,
  type PostingDisposition,
  type ResurfacePolicy,
} from "../lib/dispositions.js";
import { buildODataQuery, type FilterExpression, type OrderByClause } from "../lib/odata-client.js";
import { fetchOData } from "../lib/api-client.server.js";
import { lensChipLabel, lensFilter, parseLensParams } from "../lib/insights-lenses.js";

export interface PostingODataModel {
  id: string;
  companyId: string;
  companyName: string;
  title: string;
  requisitionId: string | null;
  location: string | null;
  disposition: string;
  resurfacePolicy: string;
  score: number | null;
  createdAt: string;
  updatedAt: string;
}

export const ALLOWED_SORT_FIELDS = [
  "title",
  "companyName",
  "score",
  "disposition",
  "createdAt",
  "updatedAt",
] as const;

export type AllowedSortField = (typeof ALLOWED_SORT_FIELDS)[number];

export function isAllowedSortField(val: string): val is AllowedSortField {
  return (ALLOWED_SORT_FIELDS as readonly string[]).includes(val);
}

export const meta: MetaFunction = () => [
  { title: "Job Postings - RoleRadar" },
];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const dispositionRaw = url.searchParams.get("disposition");
  const disposition = dispositionRaw && isPostingDisposition(dispositionRaw) ? dispositionRaw : undefined;

  const companyId = url.searchParams.get("companyId") || undefined;

  const resurfacePolicyRaw = url.searchParams.get("resurfacePolicy");
  const resurfacePolicy = resurfacePolicyRaw && isResurfacePolicy(resurfacePolicyRaw) ? resurfacePolicyRaw : undefined;

  const minScoreRaw = url.searchParams.get("minScore");
  let minScore: number | undefined;
  if (minScoreRaw !== null && minScoreRaw.trim() !== "") {
    const parsed = Number(minScoreRaw);
    if (!Number.isNaN(parsed)) {
      minScore = parsed;
    }
  }

  const q = url.searchParams.get("q") || undefined;

  // "Postings I applied to": hasApplication is a stored, derived flag (ADR 0031). Any other value is ignored.
  const hasApplicationRaw = url.searchParams.get("hasApplication");
  const hasApplication = hasApplicationRaw === "yes" || hasApplicationRaw === "no" ? hasApplicationRaw : null;

  // An Insights drill-down: the same filter the dashboard counted with, from the shared lens module.
  const lens = parseLensParams(url, "postings");

  const limitRaw = url.searchParams.get("limit");
  let limit = limitRaw ? Number(limitRaw) : 25;
  if (Number.isNaN(limit) || limit < 1) limit = 25;
  else if (limit > 100) limit = 100;

  const offsetRaw = url.searchParams.get("offset");
  let offset = offsetRaw ? Number(offsetRaw) : 0;
  if (Number.isNaN(offset) || offset < 0) offset = 0;

  const sortRaw = url.searchParams.get("sort");
  const sort = sortRaw && isAllowedSortField(sortRaw) ? sortRaw : undefined;
  const direction: "asc" | "desc" = url.searchParams.get("direction") === "asc" ? "asc" : "desc";

  // Build filters using typed odata-client
  const filterParts: FilterExpression[] = [];

  if (lens) {
    filterParts.push(lensFilter(lens.key, lens.ctx));
  }

  if (hasApplication) {
    filterParts.push({
      type: "comparison",
      operator: "eq",
      field: "hasApplication",
      value: hasApplication === "yes",
    });
  }

  if (disposition) {
    filterParts.push({
      type: "comparison",
      operator: "eq",
      field: "disposition",
      value: disposition,
    });
  }

  if (companyId) {
    filterParts.push({
      type: "comparison",
      operator: "eq",
      field: "companyId",
      value: companyId,
    });
  }

  if (resurfacePolicy) {
    filterParts.push({
      type: "comparison",
      operator: "eq",
      field: "resurfacePolicy",
      value: resurfacePolicy,
    });
  }

  if (minScore !== undefined) {
    filterParts.push({
      type: "comparison",
      operator: "ge",
      field: "score",
      value: minScore,
    });
  }

  if (q && q.trim()) {
    const term = q.trim();
    filterParts.push({
      type: "binary",
      operator: "or",
      left: {
        type: "function",
        name: "contains",
        field: "title",
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
    // Default order: when the URL has no sort, loader sends $orderby=score desc
    orderByList.push({ field: "score", direction: "desc" });
  }

  const queryString = buildODataQuery({
    filter: filterExpr,
    orderBy: orderByList.length > 0 ? orderByList : undefined,
    top: limit,
    skip: offset,
    count: true,
  });

  const { data, error, response } = await fetchOData<PostingODataModel>(
    "/odata/Postings",
    queryString,
    session.accessToken
  );

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message ?? "Failed to fetch postings", {
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
    hasApplication,
    lens: lens ? { key: lens.key, label: lensChipLabel(lens), window: lens.ctx.window, asOf: lens.ctx.asOf } : null,
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
  const currentHasApplication = searchParams.get("hasApplication") === "yes" || searchParams.get("hasApplication") === "no"
    ? (searchParams.get("hasApplication") as "yes" | "no")
    : "";

  // Controlled min score input state
  const [minScoreInput, setMinScoreInput] = React.useState(currentMinScore);

  React.useEffect(() => {
    setMinScoreInput(currentMinScore);
  }, [currentMinScore]);

  // Build active filters list for Toolbar chips
  const activeFilters = [];
  if (data.lens) {
    activeFilters.push({
      id: "lens",
      label: "Insights",
      value: data.lens.label,
      onRemove: () => {
        const next = new URLSearchParams(searchParams);
        next.delete("lens");
        next.delete("window");
        next.delete("asOf");
        next.set("offset", "0");
        setSearchParams(next);
      },
    });
  }
  if (currentHasApplication) {
    activeFilters.push({
      id: "hasApplication",
      label: "Has application",
      value: currentHasApplication === "yes" ? "Yes" : "No",
      onRemove: () => {
        const next = new URLSearchParams(searchParams);
        next.delete("hasApplication");
        next.set("offset", "0");
        setSearchParams(next);
      },
    });
  }
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

  const columns: DataTableColumn<PostingODataModel>[] = [
    {
      key: "companyName",
      header: "Company",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span>{row.companyName}</span>,
    },
    {
      key: "title",
      header: "Role Title",
      sortable: true,
      sortValue: () => 0,
      render: (row) => (
        <Link
          to={`/postings/${row.id}`}
          className="role-title-link"
        >
          {row.title}
        </Link>
      ),
    },
    {
      key: "location",
      header: "Location",
      sortable: false,
      render: (row) => <span>{row.location ?? "—"}</span>,
    },
    {
      key: "disposition",
      header: "Disposition",
      sortable: true,
      sortValue: () => 0,
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
      sortValue: () => 0,
      numeric: true,
      render: (row) => <span>{row.score !== null ? row.score : "—"}</span>,
    },
    {
      key: "createdAt",
      header: "Added",
      sortable: true,
      sortValue: () => 0,
      render: (row) => <span className="cell-nowrap">{formatDate(row.createdAt)}</span>,
    },
  ];

  const navItems = getNavItems("/postings");

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
      brand={<Brand />}
      title="Job Postings"
      userLabel={data.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div className="page-stack">
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
            next.delete("hasApplication");
            next.delete("lens");
            next.delete("window");
            next.delete("asOf");
            next.set("offset", "0");
            setSearchParams(next);
          }}
        >
          <div className="filter-group">
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

            <Select
              aria-label="Filter by application"
              value={currentHasApplication}
              onChange={(e) => {
                const next = new URLSearchParams(searchParams);
                if (e.target.value) {
                  next.set("hasApplication", e.target.value);
                } else {
                  next.delete("hasApplication");
                }
                next.set("offset", "0");
                setSearchParams(next);
              }}
            >
              <option value="">Any application status</option>
              <option value="yes">Applied to</option>
              <option value="no">Not applied to</option>
            </Select>

            <Input
              type="number"
              aria-label="Filter by Minimum Score"
              placeholder="Min Score"
              value={minScoreInput}
              width="sm"
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

        <DataTable<PostingODataModel>
          sortScope="all"
          caption="Job Postings"
          columns={columns}
          data={data.items}
          keyField="id"
          sortColumn={data.sort ?? "score"}
          sortDirection={data.direction}
          onSortChange={(newSort, newDirection) => {
            const next = new URLSearchParams(searchParams);
            next.set("sort", newSort);
            next.set("direction", newDirection);
            next.set("offset", "0");
            setSearchParams(next);
          }}
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
