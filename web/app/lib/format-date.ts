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
