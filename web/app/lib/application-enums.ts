/**
 * Application outcome (the API calls it "status"), event type and resume variant. Allowed values come from the
 * generated API contract (contracts/client/api-types.ts, ADR 0035); this file adds only labels, tones and order.
 */
import type { components } from "../../../contracts/client/api-types.js";
import { exhaustiveOrder } from "./enum-order.js";

type Schemas = components["schemas"];

/** The Domain enum ApplicationOutcome, exposed by the API as the application "status". */
export type ApplicationOutcome = Schemas["ApplicationDetailDto"]["status"];

export type ApplicationEventType = Schemas["ApplicationEventDto"]["eventType"];

export type ResumeVariant = Schemas["RecordApplicationRequestDto"]["resumeVariant"];

/** Named access to the application status the Insights lenses filter on, typed against the generated union. */
export const APPLICATION_STATUS = {
  Applied: "applied",
} as const satisfies Record<string, ApplicationOutcome>;

export interface OutcomeMetadata {
  label: string;
  tone: "neutral" | "primary" | "success" | "warning" | "danger";
}

export const APPLICATION_OUTCOME_METADATA: Record<ApplicationOutcome, OutcomeMetadata> = {
  applied: { label: "Applied", tone: "primary" },
  rejected: { label: "Rejected", tone: "danger" },
  closed: { label: "Closed", tone: "neutral" },
};

export const APPLICATION_OUTCOME_ORDER = exhaustiveOrder<ApplicationOutcome>()(["applied", "rejected", "closed"]);

export const APPLICATION_OUTCOME_OPTIONS: readonly { value: ApplicationOutcome; label: string }[] =
  APPLICATION_OUTCOME_ORDER.map((value) => ({ value, label: APPLICATION_OUTCOME_METADATA[value].label }));

export const RESUME_VARIANT_LABELS: Record<ResumeVariant, string> = {
  tailored: "Tailored",
  as_is: "As Is",
  ic_framed: "IC Framed",
};

export const RESUME_VARIANT_ORDER = exhaustiveOrder<ResumeVariant>()(["tailored", "as_is", "ic_framed"]);

export const RESUME_VARIANT_OPTIONS: readonly { value: ResumeVariant; label: string }[] = RESUME_VARIANT_ORDER.map(
  (value) => ({ value, label: RESUME_VARIANT_LABELS[value] }),
);

/** UI order of the event types (declaration order of the Domain enum, note last). */
export const APPLICATION_EVENT_TYPE_ORDER = exhaustiveOrder<ApplicationEventType>()([
  "applied",
  "follow_up",
  "interview",
  "offer",
  "offer_accepted",
  "offer_declined",
  "rejected",
  "withdrawn",
  "note",
]);

export interface EventTypeMetadata {
  label: string;
  resultingStatusDescription: string;
  terminal: boolean;
}

export const APPLICATION_EVENT_TYPE_METADATA: Record<ApplicationEventType, EventTypeMetadata> = {
  applied: {
    label: "Applied",
    resultingStatusDescription: "Status remains 'applied'",
    terminal: false,
  },
  follow_up: {
    label: "Follow Up",
    resultingStatusDescription: "Status remains 'applied'",
    terminal: false,
  },
  interview: {
    label: "Interview",
    resultingStatusDescription: "Status remains 'applied'",
    terminal: false,
  },
  offer: {
    label: "Offer Received",
    resultingStatusDescription: "Status remains 'applied'",
    terminal: false,
  },
  offer_accepted: {
    label: "Offer Accepted",
    resultingStatusDescription: "Transitions status to 'closed'",
    terminal: true,
  },
  offer_declined: {
    label: "Offer Declined",
    resultingStatusDescription: "Transitions status to 'closed'",
    terminal: true,
  },
  rejected: {
    label: "Rejected",
    resultingStatusDescription: "Transitions status to 'rejected'",
    terminal: true,
  },
  withdrawn: {
    label: "Withdrawn",
    resultingStatusDescription: "Transitions status to 'closed'",
    terminal: true,
  },
  note: {
    label: "Note",
    resultingStatusDescription: "Does not change the status",
    terminal: false,
  },
};

/**
 * Event types that can be appended to an application timeline via POST /api/v1/applications/{id}/events.
 */
const APPENDABLE_EVENT_LABELS: Record<ApplicationEventType, string> = {
  follow_up: "Follow Up",
  interview: "Interview",
  offer: "Offer Received",
  offer_accepted: "Offer Accepted (Closes Application)",
  offer_declined: "Offer Declined (Closes Application)",
  rejected: "Rejected (Marks Application Rejected)",
  withdrawn: "Withdrawn (Closes Application)",
  applied: "Applied (Record submission note)",
  note: "Note",
};

const APPENDABLE_EVENT_ORDER = exhaustiveOrder<ApplicationEventType>()([
  "follow_up",
  "interview",
  "offer",
  "offer_accepted",
  "offer_declined",
  "rejected",
  "withdrawn",
  "applied",
  "note",
]);

export const APPENDABLE_EVENT_OPTIONS: readonly { value: ApplicationEventType; label: string }[] =
  APPENDABLE_EVENT_ORDER.map((value) => ({ value, label: APPENDABLE_EVENT_LABELS[value] }));

export function isApplicationOutcome(val: unknown): val is ApplicationOutcome {
  return typeof val === "string" && Object.hasOwn(APPLICATION_OUTCOME_METADATA, val);
}

export function isApplicationEventType(val: unknown): val is ApplicationEventType {
  return typeof val === "string" && Object.hasOwn(APPLICATION_EVENT_TYPE_METADATA, val);
}

export function isResumeVariant(val: unknown): val is ResumeVariant {
  return typeof val === "string" && Object.hasOwn(RESUME_VARIANT_LABELS, val);
}
