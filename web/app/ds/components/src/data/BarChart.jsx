import React from "react";
import { ChartEmpty, ChartFrame, ChartTip, SMALL_SAMPLE, TableView, TipBody, anchorOf, formatCount, formatPercent, useChartTip, useRoving } from "./chartParts.jsx";

/** Whole percent, 0 to 100, as a class suffix: bundle.css has one rule per value, so bars paint before any script runs. */
const pctOf = (value, whole) => Math.max(0, Math.min(100, Math.round(whole > 0 ? (value / whole) * 100 : 0)));

/**
 * BarChart — horizontal bars for comparing counts across categories. Each bar's value sits at
 * its tip. Marks are in the order given. Set `funnel` for stages that narrow: every stage after
 * the first also shows "% of previous stage" beside its name, and a stage whose previous count
 * is under `smallSampleBelow` (default 10) gets a visible "Small sample" note. To make one bar
 * the point, set `highlight` on it and the rest turn gray. A "Table view" button swaps the plot
 * for a native table of the same values. Hover or focus a bar for its readout; arrow keys move
 * between bars (one tab stop). Bar thickness is capped at 20px. Bar lengths are whole percents
 * of the longest bar (classes in bundle.css, not inline styles).
 */
export function BarChart({
  title,
  subtitle,
  data = [],
  funnel = false,
  valueFormat = formatCount,
  labelHeader = "Category",
  valueHeader = "Count",
  smallSampleBelow = SMALL_SAMPLE,
  framed = true,
  isLoading = false,
  emptyMessage = "No data for this period.",
  ...rest
}) {
  const { figureRef, tip, show, hide } = useChartTip();
  const roving = useRoving(data.length);
  const max = data.reduce((m, d) => Math.max(m, d.value), 0);
  const anyHighlight = data.some((d) => d.highlight);

  const rows = data.map((d, i) => {
    const prev = funnel && i > 0 ? data[i - 1].value : null;
    const denominator = d.denominator !== undefined ? d.denominator : prev;
    return {
      ...d,
      percent: prev === null ? null : formatPercent(d.value, prev),
      small: denominator !== null && denominator !== undefined && denominator < smallSampleBelow,
      muted: anyHighlight && !d.highlight,
    };
  });

  const ariaLabel = (r) =>
    `${r.label}: ${valueFormat(r.value)}${r.percent ? `, ${r.percent} of previous stage` : ""}${r.small ? ", small sample" : ""}`;

  const plot =
    rows.length === 0 ? (
      <ChartEmpty>{isLoading ? "Loading" : emptyMessage}</ChartEmpty>
    ) : (
      <div
        role="group"
        aria-label={title}
        onKeyDown={(e) => {
          if (e.key === "Escape") hide();
        }}
        className="ds-barchart"
      >
        {rows.map((r, i) => (
          <div
            key={`${i}-${r.label}`}
            {...roving.attrs(i)}
            role="img"
            aria-label={ariaLabel(r)}
            className="ds-chart-row"
            onPointerEnter={(e) => show(i, anchorOf(e))}
            onPointerLeave={hide}
            onFocus={(e) => {
              roving.setActive(i);
              show(i, anchorOf(e));
            }}
            onBlur={hide}
          >
            <span className="ds-barchart-name">
              <span className="ds-barchart-label">{r.label}</span>
              {r.percent || r.small ? (
                <span className="ds-barchart-note">
                  {r.percent ? `${r.percent} of previous stage` : null}
                  {r.percent && r.small ? " · " : null}
                  {r.small ? "Small sample" : null}
                </span>
              ) : null}
            </span>
            <span className="ds-barchart-track">
              <span
                className={`ds-chart-bar ds-chart-bar-row ds-bar-w-${pctOf(r.value, max)}`}
                data-muted={r.muted ? "" : undefined}
                data-nz={r.value > 0 ? "" : undefined}
              />
              <span className="ds-barchart-value">{valueFormat(r.value)}</span>
            </span>
          </div>
        ))}
      </div>
    );

  const columns = [{ header: labelHeader }, { header: valueHeader, numeric: true }];
  if (funnel) columns.push({ header: "% of previous stage", numeric: true });
  if (rows.some((r) => r.small)) columns.push({ header: "Note" });
  const table = (
    <TableView
      caption={title}
      columns={columns}
      rows={rows.map((r) => {
        const cells = [r.label, valueFormat(r.value)];
        if (funnel) cells.push(r.percent);
        if (columns.length > cells.length) cells.push(r.small ? "Small sample" : "");
        return cells;
      })}
    />
  );

  const active = tip && rows[tip.index];
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
      tip={
        active ? (
          <ChartTip x={tip.x} y={tip.y} figureRef={figureRef}>
            <TipBody
              tone={active.muted ? "muted" : "primary"}
              value={valueFormat(active.value)}
              label={`${active.label}${active.percent ? ` · ${active.percent} of previous stage` : ""}${active.small ? " · small sample" : ""}`}
            />
          </ChartTip>
        ) : null
      }
    />
  );
}
