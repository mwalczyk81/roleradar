import React from "react";
import { Badge } from "../feedback/Badge.jsx";

// Shared internals for StatTile, BarChart, ColumnChart and Sparkline. Not a component:
// nothing here is exposed on the namespace. Each chart component bundles its own copy.

/** A denominator under this is a small sample and gets a visible note. */
export const SMALL_SAMPLE = 10;

const NF = new Intl.NumberFormat("en-US");
const CF = new Intl.NumberFormat("en-US", { notation: "compact", maximumFractionDigits: 1 });

/** 1,284 / 12.9K / 4.2M: plain grouping below 10,000, compact from there. */
export function formatCount(n) {
  if (typeof n !== "number" || !Number.isFinite(n)) return "—";
  return Math.abs(n) >= 10000 ? CF.format(n) : NF.format(n);
}

/** Axis ticks and value labels: compact from 1,000 (0 / 1K / 2K). */
export function formatTick(n) {
  return Math.abs(n) >= 1000 ? CF.format(n) : NF.format(n);
}

/** Whole-number percentage of `whole`; an em dash when there is nothing to divide by. */
export function formatPercent(part, whole) {
  if (!whole) return "—";
  const p = (part / whole) * 100;
  if (p > 0 && p < 1) return "<1%";
  return Math.round(p) + "%";
}

/** Round the top of an axis up to a clean number and return the ticks from 0 to it. */
export function niceTicks(max) {
  if (!(max > 0)) return { top: 1, ticks: [0, 1] };
  const raw = max / 4;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 5, 10].map((m) => m * mag).find((s) => s >= raw);
  const top = Math.ceil(max / step) * step;
  const ticks = [];
  for (let t = 0; t <= top + step / 1000; t += step) ticks.push(Math.round(t * 1e6) / 1e6);
  return { top, ticks };
}

export const surfaceStyle = {
  background: "var(--ds-surface)",
  border: "var(--ds-border-thin) solid var(--ds-border)",
  borderRadius: "var(--ds-radius-lg)",
  boxShadow: "var(--ds-shadow-sm)",
  padding: "var(--ds-space-6)",
};

/** One tab stop for a whole chart; arrow keys, Home and End move between marks. */
export function useRoving(count) {
  const [active, setActive] = React.useState(0);
  const refs = React.useRef([]);
  const current = Math.min(active, Math.max(count - 1, 0));
  const move = (next) => {
    const i = Math.max(0, Math.min(count - 1, next));
    setActive(i);
    const el = refs.current[i];
    if (el) el.focus();
  };
  const attrs = (i) => ({
    ref: (el) => {
      refs.current[i] = el;
    },
    tabIndex: i === current ? 0 : -1,
    onKeyDown: (e) => {
      if (e.key === "ArrowRight" || e.key === "ArrowDown") move(i + 1);
      else if (e.key === "ArrowLeft" || e.key === "ArrowUp") move(i - 1);
      else if (e.key === "Home") move(0);
      else if (e.key === "End") move(count - 1);
      else return;
      e.preventDefault();
    },
  });
  return { attrs, setActive };
}

/** Tooltip state for one chart: the figure is the positioning frame, marks call show/hide. */
export function useChartTip() {
  const figureRef = React.useRef(null);
  const [tip, setTip] = React.useState(null);
  const show = (index, anchor) => {
    const fig = figureRef.current;
    if (!fig || !anchor) return;
    const f = fig.getBoundingClientRect();
    const a = anchor.getBoundingClientRect();
    setTip({ index, x: a.left - f.left + a.width / 2, y: a.top - f.top });
  };
  const hide = () => setTip(null);
  return { figureRef, tip, show, hide };
}

/** The element a mark's tooltip points at: its bar if it has one, else the mark itself. */
export function anchorOf(event) {
  const mark = event.currentTarget;
  return mark.querySelector(".ds-chart-bar") || mark;
}

/** Elements width, measured; 0 until the first measurement (and always 0 without ResizeObserver). */
export function useWidth(ref) {
  const [width, setWidth] = React.useState(0);
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    setWidth(el.clientWidth);
    if (typeof ResizeObserver === "undefined") return undefined;
    const ro = new ResizeObserver(() => setWidth(el.clientWidth));
    ro.observe(el);
    return () => ro.disconnect();
  }, [ref]);
  return width;
}

/** Floating readout above a mark. Values lead, the label follows. Clamped to the figure. */
export function ChartTip({ x, y, figureRef, children }) {
  const ref = React.useRef(null);
  const [shift, setShift] = React.useState(0);
  React.useLayoutEffect(() => {
    const el = ref.current;
    const fig = figureRef.current;
    if (!el || !fig) return;
    const w = el.offsetWidth;
    const fw = fig.clientWidth;
    const margin = 4;
    const left = x - w / 2;
    let next = 0;
    if (left < margin) next = margin - left;
    else if (fw && left + w > fw - margin) next = fw - margin - (left + w);
    setShift(next);
  }, [x, y, figureRef]);
  return (
    <div
      ref={ref}
      aria-hidden="true"
      style={{
        position: "absolute",
        left: x,
        top: y,
        transform: `translate(calc(-50% + ${shift}px), calc(-100% - 8px))`,
        zIndex: "var(--ds-z-overlay)",
        pointerEvents: "none",
        width: "max-content",
        maxWidth: "16rem",
        padding: "var(--ds-space-1) var(--ds-space-2)",
        background: "var(--ds-text)",
        color: "var(--ds-bg)",
        borderRadius: "var(--ds-radius-md)",
        boxShadow: "var(--ds-shadow-md)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-xs)",
        lineHeight: "var(--ds-leading-normal)",
      }}
    >
      {children}
    </div>
  );
}

