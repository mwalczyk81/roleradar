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

const cx = (...parts) => parts.filter(Boolean).join(" ");

const PERCENT = /^(\d{1,3})%$/;

/** A whole-percent column width is a class (server-rendered); any other CSS length is applied after mount. */
function widthClass(width) {
  const m = typeof width === "string" ? PERCENT.exec(width) : null;
  return m && Number(m[1]) <= 100 ? `ds-w-${m[1]}` : undefined;
}

function SortIcon({ direction }) {
  // Up chevron when ascending, down when descending, stacked pair when unsorted.
  return (
    <svg className="ds-sort-icon" data-active={direction ? "" : undefined} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
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
 * A column `width` of a whole percent ("20%") is server-rendered through a class; any other CSS
 * length ("8rem") and `maxHeight` are applied after the page loads (bundle.css; no inline styles).
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
  className,
  style,
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

  const scrollRef = React.useRef(null);
  const rootRef = React.useRef(null);

  // Free-form sizes cannot be classes; set them through the CSSOM once the table is mounted.
  React.useLayoutEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    columns.forEach((c, i) => {
      if (!c.width || widthClass(c.width)) return;
      root.querySelectorAll(`[data-col="${i}"]`).forEach((el) => {
        el.style.width = typeof c.width === "number" ? `${c.width}px` : c.width;
      });
    });
    if (scrollRef.current && maxHeight !== undefined) {
      scrollRef.current.style.maxHeight = typeof maxHeight === "number" ? `${maxHeight}px` : maxHeight;
    }
  });

  const total = pagination ? pagination.total : 0;
  const limit = pagination ? pagination.limit : 0;
  const offset = pagination ? pagination.offset : 0;
  const from = total === 0 ? 0 : offset + 1;
  const to = Math.min(offset + limit, total);

  return (
    <div {...rest} ref={rootRef} className={cx("ds-datatable-root", className)} style={style}>
      <div
        ref={scrollRef}
        className="ds-datatable-scroll"
        {...(scrollable ? { tabIndex: 0, role: "region", "aria-label": ariaLabel || caption || "Table" } : null)}
      >
        <table
          className="ds-datatable"
          data-sticky={stickyHeader ? "" : undefined}
          aria-busy={isLoading || undefined}
          aria-label={!caption ? ariaLabel : undefined}
        >
          {caption ? <caption className="ds-visually-hidden">{caption}</caption> : null}
          <thead>
            <tr>
              {columns.map((c, i) => {
                const on = c.sortable && c.key === activeCol;
                return (
                  <th
                    key={c.key}
                    scope="col"
                    data-col={i}
                    data-align={cellAlign(c)}
                    className={widthClass(c.width)}
                    aria-sort={c.sortable ? (on ? (activeDir === "desc" ? "descending" : "ascending") : "none") : undefined}
                  >
                    {c.sortable ? (
                      <button
                        type="button"
                        className="ds-datatable-sort"
                        data-active={on ? "" : undefined}
                        data-reverse={cellAlign(c) === "right" ? "" : undefined}
                        onClick={() => toggleSort(c.key)}
                        title={sortScope === "all" ? "Sorts all rows" : "Sorts the rows on this page"}
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
                    {columns.map((c, ci) => (
                      <td key={c.key} data-col={ci} data-align={cellAlign(c)} data-numeric={c.numeric ? "" : undefined} className={widthClass(c.width)}>
                        <span className="ds-skeleton-block ds-datatable-skeleton" data-numeric={c.numeric ? "" : undefined} />
                      </td>
                    ))}
                  </tr>
                ))
              : rows.length === 0
              ? (
                  <tr>
                    <td colSpan={Math.max(columns.length, 1)} className="ds-datatable-empty">
                      <div>{emptyMessage}</div>
                      {emptyAction ? <div className="ds-datatable-empty-action">{emptyAction}</div> : null}
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
                    {columns.map((c, ci) => {
                      const raw = c.render ? c.render(row) : row[c.key];
                      return (
                        <td key={c.key} data-col={ci} data-align={cellAlign(c)} data-numeric={c.numeric ? "" : undefined} className={widthClass(c.width)}>
                          {isEmpty(raw) ? <span className="ds-datatable-dash">{DASH}</span> : raw}
                        </td>
                      );
                    })}
                  </tr>
                ))}
          </tbody>
        </table>
      </div>
      {((hasSortable && !isLoading && rows.length > 1) || pagination) && (
        <div className="ds-datatable-footer">
          <span>{hasSortable && sortScope !== "all" && !isLoading && rows.length > 1 ? "Sorting applies to the rows on this page only." : ""}</span>
          {pagination ? (
            <nav aria-label="Pagination" className="ds-datatable-pager">
              <span aria-live="polite" className="ds-datatable-range">
                {total === 0 ? "No results" : `Showing ${from}–${to} of ${total}`}
              </span>
              <button type="button" className="ds-pager-btn" disabled={offset <= 0} onClick={() => pagination.onPageChange(Math.max(0, offset - limit))}>
                Previous
              </button>
              <button type="button" className="ds-pager-btn" disabled={offset + limit >= total} onClick={() => pagination.onPageChange(offset + limit)}>
                Next
              </button>
            </nav>
          ) : null}
        </div>
      )}
    </div>
  );
}
