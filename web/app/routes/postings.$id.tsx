import type { LoaderFunctionArgs, ActionFunctionArgs, MetaFunction } from "react-router";
import { useLoaderData, useFetcher, useRevalidator, Link, data } from "react-router";
import React from "react";
import { getNavItems } from "../lib/nav.js";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { createApiClient, mapApiError } from "../lib/api-client.server.js";
import { buildODataQuery } from "../lib/odata-client.js";
import { loadODataList } from "../lib/odata-list.server.js";
import { validateCsrfToken } from "../lib/csrf.server.js";
import { AppShell, Badge, Card, Button, Toast, ToastRegion } from "../ui.js";
import { TransitionPostingDialog } from "../components/TransitionPostingDialog.jsx";
import { CreateApplicationDialog } from "../components/CreateApplicationDialog.jsx";
import { ArrowLeft } from "lucide-react";
import { formatDate } from "../lib/format-date.js";
import {
  DISPOSITION_METADATA,
  RESURFACE_POLICY_VALUES,
  RESURFACE_POLICY_LABELS,
  canCreateApplication,
  type PostingDisposition,
  type ResurfacePolicy,
} from "../lib/dispositions.js";
import type { components } from "../../../contracts/client/api-types.js";

export type PostingDetailDto = components["schemas"]["PostingDetailDto"];
export interface CriterionOption {
  id: string;
  name: string;
}
export type PostingSignalDto = components["schemas"]["PostingSignalDto"];
export type AssessmentNoteSummaryDto = components["schemas"]["AssessmentNoteSummaryDto"];
export type ApplicationDetailDto = components["schemas"]["ApplicationDetailDto"];

export interface TransitionActionSuccess {
  ok: true;
  intent?: string;
  posting: PostingDetailDto;
  application?: ApplicationDetailDto;
}

export interface TransitionActionError {
  ok: false;
  isConflict?: boolean;
  isForbidden?: boolean;
  errorCode?: string;
  message?: string;
  fieldErrors?: Record<string, string>;
}

export type TransitionActionData = TransitionActionSuccess | TransitionActionError;

export const meta: MetaFunction<typeof loader> = ({ data: loaderPayload }) => {
  const postingTitle = loaderPayload?.posting?.title;
  return [
    { title: postingTitle ? `${postingTitle} - RoleRadar` : "Job Posting - RoleRadar" },
  ];
};

