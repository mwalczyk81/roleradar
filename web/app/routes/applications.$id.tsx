import type { LoaderFunctionArgs, ActionFunctionArgs, MetaFunction } from "react-router";
import { Brand } from "../components/Brand.js";
import { useLoaderData, useFetcher, useRevalidator, Link, data } from "react-router";
import React from "react";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { useTheme } from "../lib/use-theme.js";
import { useCloseOnSubmitSuccess } from "../lib/use-close-on-submit-success.js";
import { createApiClient, mapApiError } from "../lib/api-client.server.js";
import { validateCsrfToken } from "../lib/csrf.server.js";
import { getNavItems } from "../lib/nav.js";
import { formatDate } from "../lib/format-date.js";
import {
  APPLICATION_OUTCOME_METADATA,
  APPLICATION_EVENT_TYPE_METADATA,
  APPENDABLE_EVENT_OPTIONS,
  isApplicationOutcome,
  isApplicationEventType,
  type ApplicationEventType,
} from "../lib/application-enums.js";
import {
  AppShell,
  Badge,
  Card,
  Button,
  Modal,
  Input,
  Select,
  Textarea,
  Toast,
  ToastRegion,
  Alert,
} from "../ui.js";
import { ArrowLeft, Plus } from "lucide-react";
import type { components } from "../../../contracts/client/api-types.js";

export type ApplicationDetailDto = components["schemas"]["ApplicationDetailDto"];

export interface AppendEventActionSuccess {
  ok: true;
  application: ApplicationDetailDto;
}

export interface AppendEventActionError {
  ok: false;
  isConflict?: boolean;
  errorCode?: string;
  message?: string;
  fieldErrors?: Record<string, string>;
}

export type AppendEventActionData = AppendEventActionSuccess | AppendEventActionError;

export const meta: MetaFunction<typeof loader> = ({ data: loaderPayload }) => {
  const postingTitle = loaderPayload?.posting?.title;
  return [
    { title: postingTitle ? `Application: ${postingTitle} - RoleRadar` : "Application Detail - RoleRadar" },
  ];
};

