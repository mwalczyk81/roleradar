/**
 * Strongly typed OData v4 query builder enforcing strict spec invariants:
 * - String functions (contains, startswith, endswith) are always generated as function(tolower(property), 'lowercased term')
 * - Max $top clamped to 1..100
 * - Disallowed options are excluded
 */

export type SortDirection = "asc" | "desc";

export interface OrderByClause {
  field: string;
  direction?: SortDirection;
}

export type FilterExpression =
  | {
      type: "comparison";
      operator: "eq" | "ne" | "gt" | "ge" | "lt" | "le";
      field: string;
      value: string | number | boolean | null;
    }
  | {
      type: "function";
      name: "contains" | "startswith" | "endswith";
      field: string;
      value: string;
    }
  | {
      type: "binary";
      operator: "and" | "or";
      left: FilterExpression;
      right: FilterExpression;
    }
  | {
      type: "not";
      inner: FilterExpression;
    };

export interface ODataQueryParams {
  filter?: FilterExpression | string;
  orderBy?: OrderByClause[] | string;
  top?: number;
  skip?: number;
  count?: boolean;
  select?: string[];
}

export function formatFilterExpression(expr: FilterExpression): string {
  switch (expr.type) {
    case "comparison": {
      const valStr =
        expr.value === null
          ? "null"
          : typeof expr.value === "string"
            ? `'${expr.value.replace(/'/g, "''")}'`
            : `${expr.value}`;
      return `${expr.field} ${expr.operator} ${valStr}`;
    }
    case "function": {
      // Must enforce tolower() on property and lowercased search term
      const lowerVal = expr.value.toLowerCase().replace(/'/g, "''");
      return `${expr.name}(tolower(${expr.field}),'${lowerVal}')`;
    }
    case "binary": {
      const left = formatFilterExpression(expr.left);
      const right = formatFilterExpression(expr.right);
      return `(${left}) ${expr.operator} (${right})`;
    }
    case "not": {
      const inner = formatFilterExpression(expr.inner);
      return `not (${inner})`;
    }
  }
}

export function buildODataQuery(params: ODataQueryParams): string {
  const parts: string[] = [];

  if (params.filter) {
    const filterStr =
      typeof params.filter === "string"
        ? params.filter
        : formatFilterExpression(params.filter);
    if (filterStr) {
      parts.push(`$filter=${encodeURIComponent(filterStr)}`);
    }
  }

  if (params.orderBy) {
    if (typeof params.orderBy === "string") {
      parts.push(`$orderby=${encodeURIComponent(params.orderBy)}`);
    } else if (params.orderBy.length > 0) {
      const orderStr = params.orderBy
        .map((o) => (o.direction ? `${o.field} ${o.direction}` : o.field))
        .join(",");
      parts.push(`$orderby=${encodeURIComponent(orderStr)}`);
    }
  }

  if (params.top !== undefined) {
    const clampedTop = Math.max(1, Math.min(100, params.top));
    parts.push(`$top=${clampedTop}`);
  }

  if (params.skip !== undefined) {
    const clampedSkip = Math.max(0, params.skip);
    parts.push(`$skip=${clampedSkip}`);
  }

  if (params.count !== undefined) {
    parts.push(`$count=${params.count ? "true" : "false"}`);
  }

  if (params.select && params.select.length > 0) {
    parts.push(`$select=${encodeURIComponent(params.select.join(","))}`);
  }

  return parts.join("&");
}

export interface ODataPage<T> {
  value: T[];
  "@odata.count"?: number;
}