/** Tooltip row: a short key stroke in the series color, the value in strong, then the label. */
export function TipBody({ color, value, label }) {
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: "var(--ds-space-2)" }}>
      <span aria-hidden="true" style={{ width: "0.75rem", height: "2px", flex: "none", background: color, borderRadius: "1px" }} />
      <strong style={{ fontWeight: "var(--ds-weight-semibold)", fontVariantNumeric: "tabular-nums" }}>{value}</strong>
      <span>{label}</span>
    </span>
  );
}

/** Native table twin of a chart: every value the chart shows, reachable without a pointer. */
export function TableView({ caption, columns, rows }) {
  const th = {
    padding: "var(--ds-space-2) var(--ds-space-3)",
    borderBottom: "var(--ds-border-thin) solid var(--ds-border)",
    color: "var(--ds-text-secondary)",
    fontSize: "var(--ds-text-xs)",
    fontWeight: "var(--ds-weight-medium)",
    letterSpacing: "0.04em",
    textTransform: "uppercase",
    whiteSpace: "nowrap",
  };
  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse", fontFamily: "var(--ds-font-sans)" }}>
        <caption className="ds-visually-hidden">{caption}</caption>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.header} scope="col" style={{ ...th, textAlign: c.numeric ? "right" : "left" }}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => (
            <tr key={r}>
              {row.map((cell, c) => (
                <td
                  key={c}
                  style={{
                    padding: "var(--ds-space-2) var(--ds-space-3)",
                    borderBottom: r === rows.length - 1 ? "0" : "var(--ds-border-thin) solid var(--ds-border)",
                    textAlign: columns[c].numeric ? "right" : "left",
                    color: "var(--ds-text)",
                    fontSize: "var(--ds-text-sm)",
                    fontFamily: columns[c].numeric ? "var(--ds-font-mono)" : "var(--ds-font-sans)",
                    fontVariantNumeric: columns[c].numeric ? "tabular-nums" : undefined,
                  }}
                >
                  {cell === "" || cell === null || cell === undefined ? "—" : cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function ChartEmpty({ children }) {
  return (
    <div style={{ padding: "var(--ds-space-8) var(--ds-space-4)", textAlign: "center", color: "var(--ds-text-secondary)", fontSize: "var(--ds-text-sm)" }}>
      {children}
    </div>
  );
}

/** "Small sample" note for a denominator under the threshold. */
export function SampleNote({ children = "Small sample" }) {
  return <Badge tone="neutral">{children}</Badge>;
}

/**
 * The shared chart frame: a <figure> with a title, an optional subtitle and a "Table view"
 * toggle. The chart is the default view; the table is its accessible twin. While `isLoading`
 * the previous render stays in place, dimmed, so the layout does not jump.
 */
export function ChartFrame({ figureRef, title, subtitle, framed = true, isLoading = false, plot, table, tip, onViewChange, style = {}, ...rest }) {
  const [view, setView] = React.useState("chart");
  return (
    <figure
      {...rest}
      ref={figureRef}
      aria-busy={isLoading || undefined}
      style={{
        position: "relative",
        margin: 0,
        display: "grid",
        gridTemplateColumns: "minmax(0, 1fr) auto",
        columnGap: "var(--ds-space-4)",
        rowGap: "var(--ds-space-4)",
        alignItems: "start",
        fontFamily: "var(--ds-font-sans)",
        color: "var(--ds-text)",
        ...(framed ? surfaceStyle : null),
        ...style,
      }}
    >
      <figcaption style={{ minWidth: 0 }}>
        <div style={{ fontSize: "var(--ds-text-lg)", fontWeight: "var(--ds-weight-semibold)", lineHeight: "var(--ds-leading-tight)" }}>{title}</div>
        {subtitle ? <div style={{ marginTop: "var(--ds-space-1)", fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}>{subtitle}</div> : null}
      </figcaption>
      <button
        type="button"
        className="ds-chart-toggle"
        aria-pressed={view === "table"}
        onClick={() => {
          setView(view === "table" ? "chart" : "table");
          if (onViewChange) onViewChange();
        }}
        style={{
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
          whiteSpace: "nowrap",
        }}
      >
        Table view
      </button>
      <div style={{ gridColumn: "1 / -1", minWidth: 0, opacity: isLoading ? 0.5 : 1, transition: "opacity var(--ds-duration-base) var(--ds-ease-standard)" }}>
        {view === "table" ? table : plot}
      </div>
      {view === "chart" ? tip : null}
    </figure>
  );
}
