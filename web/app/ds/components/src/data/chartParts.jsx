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

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Floating readout above a mark. Values lead, the label follows. Clamped to the figure.
 * It only exists while a mark is hovered or focused, so its position is set from script
 * (CSSOM, not a style attribute): `.ds-chart-tip` in bundle.css holds everything else.
 */
export function ChartTip({ x, y, figureRef, children }) {
  const ref = React.useRef(null);
  React.useLayoutEffect(() => {
    const el = ref.current;
    const fig = figureRef.current;
    if (!el) return;
    let shift = 0;
    if (fig) {
      const w = el.offsetWidth;
      const fw = fig.clientWidth;
      const margin = 4;
      const left = x - w / 2;
      if (left < margin) shift = margin - left;
      else if (fw && left + w > fw - margin) shift = fw - margin - (left + w);
    }
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    el.style.transform = `translate(calc(-50% + ${shift}px), calc(-100% - 8px))`;
  }, [x, y, figureRef]);
  return (
    <div ref={ref} aria-hidden="true" className="ds-chart-tip">
      {children}
    </div>
  );
}

/** Tooltip row: a short key stroke in the series colour, the value in strong, then the label. */
export function TipBody({ tone = "primary", value, label }) {
  return (
    <span className="ds-chart-tip-row">
      <span aria-hidden="true" className="ds-chart-tip-key" data-tone={tone} />
      <strong className="ds-chart-tip-value">{value}</strong>
      <span>{label}</span>
    </span>
  );
}

/** Native table twin of a chart: every value the chart shows, reachable without a pointer. */
export function TableView({ caption, columns, rows }) {
  return (
    <div className="ds-chart-table-wrap">
      <table className="ds-chart-table">
        <caption className="ds-visually-hidden">{caption}</caption>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.header} scope="col" data-numeric={c.numeric ? "" : undefined}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => (
            <tr key={r}>
              {row.map((cell, c) => (
                <td key={c} data-numeric={columns[c].numeric ? "" : undefined}>
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
  return <div className="ds-chart-empty">{children}</div>;
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
export function ChartFrame({ figureRef, title, subtitle, framed = true, isLoading = false, plot, table, tip, onViewChange, className, style, ...rest }) {
  const [view, setView] = React.useState("chart");
  return (
    <figure {...rest} ref={figureRef} aria-busy={isLoading || undefined} className={cx("ds-chart-frame", className)} data-framed={framed ? "" : undefined} style={style}>
      <figcaption className="ds-chart-caption">
        <div className="ds-chart-title">{title}</div>
        {subtitle ? <div className="ds-chart-subtitle">{subtitle}</div> : null}
      </figcaption>
      <button
        type="button"
        className="ds-chart-toggle"
        aria-pressed={view === "table"}
        onClick={() => {
          setView(view === "table" ? "chart" : "table");
          if (onViewChange) onViewChange();
        }}
      >
        Table view
      </button>
      <div className="ds-chart-body" data-loading={isLoading ? "" : undefined}>
        {view === "table" ? table : plot}
      </div>
      {view === "chart" ? tip : null}
    </figure>
  );
}
