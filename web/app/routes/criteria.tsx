import type { LoaderFunctionArgs } from "react-router";
import { useLoaderData, Link } from "react-router";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import {
  CRITERION_CATEGORY_METADATA,
  isCriterionCategory,
} from "../lib/operational-enums.js";
import { AppShell, DataTable, Badge, Button, type DataTableColumn } from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type CriterionDto = components["schemas"]["CriterionDto"];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const url = new URL(request.url);

  const effectiveDate = url.searchParams.get("effectiveDate") || undefined;

  const client = createApiClient(session.accessToken);
  const { data, response, error } = await client.GET("/api/v1/criteria", {
    params: {
      query: {
        effectiveDate,
      },
    },
  });

  await assertApiOk(response, request, session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || "Failed to load screening criteria", {
      status: response.status || 500,
    });
  }

  return {
    items: data,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export default function CriteriaIndex() {
  const data = useLoaderData<typeof loader>();
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

  const columns: DataTableColumn<CriterionDto>[] = [
    {
      key: "name",
      header: "Rule Name",
      render: (row) => (
        <span style={{ fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text)" }}>
          {row.name}
        </span>
      ),
    },
    {
      key: "weight",
      header: "Category / Weight",
      render: (row) => {
        const meta = isCriterionCategory(row.weight)
          ? CRITERION_CATEGORY_METADATA[row.weight]
          : { label: row.weight, tone: "neutral" as const };
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
      render: (row) => <span>{formatDate(row.effectiveFrom)}</span>,
    },
    {
      key: "effectiveTo",
      header: "Effective To",
      render: (row) => <span>{row.effectiveTo ? formatDate(row.effectiveTo) : "Present"}</span>,
    },
    {
      key: "isActive",
      header: "Status",
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
          caption="Screening Evaluation Rules"
          columns={columns}
          data={data.items}
          keyField="id"
          emptyMessage="No criteria found."
        />
      </div>
    </AppShell>
  );
}
