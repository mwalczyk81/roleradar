/**
 * Company Status and Ownership Type Enums derived from Domain contract.
 * Backed by contracts/company-enums.json with bidirectional drift tests.
 */

export const COMPANY_STATUS_VALUES = [
  "target",
  "watch",
  "avoid",
  "neutral",
] as const;

export type CompanyStatus = (typeof COMPANY_STATUS_VALUES)[number];

export const OWNERSHIP_TYPE_VALUES = [
  "public",
  "private",
  "pe_backed",
  "vc_backed",
  "mutual",
  "subsidiary",
  "nonprofit",
  "government",
  "unknown",
] as const;

export type OwnershipType = (typeof OWNERSHIP_TYPE_VALUES)[number];

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

export const COMPANY_STATUS_OPTIONS: readonly { value: CompanyStatus; label: string }[] = [
  { value: "target", label: "Target" },
  { value: "watch", label: "Watch" },
  { value: "neutral", label: "Neutral" },
  { value: "avoid", label: "Avoid" },
];

export const OWNERSHIP_TYPE_OPTIONS: readonly { value: OwnershipType; label: string }[] = [
  { value: "public", label: "Public" },
  { value: "private", label: "Private" },
  { value: "pe_backed", label: "PE-Backed" },
  { value: "vc_backed", label: "VC-Backed" },
  { value: "mutual", label: "Mutual" },
  { value: "subsidiary", label: "Subsidiary" },
  { value: "nonprofit", label: "Nonprofit" },
  { value: "government", label: "Government" },
  { value: "unknown", label: "Unknown" },
];

export function isCompanyStatus(val: unknown): val is CompanyStatus {
  return typeof val === "string" && (COMPANY_STATUS_VALUES as readonly string[]).includes(val);
}

export function isOwnershipType(val: unknown): val is OwnershipType {
  return typeof val === "string" && (OWNERSHIP_TYPE_VALUES as readonly string[]).includes(val);
}
