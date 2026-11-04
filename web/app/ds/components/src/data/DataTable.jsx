import React from "react";

const DASH = "—";
const INTERACTIVE = "a[href], button, input, select, textarea, summary, [role='button'], [role='menuitem']";

function isEmpty(v) {
  return v === null || v === undefined || v === "";
}

function compare(a, b) {
  if (typeof a === "number" && typeof b === "number") return a - b;
  if (a instanceof Date && b instanceof Date) return a.getTime() - b.getTime();
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: "base" });
}

function SortIcon({ direction }) {
  // Up chevron when ascending, down when descending, stacked pair when unsorted.
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ flexShrink: 0, opacity: direction ? 1 : 0.5 }}>
      {direction === "asc" ? <path d="m18 15-6-6-6 6" /> : direction === "desc" ? <path d="m6 9 6 6 6-6" /> : <path d="m7 15 5 5 5-5M7 9l5-5 5 5" />}
    </svg>
  );
}

/**
 * DataTable — a sticky-header table for lists of records.
 *
 * Sorting is page-local: DataTable sorts only the rows it is given (the current page) and
 * says so in the footer. For server-side sorting, sort upstream and omit `sortable`.
 * Numbers go in `numeric` columns (monospace, right-aligned). Give the table a `caption`
 * (visually hidden) or an `ariaLabel`. With `onRowClick`, rows are focusable and respond to
 * Enter and Space; clicks on links and buttons inside a row do not trigger it.
 */
