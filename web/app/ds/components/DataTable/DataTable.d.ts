import * as React from "react";

export interface DataTableColumn<T = any> {
  /** Field name on each row; also the sort key. */
  key: string;
  /** Column heading text (sentence case; shown uppercase). */
  header: React.ReactNode;
  /** Allow sorting this column. Sorting applies to the rows on the current page only. */
  sortable?: boolean;
  /** Monospace, right-aligned, tabular figures. Use for numbers, scores and money. */
  numeric?: boolean;
  /** Text alignment for non-numeric columns. @default "left" */
  align?: "left" | "right" | "center";
  /** CSS width of the column. */
  width?: string;
  /** Custom cell content. Empty values show an em dash. */
  render?: (row: T) => React.ReactNode;
  /** Value to sort by when it differs from `row[key]`. */
  sortValue?: (row: T) => string | number | Date | null | undefined;
}

export interface DataTablePagination {
  /** Total rows across all pages. */
  total: number;
  /** Rows per page. */
  limit: number;
  /** Zero-based index of the first row on this page. */
  offset: number;
  /** Called with the new offset when Previous or Next is pressed. */
  onPageChange: (offset: number) => void;
}

export interface DataTableProps<T = any> extends Omit<React.HTMLAttributes<HTMLDivElement>, "children"> {
  columns: DataTableColumn<T>[];
  /** The rows for the current page. */
  data: T[];
  /** Field that uniquely identifies a row. @default "id" */
  keyField?: keyof T & string;
  /** Visually hidden table caption: the table's accessible name. */
  caption?: string;
  /** Accessible name when there is no caption. */
  ariaLabel?: string;
  /** Makes rows focusable and clickable (Enter and Space). Links and buttons inside a row keep their own behaviour. */
  onRowClick?: (row: T, event: React.SyntheticEvent) => void;
  /** Show placeholder rows and mark the table busy. @default false */
  isLoading?: boolean;
  /** Placeholder row count while loading. @default 5 */
  loadingRows?: number;
  /** Text shown when there are no rows. @default "Nothing to show." */
  emptyMessage?: React.ReactNode;
  /** Node shown under the empty message, such as a Button. */
  emptyAction?: React.ReactNode;
  /** Controlled sort column key. */
  sortColumn?: string;
  /** Initial sort column key (uncontrolled). */
  defaultSortColumn?: string;
  /** Controlled sort direction. */
  sortDirection?: "asc" | "desc";
  /** Initial sort direction. @default "asc" */
  defaultSortDirection?: "asc" | "desc";
  /** Called when a sortable header is pressed. */
  onSortChange?: (column: string, direction: "asc" | "desc") => void;
  /** Shows the range and Previous/Next controls. */
  pagination?: DataTablePagination;
  /** Keep the header visible while the body scrolls (needs `maxHeight`). @default true */
  stickyHeader?: boolean;
  /** Make the body scroll above this height (px number or CSS length). */
  maxHeight?: number | string;
}

/** Sticky-header table with page-local sorting, numeric columns and paging. */
export function DataTable<T = any>(props: DataTableProps<T>): JSX.Element;