export async function loader({ request, params }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const postingId = params.id;

  if (!postingId) {
    throw new Response("Posting ID is required", { status: 400 });
  }

  const client = createApiClient(session.accessToken);

  const [postingRes, criteria] = await Promise.all([
    client.GET("/api/v1/postings/{id}", {
      params: { path: { id: postingId } },
    }),
    // Criteria for the transition dialog's picker: id and name only, from the OData read seam.
    loadODataList<CriterionOption>({
      path: "/odata/Criteria",
      queryString: buildODataQuery({
        select: ["id", "name"],
        orderBy: [{ field: "name", direction: "asc" }],
        top: 100,
      }),
      request,
      session,
      failureMessage: "Failed to load criteria",
    }),
  ]);

  await assertApiOk(postingRes.response, request, session.sessionId, { notFoundMessage: "Posting Not Found" });

  if (postingRes.error || !postingRes.data) {
    throw new Response(postingRes.error?.message ?? "Failed to fetch posting", {
      status: postingRes.response.status || 500,
    });
  }

  // Invariant: Never return tokens to browser
  return {
    posting: postingRes.data,
    criteria: criteria.items,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export async function action({ request, params }: ActionFunctionArgs) {
  const session = await requireUserSession(request);
  const postingId = params.id;

  if (!postingId) {
    return data<TransitionActionError>(
      { ok: false, errorCode: "MISSING_ID", message: "Posting ID is required." },
      { status: 400 }
    );
  }

  const formData = await request.formData();
  const csrfRaw = formData.get("_csrf");
  const csrfToken = typeof csrfRaw === "string" ? csrfRaw : null;

  if (!validateCsrfToken(csrfToken, session.csrfToken)) {
    return data<TransitionActionError>(
      {
        ok: false,
        errorCode: "CSRF_INVALID",
        message: "Invalid or missing CSRF token. Please refresh the page and try again.",
      },
      { status: 403 }
    );
  }

  const intent = String(formData.get("intent") || "");
  const client = createApiClient(session.accessToken);

  if (intent === "create_application") {
    const submittedDate = String(formData.get("submittedDate") || "").trim();
    const resumeVariant = String(formData.get("resumeVariant") || "tailored").trim();
    const currentDisposition = String(formData.get("currentDisposition") || "").trim();
    const requiresOverride = canCreateApplication(currentDisposition).requiresOverride;
    const overrideDecided = formData.get("overrideDecided") === "on" || formData.get("overrideDecided") === "true";
    const overrideReasonRaw = formData.get("overrideReason");
    const overrideReason = typeof overrideReasonRaw === "string" && overrideReasonRaw.trim().length > 0 ? overrideReasonRaw.trim() : null;

    if (requiresOverride && !overrideReason) {
      return data<TransitionActionError>(
        {
          ok: false,
          errorCode: "VALIDATION_FAILED",
          message: "Reopen-and-apply override requires an explicit non-empty overrideReason.",
          fieldErrors: { overrideReason: "Override reason is required for decided postings." },
        },
        { status: 400 }
      );
    }

    const desiredSalaryMinRaw = formData.get("desiredSalaryMin");
    const desiredSalaryMin = desiredSalaryMinRaw && !isNaN(Number(desiredSalaryMinRaw)) ? Number(desiredSalaryMinRaw) : null;
    const desiredSalaryMaxRaw = formData.get("desiredSalaryMax");
    const desiredSalaryMax = desiredSalaryMaxRaw && !isNaN(Number(desiredSalaryMaxRaw)) ? Number(desiredSalaryMaxRaw) : null;
    const desiredSalaryCurrency = String(formData.get("desiredSalaryCurrency") || "USD").trim() || "USD";
    const targetStartDate = String(formData.get("targetStartDate") || "").trim() || null;
    const resumePointer = String(formData.get("resumePointer") || "").trim() || null;
    const confirmationCode = String(formData.get("confirmationCode") || "").trim() || null;

    const appRes = await client.POST("/api/v1/applications", {
      body: {
        postingId,
        submittedDate,
        resumeVariant,
        desiredSalaryMin,
        desiredSalaryMax,
        desiredSalaryCurrency,
        targetStartDate,
        resumePointer,
        confirmationCode,
        overrideDecided,
        overrideReason,
      },
    });

    await assertApiOk(appRes.response, request, session.sessionId);

    if (appRes.response.status === 409) {
      return data<TransitionActionError>(
        {
          ok: false,
          isConflict: true,
          errorCode: "CONCURRENCY_CONFLICT",
          message: "An application already exists for this posting or conflict occurred.",
        },
        { status: 409 }
      );
    }

    if (appRes.response.status === 400) {
      const errorMap = mapApiError(appRes.error, 400);
      return data<TransitionActionError>(
        {
          ok: false,
          errorCode: errorMap.errorCode,
          message: errorMap.message,
          fieldErrors: errorMap.fieldErrors,
        },
        { status: 400 }
      );
    }

    if (appRes.error || !appRes.data) {
      const errorMap = mapApiError(appRes.error, appRes.response.status);
      return data<TransitionActionError>(
        {
          ok: false,
          errorCode: errorMap.errorCode,
          message: errorMap.message,
          fieldErrors: errorMap.fieldErrors,
        },
        { status: appRes.response.status || 500 }
      );
    }

    // Refetch the updated posting to reflect the "applied" disposition transition
    const updatedPostingRes = await client.GET("/api/v1/postings/{id}", {
      params: { path: { id: postingId } },
    });

    await assertApiOk(updatedPostingRes.response, request, session.sessionId);

    if (updatedPostingRes.error || !updatedPostingRes.data) {
      throw new Response("Failed to fetch updated posting", {
        status: updatedPostingRes.response.status || 500,
      });
    }

    return data<TransitionActionSuccess>(
      {
        ok: true,
        intent: "create_application",
        posting: updatedPostingRes.data,
        application: appRes.data,
      },
      { status: 200 }
    );
  }

  const toDisposition = String(formData.get("toDisposition") || "");
  const reason = String(formData.get("reason") || "");
  const versionToken = String(formData.get("versionToken") || "");

  const criterionIdRaw = formData.get("criterionId");
  const criterionId: string | null =
    typeof criterionIdRaw === "string" && criterionIdRaw.trim().length > 0
      ? criterionIdRaw.trim()
      : null;

  const resurfacePolicyRaw = formData.get("resurfacePolicy");
  let resurfacePolicy: ResurfacePolicy | undefined = undefined;
  if (typeof resurfacePolicyRaw === "string" && resurfacePolicyRaw.trim().length > 0) {
    const trimmed = resurfacePolicyRaw.trim();
    if (!RESURFACE_POLICY_VALUES.includes(trimmed as ResurfacePolicy)) {
      return data<TransitionActionError>(
        {
          ok: false,
          errorCode: "INVALID_RESURFACE_POLICY",
          message: `Invalid resurface policy '${trimmed}'.`,
          fieldErrors: { resurfacePolicy: `Invalid resurface policy '${trimmed}'.` },
        },
        { status: 400 }
      );
    }
    resurfacePolicy = trimmed as ResurfacePolicy;
  }

  const overrideReasonRaw = formData.get("overrideReason");
  const overrideReason: string | undefined =
    typeof overrideReasonRaw === "string" && overrideReasonRaw.trim().length > 0
      ? overrideReasonRaw.trim()
      : undefined;

  // Invariant: Target applied is NEVER reachable through disposition matrix
  if (toDisposition.toLowerCase() === "applied") {
    return data<TransitionActionError>(
      {
        ok: false,
        errorCode: "INVALID_TRANSITION",
        message: "Target disposition 'applied' is forbidden.",
        fieldErrors: { toDisposition: "Target disposition 'applied' is forbidden." },
      },
      { status: 400 }
    );
  }

  if (!reason.trim()) {
    return data<TransitionActionError>(
      {
        ok: false,
        errorCode: "VALIDATION_FAILED",
        message: "Reason is required.",
        fieldErrors: { reason: "Reason is required." },
      },
      { status: 400 }
    );
  }

  const transitionRes = await client.POST("/api/v1/postings/{id}/transition", {
    params: { path: { id: postingId } },
    body: {
      toDisposition,
      reason,
      criterionId,
      versionToken,
      resurfacePolicy,
      overrideReason,
    },
  });

  await assertApiOk(transitionRes.response, request, session.sessionId);

  if (transitionRes.response.status === 409) {
    return data<TransitionActionError>(
      {
        ok: false,
        isConflict: true,
        errorCode: "CONCURRENCY_CONFLICT",
        message: "This posting was modified by another process. Please reload to see the latest version.",
      },
      { status: 409 }
    );
  }

  if (transitionRes.response.status === 400) {
    const errorMap = mapApiError(transitionRes.error, 400);
    return data<TransitionActionError>(
      {
        ok: false,
        errorCode: errorMap.errorCode,
        message: errorMap.message,
        fieldErrors: errorMap.fieldErrors,
      },
      { status: 400 }
    );
  }

  if (transitionRes.error || !transitionRes.data) {
    const errorMap = mapApiError(transitionRes.error, transitionRes.response.status);
    return data<TransitionActionError>(
      {
        ok: false,
        errorCode: errorMap.errorCode,
        message: errorMap.message,
        fieldErrors: errorMap.fieldErrors,
      },
      { status: transitionRes.response.status || 500 }
    );
  }

  return data<TransitionActionSuccess>(
    {
      ok: true,
      posting: transitionRes.data,
    },
    { status: 200 }
  );
}

export default function PostingDetail() {
  const loaderData = useLoaderData<typeof loader>();
  const fetcher = useFetcher<TransitionActionData>();
  const revalidator = useRevalidator();

  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [isAppDialogOpen, setIsAppDialogOpen] = React.useState(false);
  const [conflictToastDismissed, setConflictToastDismissed] = React.useState(false);

  const { theme, toggleTheme } = useTheme();

  const posting = fetcher.data?.ok ? fetcher.data.posting : loaderData.posting;
  const isConflict = Boolean(
    fetcher.data?.ok === false && fetcher.data?.isConflict && !conflictToastDismissed
  );

  const dispMeta = DISPOSITION_METADATA[posting.disposition as PostingDisposition] ?? {
    label: posting.disposition,
    tone: "neutral",
  };

  const navItems = getNavItems("/postings");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={loaderData.csrfToken} />
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
      title={posting.title}
      userLabel={loaderData.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Breadcrumb back to postings list */}
        <div>
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
            <ArrowLeft size={16} />
            <span>Back to Job Postings</span>
          </Link>
        </div>

        {/* Top Summary Card */}
        <Card style={{ padding: "var(--ds-space-5)" }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "flex-start",
              flexWrap: "wrap",
              gap: "var(--ds-space-4)",
            }}
          >
            <div>
              <h2 style={{ margin: "0 0 var(--ds-space-1) 0", fontSize: "var(--ds-text-xl)" }}>
                {posting.title}
              </h2>
              <div
                style={{
                  display: "flex",
                  gap: "var(--ds-space-3)",
                  alignItems: "center",
                  color: "var(--ds-text-secondary)",
                  fontSize: "var(--ds-text-sm)",
                }}
              >
                <span>{posting.companyName}</span>
                {posting.location ? <span>• {posting.location}</span> : null}
                {posting.requisitionId ? <span>• Req: {posting.requisitionId}</span> : null}
              </div>
            </div>

            <div style={{ display: "flex", gap: "var(--ds-space-2)", alignItems: "center" }}>
              <Badge tone={dispMeta.tone} data-tone={dispMeta.tone} data-testid="disposition-badge">
                {dispMeta.label}
              </Badge>
              {posting.score !== null && posting.score !== undefined ? (
                <span
                  style={{
                    fontFamily: "var(--ds-font-mono)",
                    fontSize: "var(--ds-text-sm)",
                    fontWeight: "var(--ds-weight-bold)",
                  }}
                >
                  Score: {posting.score}
                </span>
              ) : null}
              {canCreateApplication(posting.disposition).allowed ? (
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    setConflictToastDismissed(false);
                    setIsAppDialogOpen(true);
                  }}
                >
                  Create Application
                </Button>
              ) : null}
              <Button
                variant="primary"
                size="sm"
                onClick={() => {
                  setConflictToastDismissed(false);
                  setIsDialogOpen(true);
                }}
              >
                Change Disposition
              </Button>
            </div>
          </div>
        </Card>

        {/* Details Grid */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(20rem, 1fr))",
            gap: "var(--ds-space-6)",
          }}
        >
          {/* Metadata Card */}
          <Card style={{ padding: "var(--ds-space-5)" }}>
            <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Details</h3>
            <dl
              style={{
                display: "grid",
                gridTemplateColumns: "max-content 1fr",
                gap: "var(--ds-space-2)",
                margin: 0,
                fontSize: "var(--ds-text-sm)",
              }}
            >
              <dt style={{ color: "var(--ds-text-secondary)" }}>Resurface Policy:</dt>
              <dd style={{ margin: 0 }}>
                {RESURFACE_POLICY_LABELS[posting.resurfacePolicy as ResurfacePolicy] ?? posting.resurfacePolicy}
              </dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Compensation:</dt>
              <dd style={{ margin: 0 }}>
                {posting.compensation?.rawText ?? (posting.compensation?.currency ? `Currency: ${posting.compensation.currency}` : "—")}
              </dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Posting URL:</dt>
              <dd style={{ margin: 0 }}>
                {posting.url ? (
                  <a
                    href={posting.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    style={{ color: "var(--ds-primary)", wordBreak: "break-all" }}
                  >
                    {posting.url}
                  </a>
                ) : (
                  "—"
                )}
              </dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Created:</dt>
              <dd style={{ margin: 0 }}>{formatDate(posting.createdAt)}</dd>

              <dt style={{ color: "var(--ds-text-secondary)" }}>Updated:</dt>
              <dd style={{ margin: 0 }}>{formatDate(posting.updatedAt)}</dd>
            </dl>
          </Card>

          {/* Assessment Note Card */}
          <Card style={{ padding: "var(--ds-space-5)" }}>
            <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Assessment Note</h3>
            {posting.assessmentNote ? (
              <div style={{ fontSize: "var(--ds-text-sm)" }}>
                <p style={{ margin: "0 0 var(--ds-space-2) 0", whiteSpace: "pre-wrap" }}>
                  {posting.assessmentNote.summary}
                </p>
                <div style={{ color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-xs)" }}>
                  Updated: {formatDate(posting.assessmentNote.updatedAt)}
                </div>
              </div>
            ) : (
              <div style={{ color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-sm)" }}>
                No assessment note recorded for this opportunity.
              </div>
            )}
          </Card>
        </div>

        {/* Signals Card */}
        <Card style={{ padding: "var(--ds-space-5)" }}>
          <h3 style={{ margin: "0 0 var(--ds-space-3) 0", fontSize: "var(--ds-text-base)" }}>Opportunity Signals</h3>
          {posting.signals && posting.signals.length > 0 ? (
            <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-2)" }}>
              {posting.signals.map((sig, index) => (
                <div
                  key={`${sig.signalType}-${index}`}
                  style={{
                    padding: "var(--ds-space-3)",
                    backgroundColor: "var(--ds-surface-hover)",
                    borderRadius: "var(--ds-radius-md)",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: "var(--ds-space-2)",
                  }}
                >
                  <div style={{ fontSize: "var(--ds-text-sm)" }}>
                    <strong>{sig.signalType}</strong>
                    {sig.evidence ? (
                      <span style={{ color: "var(--ds-text-secondary)", marginLeft: "var(--ds-space-2)" }}>
                        — {sig.evidence}
                      </span>
                    ) : null}
                  </div>
                  {sig.detectedAt ? (
                    <span style={{ fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)" }}>
                      {formatDate(sig.detectedAt)}
                    </span>
                  ) : null}
                </div>
              ))}
            </div>
          ) : (
            <div style={{ color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-sm)" }}>
              No opportunity signals detected.
            </div>
          )}
        </Card>

        {/* Transition Dialog */}
        <TransitionPostingDialog
          open={isDialogOpen}
          onClose={() => setIsDialogOpen(false)}
          postingId={posting.id}
          currentDisposition={posting.disposition}
          currentResurfacePolicy={posting.resurfacePolicy}
          versionToken={posting.versionToken}
          csrfToken={loaderData.csrfToken}
          criteria={loaderData.criteria}
          fetcher={fetcher}
        />

        {/* Create Application Dialog */}
        <CreateApplicationDialog
          open={isAppDialogOpen}
          onClose={() => setIsAppDialogOpen(false)}
          postingId={posting.id}
          postingTitle={posting.title}
          currentDisposition={posting.disposition}
          csrfToken={loaderData.csrfToken}
          fetcher={fetcher}
        />

        {/* 409 Conflict Toast Notification */}
        {isConflict ? (
          <ToastRegion>
            <Toast
              title="Concurrency Conflict"
              variant="warning"
              message="This posting was updated by another process. Please reload to see the latest version."
              action={{
                label: "Reload",
                onClick: () => {
                  setConflictToastDismissed(true);
                  revalidator.revalidate();
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
