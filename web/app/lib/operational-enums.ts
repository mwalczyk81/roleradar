export const CRITERION_CATEGORY_VALUES = ["hard_skip", "weight", "preference"] as const;
export type CriterionCategory = (typeof CRITERION_CATEGORY_VALUES)[number];

export const SUPPORTED_ENTITY_ROUTE_TYPES = ["posting", "company", "application"] as const;
export type SupportedEntityRouteType = (typeof SUPPORTED_ENTITY_ROUTE_TYPES)[number];

export function isCriterionCategory(val: unknown): val is CriterionCategory {
  return typeof val === "string" && (CRITERION_CATEGORY_VALUES as readonly string[]).includes(val);
}

export function isSupportedEntityRouteType(val: unknown): val is SupportedEntityRouteType {
  return typeof val === "string" && (SUPPORTED_ENTITY_ROUTE_TYPES as readonly string[]).includes(val);
}

export const CRITERION_CATEGORY_METADATA: Record<
  CriterionCategory,
  { label: string; tone: "danger" | "primary" | "neutral" }
> = {
  hard_skip: { label: "Hard Skip", tone: "danger" },
  weight: { label: "Weighted", tone: "primary" },
  preference: { label: "Preference", tone: "neutral" },
};

export function getEntityRoute(
  type: string | null | undefined,
  id: string | null | undefined
): string | null {
  if (!type || !id) return null;
  const normalized = type.toLowerCase().trim();
  if (normalized === "posting") return `/postings/${id}`;
  if (normalized === "company") return `/companies/${id}`;
  if (normalized === "application") return `/applications/${id}`;
  return null;
}
