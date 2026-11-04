import React from "react";
import { ChartEmpty, ChartFrame, ChartTip, TableView, TipBody, anchorOf, formatCount, formatTick, niceTicks, useChartTip, useRoving, useWidth } from "./chartParts.jsx";

const TOP_PAD = 20; // room above the tallest column for its value
const LABEL_ROW = 24; // x-axis labels
const MIN_SLOT = 20; // columns never get narrower; the plot scrolls sideways instead
const LABEL_WIDTH = 48; // space one x-axis label needs

/**
 * ColumnChart — vertical columns for counts over time buckets, such as applications per week.
 * Columns are capped at 24px wide with a 4px rounded top and a square base on one baseline.
 * The y axis is rounded to clean numbers (0 / 1K / 2K). Values sit on the column caps while
 * they fit; when the columns get narrow only the peak and the latest keep a value, and the
 * x-axis labels thin out to fit. Hover or focus a column for its readout (`fullLabel` is the
 * long form, such as "Week of Oct 5"); arrow keys move between columns (one tab stop). A
 * "Table view" button swaps the plot for a native table of every value.
 */
export function ColumnChart({
  title,
  subtitle,
  data = [],
  height = 180,
  valueFormat = formatCount,
  labelHeader = "Period",
  valueHeader = "Count",
  framed = true,
  isLoading = false,
  emptyMessage = "No data for this period.",
  style = {},
  ...rest
}) {
  const { figureRef, tip, show, hide } = useChartTip();
  const roving = useRoving(data.length);
  const plotRef = React.useRef(null);
  const width = useWidth(plotRef);

  const n = data.length;
  const max = data.reduce((m, d) => Math.max(m, d.value), 0);
  const { top, ticks } = niceTicks(max);
  const slot = width && n ? width / n : 0;
  const labelAll = slot ? slot >= 36 : n <= 12;
  const step = slot ? Math.max(1, Math.ceil(LABEL_WIDTH / slot)) : n <= 8 ? 1 : n <= 16 ? 2 : 4;
  const peak = data.findIndex((d) => d.value === max);
  const showValue = (i) => labelAll || i === peak || i === n - 1;
  const total = TOP_PAD + height + LABEL_ROW;

  const plot =
    n === 0 ? (
      <ChartEmpty>{isLoading ? "Loading" : emptyMessage}</ChartEmpty>
    ) : (
      <div style={{ display: "flex", gap: "var(--ds-space-2)" }}>
        <div aria-hidden="true" style={{ position: "relative", flex: "0 0 2.25rem", height: total }}>
          <div style={{ position: "absolute", top: TOP_PAD, left: 0, right: 0, height }}>
            {ticks.map((t) => (
              <span
                key={t}
                style={{ position: "absolute", right: 0, bottom: `${(t / top) * 100}%`, transform: "translateY(50%)", fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)", fontVariantNumeric: "tabular-nums" }}
              >
                {formatTick(t)}
              </span>
            ))}
          </div>
        </div>
        <div style={{ flex: 1, minWidth: 0, overflowX: "auto", paddingRight: "var(--ds-space-2)" }}>
          <div ref={plotRef} onKeyDown={(e) => e.key === "Escape" && hide()} style={{ position: "relative", height: total, minWidth: n * MIN_SLOT }}>
            <div aria-hidden="true" style={{ position: "absolute", top: TOP_PAD, left: 0, right: 0, height }}>
              {ticks.map((t) => (
                <span
                  key={t}
                  style={{ position: "absolute", left: 0, right: 0, bottom: `${(t / top) * 100}%`, height: 0, borderTop: `var(--ds-border-thin) solid ${t === 0 ? "var(--ds-border-strong)" : "var(--ds-border)"}` }}
                />
              ))}
            </div>
            <div role="group" aria-label={title} style={{ position: "absolute", top: TOP_PAD, left: 0, right: 0, height, display: "flex" }}>
              {data.map((d, i) => {
                const pct = (d.value / top) * 100;
                const name = d.fullLabel || d.label;
                return (
                  <div
                    key={`${i}-${d.label}`}
                    {...roving.attrs(i)}
                    role="img"
                    aria-label={`${name}: ${valueFormat(d.value)}`}
                    className="ds-chart-slot"
                    onPointerEnter={(e) => show(i, anchorOf(e))}
                    onPointerLeave={hide}
                    onFocus={(e) => {
                      roving.setActive(i);
                      show(i, anchorOf(e));
                    }}
                    onBlur={hide}
                    style={{ position: "relative", flex: "1 1 0", minWidth: 0, height: "100%", display: "flex", alignItems: "flex-end", justifyContent: "center" }}
                  >
                    <span
                      className="ds-chart-bar"
                      style={{
                        width: "min(24px, calc(100% - 4px))",
                        height: `${pct}%`,
                        minHeight: d.value > 0 ? "2px" : 0,
                        background: "var(--ds-chart-primary)",
                        borderRadius: "4px 4px 0 0",
                        transition: "background var(--ds-duration-fast) var(--ds-ease-standard)",
                      }}
                    />
                    {showValue(i) ? (
                      <span
                        aria-hidden="true"
                        style={{ position: "absolute", left: "50%", bottom: `calc(${pct}% + 4px)`, transform: "translateX(-50%)", whiteSpace: "nowrap", fontSize: "var(--ds-text-xs)", fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text)" }}
                      >
                        {formatTick(d.value)}
                      </span>
                    ) : null}
                  </div>
                );
              })}
            </div>
            <div aria-hidden="true" style={{ position: "absolute", top: TOP_PAD + height, left: 0, right: 0, height: LABEL_ROW, display: "flex" }}>
              {data.map((d, i) => (
                <span key={`${i}-${d.label}`} style={{ flex: "1 1 0", minWidth: 0, display: "flex", justifyContent: "center", paddingTop: "var(--ds-space-1)", fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)", whiteSpace: "nowrap" }}>
                  {i % step === 0 ? d.label : ""}
                </span>
              ))}
            </div>
          </div>
        </div>
      </div>
    );

  const table = <TableView caption={title} columns={[{ header: labelHeader }, { header: valueHeader, numeric: true }]} rows={data.map((d) => [d.fullLabel || d.label, valueFormat(d.value)])} />;

  const active = tip && data[tip.index];
  return (
    <ChartFrame
      {...rest}
      figureRef={figureRef}
      title={title}
      subtitle={subtitle}
      framed={framed}
      isLoading={isLoading}
      plot={plot}
      table={table}
      onViewChange={hide}
      style={style}
      tip={
        active ? (
          <ChartTip x={tip.x} y={tip.y} figureRef={figureRef}>
            <TipBody color="var(--ds-chart-primary)" value={valueFormat(active.value)} label={active.fullLabel || active.label} />
          </ChartTip>
        ) : null
      }
    />
  );
}