export function DataTable({
  columns = [],
  data = [],
  keyField = "id",
  caption,
  ariaLabel,
  onRowClick,
  isLoading = false,
  loadingRows = 5,
  emptyMessage = "Nothing to show.",
  emptyAction = null,
  sortColumn,
  defaultSortColumn,
  sortDirection,
  defaultSortDirection = "asc",
  onSortChange,
  sortScope = "page",
  pagination,
  stickyHeader = true,
  maxHeight,
  style = {},
  ...rest
}) {
  const [internalCol, setInternalCol] = React.useState(defaultSortColumn);
  const [internalDir, setInternalDir] = React.useState(defaultSortDirection);
  const colControlled = sortColumn !== undefined;
  const activeCol = colControlled ? sortColumn : internalCol;
  const activeDir = sortDirection !== undefined ? sortDirection : internalDir;
  const hasSortable = columns.some((c) => c.sortable);

  function toggleSort(key) {
    const next = activeCol === key && activeDir === "asc" ? "desc" : "asc";
    if (!colControlled) setInternalCol(key);
    if (sortDirection === undefined) setInternalDir(next);
    onSortChange && onSortChange(key, next);
  }

  const rows = React.useMemo(() => {
    const col = columns.find((c) => c.key === activeCol && c.sortable);
    if (!col) return data;
    const value = (row) => (col.sortValue ? col.sortValue(row) : row[col.key]);
    const sign = activeDir === "desc" ? -1 : 1;
    return data
      .map((row, i) => ({ row, i, v: value(row) }))
      .sort((x, y) => {
        const xe = isEmpty(x.v);
        const ye = isEmpty(y.v);
        if (xe || ye) return xe && ye ? x.i - y.i : xe ? 1 : -1; // empty values stay last in both directions
        return compare(x.v, y.v) * sign || x.i - y.i;
      })
      .map((x) => x.row);
  }, [data, columns, activeCol, activeDir]);

  const clickable = typeof onRowClick === "function";
  const scrollable = maxHeight !== undefined;
  const cellAlign = (c) => (c.numeric ? "right" : c.align || "left");

  const th = {
    padding: "var(--ds-space-2) var(--ds-space-4)",
    background: "var(--ds-bg-subtle)",
    borderBottom: "var(--ds-border-thin) solid var(--ds-border)",
    color: "var(--ds-text-secondary)",
    fontFamily: "var(--ds-font-sans)",
    fontSize: "var(--ds-text-xs)",
    fontWeight: "var(--ds-weight-medium)",
    letterSpacing: "0.04em",
    textTransform: "uppercase",
    whiteSpace: "nowrap",
    ...(stickyHeader ? { position: "sticky", top: 0, zIndex: "var(--ds-z-sticky)" } : null),
  };

  const td = (c) => ({
    padding: "var(--ds-space-3) var(--ds-space-4)",
    textAlign: cellAlign(c),
    color: "var(--ds-text)",
    fontSize: "var(--ds-text-sm)",
    lineHeight: "var(--ds-leading-normal)",
    fontFamily: c.numeric ? "var(--ds-font-mono)" : "var(--ds-font-sans)",
    fontVariantNumeric: c.numeric ? "tabular-nums" : undefined,
    verticalAlign: "middle",
    ...(c.width ? { width: c.width } : null),
  });

  const total = pagination ? pagination.total : 0;
  const limit = pagination ? pagination.limit : 0;
  const offset = pagination ? pagination.offset : 0;
  const from = total === 0 ? 0 : offset + 1;
  const to = Math.min(offset + limit, total);

  return (
    <div
      {...rest}
      style={{
        background: "var(--ds-surface)",
        border: "var(--ds-border-thin) solid var(--ds-border)",
        borderRadius: "var(--ds-radius-lg)",
        overflow: "hidden",
        fontFamily: "var(--ds-font-sans)",
        ...style,
      }}
    >
      <div
        className="ds-datatable-scroll"
        {...(scrollable ? { tabIndex: 0, role: "region", "aria-label": ariaLabel || caption || "Table" } : null)}
        style={{ overflow: "auto", ...(scrollable ? { maxHeight } : null) }}
      >
        <table
          className="ds-datatable"
          aria-busy={isLoading || undefined}
          aria-label={!caption ? ariaLabel : undefined}
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: 0 }}
        >
          {caption ? <caption className="ds-visually-hidden">{caption}</caption> : null}
          <thead>
            <tr>
              {columns.map((c) => {
                const on = c.sortable && c.key === activeCol;
                return (
                  <th
                    key={c.key}
                    scope="col"
                    aria-sort={c.sortable ? (on ? (activeDir === "desc" ? "descending" : "ascending") : "none") : undefined}
                    style={{ ...th, textAlign: cellAlign(c), ...(c.width ? { width: c.width } : null) }}
                  >
                    {c.sortable ? (
                      <button
                        type="button"
                        className="ds-datatable-sort"
                        onClick={() => toggleSort(c.key)}
                        title={sortScope === "all" ? "Sorts all rows" : "Sorts the rows on this page"}
                        style={{
                          appearance: "none",
                          background: "none",
                          border: "none",
                          padding: 0,
                          margin: 0,
                          font: "inherit",
                          letterSpacing: "inherit",
                          textTransform: "inherit",
                          color: on ? "var(--ds-text)" : "inherit",
                          cursor: "pointer",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "var(--ds-space-1)",
                          flexDirection: cellAlign(c) === "right" ? "row-reverse" : "row",
                        }}
                      >
                        {c.header}
                        <SortIcon direction={on ? (activeDir === "desc" ? "desc" : "asc") : null} />
                      </button>
                    ) : (
                      c.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {isLoading
              ? Array.from({ length: loadingRows }, (_, i) => (
                  <tr key={`sk-${i}`} aria-hidden="true">
                    {columns.map((c) => (
                      <td key={c.key} style={td(c)}>
                        <span className="ds-skeleton-block" style={{ display: "inline-block", height: "0.875rem", width: c.numeric ? "3rem" : "70%", borderRadius: "var(--ds-radius-sm)" }} />
                      </td>
                    ))}
                  </tr>
                ))
              : rows.length === 0
              ? (
                  <tr>
                    <td colSpan={Math.max(columns.length, 1)} style={{ padding: "var(--ds-space-10) var(--ds-space-4)", textAlign: "center", color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-sm)" }}>
                      <div>{emptyMessage}</div>
                      {emptyAction ? <div style={{ marginTop: "var(--ds-space-3)" }}>{emptyAction}</div> : null}
                    </td>
                  </tr>
                )
              : rows.map((row) => (
                  <tr
                    key={row[keyField]}
                    className="ds-datatable-row"
                    data-clickable={clickable ? "" : undefined}
                    tabIndex={clickable ? 0 : undefined}
                    onClick={(e) => {
                      if (!clickable || e.defaultPrevented) return;
                      const inner = e.target.closest && e.target.closest(INTERACTIVE);
                      if (inner && inner !== e.currentTarget && e.currentTarget.contains(inner)) return;
                      onRowClick(row, e);
                    }}
                    onKeyDown={(e) => {
                      if (!clickable || e.target !== e.currentTarget) return;
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onRowClick(row, e);
                      }
                    }}
                  >
                    {columns.map((c) => {
                      const raw = c.render ? c.render(row) : row[c.key];
                      return (
                        <td key={c.key} style={td(c)}>
                          {isEmpty(raw) ? <span style={{ color: "var(--ds-text-secondary)" }}>{DASH}</span> : raw}
                        </td>
                      );
                    })}
                  </tr>
                ))}
          </tbody>
        </table>
      </div>
      {((hasSortable && !isLoading && rows.length > 1) || pagination) && (
        <div
          style={{
            display: "flex",
            flexWrap: "wrap",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "var(--ds-space-3)",
            padding: "var(--ds-space-3) var(--ds-space-4)",
            borderTop: "var(--ds-border-thin) solid var(--ds-border)",
            background: "var(--ds-bg-subtle)",
            fontSize: "var(--ds-text-xs)",
            color: "var(--ds-text-secondary)",
          }}
        >
          <span>{hasSortable && sortScope !== "all" && !isLoading && rows.length > 1 ? "Sorting applies to the rows on this page only." : ""}</span>
          {pagination ? (
            <nav aria-label="Pagination" style={{ display: "flex", alignItems: "center", gap: "var(--ds-space-3)" }}>
              <span aria-live="polite" style={{ fontVariantNumeric: "tabular-nums" }}>
                {total === 0 ? "No results" : `Showing ${from}–${to} of ${total}`}
              </span>
              <button
                type="button"
                className="ds-pager-btn"
                disabled={offset <= 0}
                onClick={() => pagination.onPageChange(Math.max(0, offset - limit))}
                style={pagerButton}
              >
                Previous
              </button>
              <button
                type="button"
                className="ds-pager-btn"
                disabled={offset + limit >= total}
                onClick={() => pagination.onPageChange(offset + limit)}
                style={pagerButton}
              >
                Next
              </button>
            </nav>
          ) : null}
        </div>
      )}
    </div>
  );
}

const pagerButton = {
  height: "var(--ds-control-sm)",
  padding: "0 var(--ds-space-3)",
  background: "var(--ds-surface)",
  color: "var(--ds-text)",
  border: "var(--ds-border-thin) solid var(--ds-border-control)",
  borderRadius: "var(--ds-radius-md)",
  fontFamily: "var(--ds-font-sans)",
  fontSize: "var(--ds-text-sm)",
  fontWeight: "var(--ds-weight-medium)",
  cursor: "pointer",
};
