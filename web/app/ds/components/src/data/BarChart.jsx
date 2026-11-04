import React from "react";
import { ChartEmpty, ChartFrame, ChartTip, SMALL_SAMPLE, TableView, TipBody, anchorOf, formatCount, formatPercent, useChartTip, useRoving } from "./chartParts.jsx";

const BAR_HEIGHT = "1.25rem"; // 20px, under the 24px cap
const VALUE_SPACE = "4.5rem"; // room after the longest bar for its value

/**
 * BarChart — horizontal bars for comparing counts across categories. Each bar's value sits at
 * its tip. Marks are in the order given. Set `funnel` for stages that narrow: every stage after
 * the first also shows "% of previous stage" beside its name, and a stage whose previous count
 * is under `smallSampleBelow` (default 10) gets a visible "Small sample" note. To make one bar
 * the point, set `highlight` on it and the rest turn gray. A "Table view" button swaps the plot
 * for a native table of the same values. Hover or focus a bar for its readout; arrow keys move
 * between bars (one tab stop). Bar thickness is capped at 20px.
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
  style = {},
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
        style={{ display: "flex", flexDirection: "column", gap: "2px" }}
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
            style={{
              display: "grid",
              gridTemplateColumns: "minmax(6rem, 35%) minmax(0, 1fr)",
              alignItems: "center",
              columnGap: "var(--ds-space-3)",
              minHeight: "2.25rem",
              padding: "var(--ds-space-1) var(--ds-space-2)",
              borderRadius: "var(--ds-radius-md)",
            }}
          >
            <span style={{ minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }}>
              <span style={{ fontSize: "var(--ds-text-sm)", color: "var(--ds-text)", overflowWrap: "anywhere" }}>{r.label}</span>
              {r.percent || r.small ? (
                <span style={{ fontSize: "var(--ds-text-xs)", color: "var(--ds-text-secondary)" }}>
                  {r.percent ? `${r.percent} of previous stage` : null}
                  {r.percent && r.small ? " · " : null}
                  {r.small ? "Small sample" : null}
                </span>
              ) : null}
            </span>
            <span style={{ display: "flex", alignItems: "center", gap: "var(--ds-space-2)", minWidth: 0 }}>
              <span
                className="ds-chart-bar"
                data-muted={r.muted ? "" : undefined}
                style={{
                  flex: "none",
                  height: BAR_HEIGHT,
                  width: `calc((100% - ${VALUE_SPACE}) * ${max > 0 ? r.value / max : 0})`,
                  minWidth: r.value > 0 ? "2px" : 0,
                  background: r.muted ? "var(--ds-chart-muted)" : "var(--ds-chart-primary)",
                  borderRadius: "0 4px 4px 0",
                  transition: "background var(--ds-duration-fast) var(--ds-ease-standard)",
                }}
              />
              <span style={{ whiteSpace: "nowrap", fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-semibold)", color: "var(--ds-text)" }}>
                {valueFormat(r.value)}
              </span>
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
      style={style}
      tip={
        active ? (
          <ChartTip x={tip.x} y={tip.y} figureRef={figureRef}>
            <TipBody
              color={active.muted ? "var(--ds-chart-muted)" : "var(--ds-chart-primary)"}
              value={valueFormat(active.value)}
              label={`${active.label}${active.percent ? ` · ${active.percent} of previous stage` : ""}${active.small ? " · small sample" : ""}`}
            />
          </ChartTip>
        ) : null
      }
    />
  );
}
