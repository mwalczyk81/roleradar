import type { components } from "../../../contracts/client/api-types.js";
import { exhaustiveOrder } from "./enum-order.js";

type Schemas = components["schemas"];

/** Allowed values come from the generated API contract; this file adds only order, labels and tones. */
export type PostingDisposition = Schemas["PostingDetailDto"]["disposition"];

export type ResurfacePolicy = Schemas["PostingDetailDto"]["resurfacePolicy"];

/** UI order of the dispositions. A member missing from the generated type is a compile error here. */
export const DISPOSITION_ORDER = exhaustiveOrder<PostingDisposition>()([
  "lead",
  "scored",
  "applied",
  "rejected",
  "screened_out",
  "closed",
  "unscored",
]);

/**
 * Named access to the dispositions the Insights lenses filter on. Typed against the generated union, so a rename
 * in the Domain enum (and the regenerated contract) breaks the build here instead of silently changing a filter.
 */
export const DISPOSITION = {
  Lead: "lead",
  Unscored: "unscored",
  ScreenedOut: "screened_out",
  Closed: "closed",
} as const satisfies Record<string, PostingDisposition>;

/** UI order of the resurface policies. */
export const RESURFACE_POLICY_ORDER = exhaustiveOrder<ResurfacePolicy>()(["never", "if_reopened"]);

export function isPostingDisposition(val: unknown): val is PostingDisposition {
  return typeof val === "string" && Object.hasOwn(DISPOSITION_METADATA, val);
}

export function isResurfacePolicy(val: unknown): val is ResurfacePolicy {
  return typeof val === "string" && Object.hasOwn(RESURFACE_POLICY_LABELS, val);
}

export interface DispositionMeta {
  value: PostingDisposition;
  label: string;
  tone: "neutral" | "primary" | "success" | "warning" | "danger";
}

export const DISPOSITION_METADATA: Record<PostingDisposition, DispositionMeta> = {
  lead: {
    value: "lead",
    label: "Lead",
    tone: "primary",
  },
  scored: {
    value: "scored",
    label: "Scored",
    tone: "warning",
  },
  applied: {
    value: "applied",
    label: "Applied",
    tone: "success",
  },
  rejected: {
    value: "rejected",
    label: "Rejected",
    tone: "danger",
  },
  screened_out: {
    value: "screened_out",
    label: "Screened Out",
    tone: "neutral",
  },
  closed: {
    value: "closed",
    label: "Closed",
    tone: "neutral",
  },
  unscored: {
    value: "unscored",
    label: "Unscored",
    tone: "neutral",
  },
};

export const RESURFACE_POLICY_LABELS: Record<ResurfacePolicy, string> = {
  never: "Never",
  if_reopened: "If Reopened",
};

export interface TransitionRule {
  from: PostingDisposition;
  to: PostingDisposition;
  requiredPolicy: ResurfacePolicy | null;
  requiresReason: boolean;
}

export const TRANSITIONS: TransitionRule[] = [
  { from: "lead", to: "scored", requiredPolicy: null, requiresReason: false },
  { from: "lead", to: "screened_out", requiredPolicy: null, requiresReason: false },
  { from: "lead", to: "closed", requiredPolicy: null, requiresReason: false },
  { from: "lead", to: "unscored", requiredPolicy: null, requiresReason: false },
  { from: "scored", to: "screened_out", requiredPolicy: null, requiresReason: false },
  { from: "scored", to: "closed", requiredPolicy: null, requiresReason: false },
  { from: "applied", to: "rejected", requiredPolicy: null, requiresReason: false },
  { from: "applied", to: "closed", requiredPolicy: null, requiresReason: false },
  { from: "rejected", to: "lead", requiredPolicy: null, requiresReason: true },
  { from: "screened_out", to: "lead", requiredPolicy: "if_reopened", requiresReason: false },
  { from: "closed", to: "lead", requiredPolicy: "if_reopened", requiresReason: false },
  { from: "unscored", to: "scored", requiredPolicy: null, requiresReason: false },
  { from: "unscored", to: "screened_out", requiredPolicy: null, requiresReason: false },
  { from: "unscored", to: "closed", requiredPolicy: null, requiresReason: false },
];

export function getValidTransitionTargets(
  current: PostingDisposition | string,
  storedPolicy: ResurfacePolicy | string = "never"
): TransitionRule[] {
  return TRANSITIONS.filter((t) => {
    if (t.from !== current) return false;
    if (t.requiredPolicy && t.requiredPolicy !== storedPolicy) return false;
    return true;
  });
}

/**
 * Determines whether an application can be created for a posting given its current disposition.
 * 'applied': already applied, forbidden.
 * 'rejected': must be reopened to lead first via disposition transition, forbidden.
 * 'screened_out' | 'closed': decided, allowed ONLY with overrideDecided=true and overrideReason.
 * 'lead' | 'scored' | 'unscored': actively in flight, allowed without override.
 */
export function canCreateApplication(
  disposition: PostingDisposition | string
): { allowed: boolean; requiresOverride: boolean } {
  const normalized = String(disposition).toLowerCase();
  if (normalized === "applied" || normalized === "rejected") {
    return { allowed: false, requiresOverride: false };
  }
  if (normalized === "screened_out" || normalized === "closed") {
    return { allowed: true, requiresOverride: true };
  }
  return { allowed: true, requiresOverride: false };
}