export async function loader({ request, params }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const applicationId = params.id;

  if (!applicationId) {
    throw new Response("Application ID is required", { status: 400 });
  }

  const client = createApiClient(session.accessToken);
  const appRes = await client.GET("/api/v1/applications/{id}", {
    params: { path: { id: applicationId } },
  });

  await assertApiOk(appRes.response, request, session.sessionId, { notFoundMessage: "Application Not Found" });

  if (appRes.error || !appRes.data) {
    throw new Response(appRes.error?.message || "Failed to load application", {
      status: appRes.response.status || 500,
    });
  }

  const application = appRes.data;

  // Resolve posting and company details in parallel with failure tolerance
  const [postingRes, companyRes] = await Promise.all([
    client.GET("/api/v1/postings/{id}", {
      params: { path: { id: application.postingId } },
    }),
    client.GET("/api/v1/companies/{id}", {
      params: { path: { id: application.companyId } },
    }),
  ]);

  const posting = postingRes.data && !postingRes.error
    ? postingRes.data
    : {
        id: application.postingId,
        title: `Posting (${application.postingId.slice(0, 8)}...)`,
        companyName: "Unavailable",
        location: null,
      };

  const company = companyRes.data && !companyRes.error
    ? companyRes.data
    : {
        id: application.companyId,
        name: `Company (${application.companyId.slice(0, 8)}...)`,
      };

  return {
    application,
    posting,
    company,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export async function action({ request, params }: ActionFunctionArgs) {
  const session = await requireUserSession(request);
  const applicationId = params.id;

  if (!applicationId) {
    return data<AppendEventActionError>(
      { ok: false, errorCode: "MISSING_ID", message: "Application ID is required." },
      { status: 400 }
    );
  }

  const formData = await request.formData();
  const csrfRaw = formData.get("_csrf");
  const csrfToken = typeof csrfRaw === "string" ? csrfRaw : null;

  if (!validateCsrfToken(csrfToken, session.csrfToken)) {
    return data<AppendEventActionError>(
      {
        ok: false,
        errorCode: "CSRF_INVALID",
        message: "Invalid or missing CSRF token. Please refresh the page and try again.",
      },
      { status: 403 }
    );
  }

  const eventTypeRaw = String(formData.get("eventType") || "").trim();
  const eventDate = String(formData.get("eventDate") || "").trim();
  const notesRaw = formData.get("notes");
  const notes = typeof notesRaw === "string" && notesRaw.trim().length > 0 ? notesRaw.trim() : null;
  const eventSourceRaw = formData.get("eventSource");
  const eventSource = typeof eventSourceRaw === "string" && eventSourceRaw.trim().length > 0 ? eventSourceRaw.trim() : "web";

  if (!isApplicationEventType(eventTypeRaw)) {
    return data<AppendEventActionError>(
      {
        ok: false,
        errorCode: "VALIDATION_FAILED",
        message: "Invalid event type.",
        fieldErrors: { eventType: "Please select a valid event type from the list." },
      },
      { status: 400 }
    );
  }

  if (!eventDate) {
    return data<AppendEventActionError>(
      {
        ok: false,
        errorCode: "VALIDATION_FAILED",
        message: "Event date is required.",
        fieldErrors: { eventDate: "Event date is required in YYYY-MM-DD format." },
      },
      { status: 400 }
    );
  }

  const client = createApiClient(session.accessToken);
  const eventRes = await client.POST("/api/v1/applications/{id}/events", {
    params: { path: { id: applicationId } },
    body: {
      eventType: eventTypeRaw,
      eventDate,
      notes,
      eventSource,
    },
  });

  await assertApiOk(eventRes.response, request, session.sessionId);

  if (eventRes.response.status === 409) {
    return data<AppendEventActionError>(
      {
        ok: false,
        isConflict: true,
        errorCode: "CONCURRENCY_CONFLICT",
        message: "This application was updated concurrently. Please reload to see the latest version.",
      },
      { status: 409 }
    );
  }

  if (eventRes.response.status === 400) {
    const errorMap = mapApiError(eventRes.error, 400);
    return data<AppendEventActionError>(
      {
        ok: false,
        errorCode: errorMap.errorCode,
        message: errorMap.message,
        fieldErrors: errorMap.fieldErrors,
      },
      { status: 400 }
    );
  }

  if (eventRes.error || !eventRes.data) {
    const errorMap = mapApiError(eventRes.error, eventRes.response.status);
    return data<AppendEventActionError>(
      {
        ok: false,
        errorCode: errorMap.errorCode,
        message: errorMap.message,
        fieldErrors: errorMap.fieldErrors,
      },
      { status: eventRes.response.status || 500 }
    );
  }

  return data<AppendEventActionSuccess>(
    {
      ok: true,
      application: eventRes.data,
    },
    { status: 200 }
  );
}

export default function ApplicationDetail() {
  const loaderData = useLoaderData<typeof loader>();
  const fetcher = useFetcher<AppendEventActionData>();
  const revalidator = useRevalidator();
  const { theme, toggleTheme } = useTheme();

  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [conflictToastDismissed, setConflictToastDismissed] = React.useState(false);

  const application = fetcher.data?.ok ? fetcher.data.application : loaderData.application;
  const isConflict = Boolean(
    fetcher.data?.ok === false && fetcher.data?.isConflict && !conflictToastDismissed
  );

  const todayIso = new Date().toISOString().slice(0, 10);
  const [eventType, setEventType] = React.useState<ApplicationEventType>("follow_up");
  const [eventDate, setEventDate] = React.useState(todayIso);
  const [notes, setNotes] = React.useState("");
  const [eventSource, setEventSource] = React.useState("");

  React.useEffect(() => {
    if (isDialogOpen) {
      setEventType("follow_up");
      setEventDate(new Date().toISOString().slice(0, 10));
      setNotes("");
      setEventSource("");
    }
  }, [isDialogOpen]);

  const closeAfterAppend = React.useCallback(() => {
    setIsDialogOpen(false);
    revalidator.revalidate();
  }, [revalidator]);
  useCloseOnSubmitSuccess(isDialogOpen, fetcher, closeAfterAppend);

  const navItems = getNavItems("/applications");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={loaderData.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const outcomeMeta = isApplicationOutcome(application.status)
    ? APPLICATION_OUTCOME_METADATA[application.status]
    : { label: String(application.status || "—"), tone: "neutral" as const };

  const fieldErrors = (fetcher.data && !fetcher.data.ok ? fetcher.data.fieldErrors : {}) || {};
  const isSubmitting = fetcher.state === "submitting" || fetcher.state === "loading";

  const postingDisposition = (loaderData.posting as { disposition?: string } | undefined)?.disposition;
  const isPostingTerminal = postingDisposition === "rejected" || postingDisposition === "closed";

  const getPostingSyncNote = () => {
    if (eventType === "rejected") {
      if (isPostingTerminal) {
        return `Note: The linked job posting is already ${postingDisposition === "rejected" ? "Rejected" : "Closed"}; its status will remain unchanged.`;
      }
      return "Note: Appending this event will also update the linked job posting status to Rejected.";
    }
    if (eventType === "offer_accepted" || eventType === "offer_declined" || eventType === "withdrawn") {
      if (isPostingTerminal) {
        return `Note: The linked job posting is already ${postingDisposition === "rejected" ? "Rejected" : "Closed"}; its status will remain unchanged.`;
      }
      return "Note: Appending this event will also update the linked job posting status to Closed.";
    }
    return null;
  };

  const postingSyncNote = getPostingSyncNote();

  return (
    <AppShell
      brand={<Brand />}
      title={`Application: ${loaderData.posting.title}`}
      userLabel={loaderData.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-6)" }}>
        {/* Breadcrumb back to applications list */}
        <div>
          <Link
            to="/applications"
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
            <span>Back to Applications</span>
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
              <h2 className="page-heading" style={{ margin: "0 0 var(--ds-space-1) 0", fontSize: "var(--ds-text-xl)" }}>
                {loaderData.posting.title}
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
                <Link
                  to={`/companies/${application.companyId}`}
                  style={{ color: "var(--ds-primary)", textDecoration: "none" }}
                >
                  {loaderData.company.name}
                </Link>
                <span>• Applied on {formatDate(application.appliedAt)}</span>
                <Link
                  to={`/postings/${application.postingId}`}
                  style={{ color: "var(--ds-primary)", textDecoration: "none" }}
                >
                  View Job Opportunity →
                </Link>
              </div>
            </div>

            <div style={{ display: "flex", gap: "var(--ds-space-2)", alignItems: "center" }}>
              <Badge tone={outcomeMeta.tone} data-testid="application-status-badge">
                {outcomeMeta.label}
              </Badge>
              <Button
                variant="primary"
                size="sm"
                onClick={() => {
                  setConflictToastDismissed(false);
                  setIsDialogOpen(true);
                }}
              >
                <Plus size={16} style={{ marginRight: "var(--ds-space-1)" }} />
                Append Event
              </Button>
            </div>
          </div>
        </Card>

        {/* Application Timeline Events Card */}
        <Card style={{ padding: "var(--ds-space-5)" }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "var(--ds-space-4)",
            }}
          >
            <h3 className="page-heading" style={{ margin: 0, fontSize: "var(--ds-text-base)" }}>
              Application History & Timeline ({application.events.length} event{application.events.length === 1 ? "" : "s"})
            </h3>
          </div>

          {application.events.length === 0 ? (
            <p style={{ color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-sm)" }}>
              No timeline events recorded for this application.
            </p>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "var(--ds-space-4)",
                position: "relative",
              }}
            >
              {application.events.map((ev, index) => {
                const meta = isApplicationEventType(ev.eventType)
                  ? APPLICATION_EVENT_TYPE_METADATA[ev.eventType]
                  : { label: ev.eventType, resultingStatusDescription: "", terminal: false };

                return (
                  <div
                    key={`${ev.eventType}-${ev.eventDate}-${index}`}
                    data-testid="timeline-event"
                    style={{
                      display: "flex",
                      flexDirection: "column",
                      gap: "var(--ds-space-1)",
                      padding: "var(--ds-space-3)",
                      backgroundColor: "var(--ds-surface-hover)",
                      borderRadius: "var(--ds-radius-md)",
                      borderLeft: meta.terminal
                        ? "var(--ds-space-1) solid var(--ds-danger)"
                        : "var(--ds-space-1) solid var(--ds-primary)",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <div style={{ display: "flex", gap: "var(--ds-space-2)", alignItems: "center" }}>
                        <span style={{ fontWeight: "var(--ds-weight-bold)", fontSize: "var(--ds-text-sm)" }}>
                          {meta.label}
                        </span>
                        {meta.resultingStatusDescription ? (
                          <span style={{ fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)" }}>
                            ({meta.resultingStatusDescription})
                          </span>
                        ) : null}
                      </div>
                      <span style={{ fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)" }}>
                        {formatDate(ev.eventDate)}
                      </span>
                    </div>

                    {ev.eventSource ? (
                      <span className="event-source" data-testid="event-source">
                        Source: {ev.eventSource}
                      </span>
                    ) : null}

                    {ev.notes ? (
                      <p
                        style={{
                          margin: "var(--ds-space-1) 0 0 0",
                          fontSize: "var(--ds-text-sm)",
                          whiteSpace: "pre-wrap",
                          color: "var(--ds-text-primary)",
                        }}
                      >
                        {ev.notes}
                      </p>
                    ) : null}
                  </div>
                );
              })}
            </div>
          )}
        </Card>

        {/* Append Event Dialog */}
        <Modal
          open={isDialogOpen}
          onClose={() => setIsDialogOpen(false)}
          title="Append Timeline Event"
        >
          <fetcher.Form method="post">
            <input type="hidden" name="_csrf" value={loaderData.csrfToken} />

            <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-4)" }}>
              {fetcher.data && !fetcher.data.ok && fetcher.data.message ? (
                <Alert variant="danger">{fetcher.data.message}</Alert>
              ) : null}

              {/* Event Type Select */}
              <div>
                <label
                  htmlFor="append-event-type"
                  style={{
                    display: "block",
                    marginBottom: "var(--ds-space-1)",
                    fontSize: "var(--ds-text-sm)",
                    fontWeight: "var(--ds-weight-medium)",
                  }}
                >
                  Event Type *
                </label>
                <Select
                  id="append-event-type"
                  name="eventType"
                  value={eventType}
                  onChange={(e) => setEventType(e.target.value as ApplicationEventType)}
                >
                  {APPENDABLE_EVENT_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
                {fieldErrors.eventType ? (
                  <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                    {fieldErrors.eventType}
                  </span>
                ) : null}
                {postingSyncNote ? (
                  <p
                    data-testid="posting-sync-note"
                    style={{
                      margin: "var(--ds-space-2) 0 0 0",
                      fontSize: "var(--ds-text-xs)",
                      color: "var(--ds-text-secondary)",
                      backgroundColor: "var(--ds-surface-sunken)",
                      padding: "var(--ds-space-2)",
                      borderRadius: "var(--ds-radius-sm)",
                    }}
                  >
                    {postingSyncNote}
                  </p>
                ) : null}
              </div>

              {/* Event Date */}
              <div>
                <label
                  htmlFor="append-event-date"
                  style={{
                    display: "block",
                    marginBottom: "var(--ds-space-1)",
                    fontSize: "var(--ds-text-sm)",
                    fontWeight: "var(--ds-weight-medium)",
                  }}
                >
                  Event Date *
                </label>
                <Input
                  id="append-event-date"
                  name="eventDate"
                  type="date"
                  value={eventDate}
                  onChange={(e) => setEventDate(e.target.value)}
                  required
                />
                {fieldErrors.eventDate ? (
                  <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                    {fieldErrors.eventDate}
                  </span>
                ) : null}
              </div>

              {/* Notes */}
              <div>
                <label
                  htmlFor="append-event-notes"
                  style={{
                    display: "block",
                    marginBottom: "var(--ds-space-1)",
                    fontSize: "var(--ds-text-sm)",
                  }}
                >
                  Notes & Details
                </label>
                <Textarea
                  id="append-event-notes"
                  name="notes"
                  placeholder="Record interview feedback, offer terms, or correspondence details..."
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
                {fieldErrors.notes ? (
                  <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                    {fieldErrors.notes}
                  </span>
                ) : null}
              </div>

              {/* Event Source */}
              <div>
                <label
                  htmlFor="append-event-source"
                  style={{
                    display: "block",
                    marginBottom: "var(--ds-space-1)",
                    fontSize: "var(--ds-text-sm)",
                  }}
                >
                  Source / Channel
                </label>
                <Input
                  id="append-event-source"
                  name="eventSource"
                  placeholder="e.g. recruiter_email, phone_call, interview_panel"
                  value={eventSource}
                  onChange={(e) => setEventSource(e.target.value)}
                />
              </div>

              {/* Modal Actions */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "flex-end",
                  gap: "var(--ds-space-2)",
                  marginTop: "var(--ds-space-2)",
                }}
              >
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => setIsDialogOpen(false)}
                  disabled={isSubmitting}
                >
                  Cancel
                </Button>
                <Button type="submit" variant="primary" disabled={isSubmitting}>
                  {isSubmitting ? "Appending..." : "Append Event"}
                </Button>
              </div>
            </div>
          </fetcher.Form>
        </Modal>

        {/* 409 Conflict Toast Notification */}
        {isConflict ? (
          <ToastRegion>
            <Toast
              title="Concurrency Conflict"
              variant="warning"
              message="This application was modified by another process. Please reload to see the latest version."
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
