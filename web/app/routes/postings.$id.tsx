import type { LoaderFunctionArgs, ActionFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useActionData, useNavigation, Link } from "react-router";

export const meta: MetaFunction<typeof loader> = ({ data }) => {
  const postingTitle = data?.posting?.title;
  return [
    { title: postingTitle ? `${postingTitle} - RoleRadar` : "Job Posting - RoleRadar" },
  ];
};
import React from "react";
import { requireUserSession } from "../lib/auth-guard.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { validateCsrfToken } from "../lib/csrf.server.js";
import { AppShell } from "../ds/components/src/layout/AppShell.jsx";
import { Badge } from "../ds/components/src/feedback/Badge.jsx";
import { Card } from "../ds/components/src/feedback/Card.jsx";
import { Button } from "../ds/components/src/forms/Button.jsx";
import { Toast } from "../ds/components/src/feedback/Toast.jsx";
import { ToastRegion } from "../ds/components/src/feedback/ToastRegion.jsx";
import { TransitionPostingDialog } from "../components/TransitionPostingDialog.jsx";
import { ArrowLeft } from "lucide-react";
import type { components } from "../../../contracts/client/api-types.js";

export type PostingDetailDto = components["schemas"]["PostingDetailDto"];
export type CriterionDto = components["schemas"]["CriterionDto"];

export async function loader({ request, params }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const postingId = params.id;

  if (!postingId) {
    throw new Response("Posting ID is required", { status: 400 });
  }

  const client = createApiClient(session.accessToken);

  const [postingRes, criteriaRes] = await Promise.all([
    client.GET("/api/v1/postings/{id}", {
      params: { path: { id: postingId } },
    }),
    client.GET("/api/v1/criteria", {}),
  ]);

  if (postingRes.response.status === 403 || criteriaRes.response.status === 403) {
    throw new Response(null, {
      status: 302,
      headers: { Location: "/not-authorized" },
    });
  }

  if (postingRes.response.status === 404) {
    throw new Response("Posting Not Found", { status: 404 });
  }

  if (postingRes.error || !postingRes.data) {
    throw new Response(postingRes.error?.message ?? "Failed to fetch posting", {
      status: postingRes.response.status || 500,
    });
  }

  // Invariant: Never return tokens to browser
  return {
    posting: postingRes.data,
    criteria: criteriaRes.data ?? [],
    csrfToken: session.csrfToken,
    userLabel: session.ownerSub,
  };
}

