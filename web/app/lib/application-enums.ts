/**
 * Application Outcome, Event Type, and Resume Variant Enums derived from Domain contract.
 * Backed by contracts/application-enums.json with bidirectional drift tests.
 */

export const APPLICATION_OUTCOME_VALUES = [
  "applied",
  "rejected",
  "closed",
] as const;

export type ApplicationOutcome = (typeof APPLICATION_OUTCOME_VALUES)[number];

export const APPLICATION_EVENT_TYPE_VALUES = [
  "applied",
  "follow_up",
  "interview",
  "offer",
  "offer_accepted",
  "offer_declined",
  "rejected",
  "withdrawn",
] as const;

export type ApplicationEventType = (typeof APPLICATION_EVENT_TYPE_VALUES)[number];

export const RESUME_VARIANT_VALUES = [
  "tailored",
  "as_is",
  "ic_framed",
] as const;

export type ResumeVariant = (typeof RESUME_VARIANT_VALUES)[number];

export interface OutcomeMetadata {
  label: string;
  tone: "neutral" | "primary" | "success" | "warning" | "danger";
}

export const APPLICATION_OUTCOME_METADATA: Record<ApplicationOutcome, OutcomeMetadata> = {
  applied: { label: "Applied", tone: "primary" },
  rejected: { label: "Rejected", tone: "danger" },
  closed: { label: "Closed", tone: "neutral" },
};

export const APPLICATION_OUTCOME_OPTIONS: readonly { value: ApplicationOutcome; label: string }[] = [
  { value: "applied", label: "Applied" },
  { value: "rejected", label: "Rejected" },
  { value: "closed", label: "Closed" },
];

export const RESUME_VARIANT_OPTIONS: readonly { value: ResumeVariant; label: string }[] = [
  { value: "tailored", label: "Tailored" },
  { value: "as_is", label: "As Is" },
  { value: "ic_framed", label: "IC Framed" },
];

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
};

/**
 * Event types that can be appended to an application timeline via POST /api/v1/applications/{id}/events.
 */
export const APPENDABLE_EVENT_OPTIONS: readonly { value: ApplicationEventType; label: string }[] = [
  { value: "follow_up", label: "Follow Up" },
  { value: "interview", label: "Interview" },
  { value: "offer", label: "Offer Received" },
  { value: "offer_accepted", label: "Offer Accepted (Closes Application)" },
  { value: "offer_declined", label: "Offer Declined (Closes Application)" },
  { value: "rejected", label: "Rejected (Marks Application Rejected)" },
  { value: "withdrawn", label: "Withdrawn (Closes Application)" },
  { value: "applied", label: "Applied (Record submission note)" },
];

export function isApplicationOutcome(val: unknown): val is ApplicationOutcome {
  return typeof val === "string" && (APPLICATION_OUTCOME_VALUES as readonly string[]).includes(val);
}

export function isApplicationEventType(val: unknown): val is ApplicationEventType {
  return typeof val === "string" && (APPLICATION_EVENT_TYPE_VALUES as readonly string[]).includes(val);
}

export function isResumeVariant(val: unknown): val is ResumeVariant {
  return typeof val === "string" && (RESUME_VARIANT_VALUES as readonly string[]).includes(val);
}
