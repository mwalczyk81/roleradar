/**
 * Company status and ownership type. Allowed values come from the generated API contract
 * (contracts/client/api-types.ts, ADR 0035); this file adds only labels, tones and order.
 */
import type { components } from "../../../contracts/client/api-types.js";
import { exhaustiveOrder } from "./enum-order.js";

type Schemas = components["schemas"];

export type CompanyStatus = Schemas["CompanyDetailDto"]["status"];

export type OwnershipType = Schemas["CompanyDetailDto"]["ownershipType"];

export type FetchMethod = Schemas["CareerSourceDto"]["fetchMethod"];

export interface StatusMetadata {
  label: string;
  tone: "neutral" | "primary" | "success" | "warning" | "danger";
}

export const COMPANY_STATUS_METADATA: Record<CompanyStatus, StatusMetadata> = {
  target: { label: "Target", tone: "success" },
  watch: { label: "Watch", tone: "warning" },
  avoid: { label: "Avoid", tone: "danger" },
  neutral: { label: "Neutral", tone: "neutral" },
};

/** UI order of the statuses (the contract order differs: avoid before neutral). */
export const COMPANY_STATUS_ORDER = exhaustiveOrder<CompanyStatus>()(["target", "watch", "neutral", "avoid"]);

export const COMPANY_STATUS_OPTIONS: readonly { value: CompanyStatus; label: string }[] = COMPANY_STATUS_ORDER.map(
  (value) => ({ value, label: COMPANY_STATUS_METADATA[value].label }),
);

export const OWNERSHIP_TYPE_LABELS: Record<OwnershipType, string> = {
  public: "Public",
  private: "Private",
  pe_backed: "PE-Backed",
  vc_backed: "VC-Backed",
  mutual: "Mutual",
  subsidiary: "Subsidiary",
  nonprofit: "Nonprofit",
  government: "Government",
  unknown: "Unknown",
};

export const OWNERSHIP_TYPE_ORDER = exhaustiveOrder<OwnershipType>()([
  "public",
  "private",
  "pe_backed",
  "vc_backed",
  "mutual",
  "subsidiary",
  "nonprofit",
  "government",
  "unknown",
]);

export const OWNERSHIP_TYPE_OPTIONS: readonly { value: OwnershipType; label: string }[] = OWNERSHIP_TYPE_ORDER.map(
  (value) => ({ value, label: OWNERSHIP_TYPE_LABELS[value] }),
);

export const FETCH_METHOD_LABELS: Record<FetchMethod, string> = {
  api: "API",
  browser: "Browser",
  blocked: "Blocked",
};

const FETCH_METHOD_ORDER = exhaustiveOrder<FetchMethod>()(["api", "browser", "blocked"]);

export const FETCH_METHOD_OPTIONS: readonly { value: FetchMethod; label: string }[] = FETCH_METHOD_ORDER.map(
  (value) => ({ value, label: FETCH_METHOD_LABELS[value] }),
);

export function isFetchMethod(val: unknown): val is FetchMethod {
  return typeof val === "string" && Object.hasOwn(FETCH_METHOD_LABELS, val);
}

export function isCompanyStatus(val: unknown): val is CompanyStatus {
  return typeof val === "string" && Object.hasOwn(COMPANY_STATUS_METADATA, val);
}

export function isOwnershipType(val: unknown): val is OwnershipType {
  return typeof val === "string" && Object.hasOwn(OWNERSHIP_TYPE_LABELS, val);
}