export async function action({ request, params }: ActionFunctionArgs) {
  const session = await requireUserSession(request);
  const postingId = params.id;

  if (!postingId) {
    return new Response(JSON.stringify({ ok: false, error: "Posting ID is required" }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    });
  }

  const formData = await request.formData();
  const csrfToken = formData.get("_csrf") as string | null;

  if (!validateCsrfToken(csrfToken, session.csrfToken)) {
    return new Response(JSON.stringify({ ok: false, error: "Invalid CSRF token" }), {
      status: 403,
      headers: { "Content-Type": "application/json" },
    });
  }

  const toDisposition = formData.get("toDisposition") as string | null;
  const reason = formData.get("reason") as string | null;
  const criterionId = (formData.get("criterionId") as string | null) || null;
  const versionToken = formData.get("versionToken") as string | null;
  const resurfacePolicy = (formData.get("resurfacePolicy") as string | null) || null;
  const overrideReason = (formData.get("overrideReason") as string | null) || null;

  // Invariant: Transition dialog never offers or permits 'applied'
  if (toDisposition === "applied") {
    return new Response(
      JSON.stringify({
        ok: false,
        error: "Transition to 'applied' is not permitted via this interface.",
      }),
      { status: 400, headers: { "Content-Type": "application/json" } }
    );
  }

  if (!toDisposition || !reason || !versionToken) {
    return new Response(
      JSON.stringify({
        ok: false,
        error: "Missing required fields (toDisposition, reason, versionToken)",
      }),
      { status: 400, headers: { "Content-Type": "application/json" } }
    );
  }

  const client = createApiClient(session.accessToken);
  const { data, error, response } = await client.POST("/api/v1/postings/{id}/transition", {
    params: {
      path: { id: postingId },
    },
    body: {
      toDisposition,
      reason,
      criterionId,
      versionToken,
      resurfacePolicy,
      overrideReason,
    },
  });

  if (response.status === 403) {
    throw new Response(null, {
      status: 302,
      headers: { Location: "/not-authorized" },
    });
  }

  if (response.status === 409) {
    return new Response(
      JSON.stringify({
        ok: false,
        isConflict: true,
        error: error?.message ?? "The posting was modified by another process. Please reload.",
      }),
      { status: 409, headers: { "Content-Type": "application/json" } }
    );
  }

  if (response.status === 400 || error) {
    return new Response(
      JSON.stringify({
        ok: false,
        errorCode: error?.errorCode ?? "BAD_REQUEST",
        error: error?.message ?? "Invalid transition requested.",
      }),
      { status: response.status || 400, headers: { "Content-Type": "application/json" } }
    );
  }

  // Success
  return new Response(JSON.stringify({ ok: true, posting: data }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

export default function PostingDetail() {
  const data = useLoaderData<typeof loader>();
  let actionData: any = null;
  try {
    actionData = useActionData<any>();
  } catch {
    // Standalone render outside data router
  }

  let navigationState = "idle";
  try {
    const navigation = useNavigation();
    if (navigation?.state) navigationState = navigation.state;
  } catch {
    // Standalone render outside data router
  }

  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [conflictToastDismissed, setConflictToastDismissed] = React.useState(false);

  const posting = actionData?.ok && actionData?.posting ? actionData.posting : data.posting;
  const isConflict = actionData?.isConflict && !conflictToastDismissed;

  if (!posting) {
    return <div>Posting not found.</div>;
  }

  const navItems = [
    { label: "Postings", href: "/postings", active: true },
    { label: "Companies", href: "/companies", active: false },
    { label: "Applications", href: "/applications", active: false },
  ];

  return (
    <AppShell
      brand="RoleRadar"
      title={posting.title}
      userLabel={data.userLabel}
      navItems={navItems}
      headerActions={
        <div style={{ display: "flex", alignItems: "center", gap: "var(--ds-space-3)" }}>
          <Button variant="primary" size="sm" onClick={() => setIsDialogOpen(true)}>
            Change Disposition
          </Button>
          <Link
            to="/postings"
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "var(--ds-space-1)",
              color: "var(--ds-text-secondary)",
              textDecoration: "none",
              fontSize: "var(--ds-text-sm)",
            }}
          >
            <ArrowLeft size={16} /> Back to Postings
          </Link>
        </div>
      }
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
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Header overview card */}
        <Card style={{ padding: "var(--ds-space-6)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: "var(--ds-space-4)" }}>
            <div>
              <h2 style={{ margin: "0 0 var(--ds-space-2) 0", fontSize: "var(--ds-text-xl)" }}>
                {posting.title}
              </h2>
              <div style={{ display: "flex", gap: "var(--ds-space-3)", alignItems: "center", color: "var(--ds-text-secondary)", flexWrap: "wrap" }}>
                <span>{posting.companyName}</span>
                {posting.location ? <span>• {posting.location}</span> : null}
                {posting.requisitionId ? <span>• Req: {posting.requisitionId}</span> : null}
              </div>
            </div>

            <div style={{ display: "flex", gap: "var(--ds-space-3)", alignItems: "center" }}>
              <Badge variant="primary">{posting.disposition}</Badge>
              {posting.score !== null ? (
                <span
                  style={{
                    fontFamily: "var(--ds-font-mono)",
                    fontWeight: "var(--ds-weight-semibold)",
                    fontSize: "var(--ds-text-lg)",
                  }}
                >
                  Score: {posting.score}
                </span>
              ) : null}
            </div>
          </div>
        </Card>

        {/* Details grid */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(20rem, 1fr))", gap: "var(--ds-space-6)" }}>
          {/* Metadata Card */}
          <Card style={{ padding: "var(--ds-space-5)" }}>
            <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Details</h3>
            <dl style={{ display: "grid", gridTemplateColumns: "max-content 1fr", gap: "var(--ds-space-2)", margin: 0, fontSize: "var(--ds-text-sm)" }}>
              <dt style={{ color: "var(--ds-text-secondary)" }}>Resurface Policy:</dt>
              <dd style={{ margin: 0 }}>{posting.resurfacePolicy}</dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Compensation:</dt>
              <dd style={{ margin: 0 }}>
                {posting.compensation?.rawText ?? (posting.compensation?.currency ? `Currency: ${posting.compensation.currency}` : "—")}
              </dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Posting URL:</dt>
              <dd style={{ margin: 0 }}>
                {posting.url ? (
                  <a href={posting.url} target="_blank" rel="noopener noreferrer" style={{ color: "var(--ds-primary)", wordBreak: "break-all" }}>
                    {posting.url}
                  </a>
                ) : (
                  "—"
                )}
              </dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Created:</dt>
              <dd style={{ margin: 0 }}>{new Date(posting.createdAt).toLocaleString()}</dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Last Updated:</dt>
              <dd style={{ margin: 0 }}>{new Date(posting.updatedAt).toLocaleString()}</dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Version Token:</dt>
              <dd style={{ margin: 0, fontFamily: "var(--ds-font-mono)", fontSize: "var(--ds-text-xs)" }}>{posting.versionToken}</dd>
            </dl>
          </Card>

          {/* Assessment Note Card */}
          <Card style={{ padding: "var(--ds-space-5)" }}>
            <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Assessment Note</h3>
            {posting.assessmentNote ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-2)", fontSize: "var(--ds-text-sm)" }}>
                <div style={{ color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-xs)" }}>
                  Author: {posting.assessmentNote.authorName} • {new Date(posting.assessmentNote.createdAt).toLocaleDateString()}
                </div>
                <div style={{ whiteSpace: "pre-wrap", lineHeight: 1.5 }}>
                  {posting.assessmentNote.summary}
                </div>
              </div>
            ) : (
              <p style={{ color: "var(--ds-text-secondary)", margin: 0, fontSize: "var(--ds-text-sm)" }}>No assessment note available.</p>
            )}
          </Card>
        </div>

        {/* Signals */}
        {posting.signals && posting.signals.length > 0 ? (
          <Card style={{ padding: "var(--ds-space-5)" }}>
            <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Extracted Signals</h3>
            <ul style={{ margin: 0, paddingLeft: "var(--ds-space-4)", display: "flex", flexDirection: "column", gap: "var(--ds-space-2)" }}>
              {posting.signals.map((sig, idx) => (
                <li key={idx} style={{ fontSize: "var(--ds-text-sm)" }}>
                  <strong>{sig.signalType}:</strong> <span>{sig.evidence}</span>
                </li>
              ))}
            </ul>
          </Card>
        ) : null}

        {/* Transition dialog modal */}
        <TransitionPostingDialog
          open={isDialogOpen}
          onClose={() => setIsDialogOpen(false)}
          postingId={posting.id}
          currentDisposition={posting.disposition}
          versionToken={posting.versionToken}
          csrfToken={data.csrfToken}
          criteria={data.criteria}
          error={actionData?.error}
          isSubmitting={navigationState === "submitting"}
        />

        {/* 409 Conflict Toast */}
        {isConflict ? (
          <ToastRegion label="Notifications">
            <Toast
              title="Concurrency Conflict"
              message={actionData?.error ?? "This posting was modified elsewhere. Please reload to see the latest version."}
              variant="warning"
              action={{
                label: "Reload Posting",
                onClick: () => {
                  window.location.reload();
                },
              }}
              onDismiss={() => setConflictToastDismissed(true)}
            />
          </ToastRegion>
        ) : null}
      </div>
    </AppShell>
  );
}
