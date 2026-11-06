/**
 * Deterministic UTC date formatter for ISO 8601 date strings.
 *
 * Avoids locale/timezone hydration discrepancies between server SSR
 * and client hydration by formatting strictly as UTC YYYY-MM-DD.
 * Invalid date inputs safely return the raw input string.
 */
export function formatDate(iso: string | null | undefined): string {
  if (!iso || typeof iso !== "string") {
    return "";
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  const day = String(date.getUTCDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

/**
 * Short UTC month and day ("Jul 6") for chart axis labels. Fixed English month names, never a locale, so server and
 * client agree. Invalid input behaves as in `formatDate`.
 */
export function formatShortDate(iso: string | null | undefined): string {
  if (!iso || typeof iso !== "string") {
    return "";
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return `${MONTHS[date.getUTCMonth()]} ${date.getUTCDate()}`;
}
