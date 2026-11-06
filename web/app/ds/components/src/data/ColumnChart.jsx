import React from "react";
import { ChartEmpty, ChartFrame, ChartTip, TableView, TipBody, anchorOf, formatCount, formatTick, niceTicks, useChartTip, useRoving, useWidth } from "./chartParts.jsx";

const MIN_SLOT = 20; // columns never get narrower; the plot scrolls sideways instead
const LABEL_WIDTH = 48; // space one x-axis label needs

/** Whole percent, 0 to 100, as a class suffix: bundle.css has one rule per value, so marks paint before any script runs. */
const pctOf = (value, whole) => Math.max(0, Math.min(100, Math.round(whole > 0 ? (value / whole) * 100 : 0)));

/** The plot height in px, rounded to a multiple of 10 between 60 and 400 (bundle.css has one rule per step). */
const plotHeight = (height) => Math.max(60, Math.min(400, Math.round((Number(height) || 180) / 10) * 10));

/**
 * ColumnChart — vertical columns for counts over time buckets, such as applications per week.
 * Columns are capped at 24px wide with a 4px rounded top and a square base on one baseline.
 * The y axis is rounded to clean numbers (0 / 1K / 2K). Values sit on the column caps while
 * they fit; when the columns get narrow only the peak and the latest keep a value, and the
 * x-axis labels thin out to fit. Hover or focus a column for its readout (`fullLabel` is the
 * long form, such as "Week of Oct 5"); arrow keys move between columns (one tab stop). A
 * "Table view" button swaps the plot for a native table of every value.
 * Column heights and tick positions are whole percents and `height` snaps to a multiple of
 * 10px (60 to 400): the sizes are classes in bundle.css, not inline styles.
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

  // The plot never gets narrower than its columns; set after mount (the container width is only known there).
  React.useLayoutEffect(() => {
    if (plotRef.current) plotRef.current.style.minWidth = `${n * MIN_SLOT}px`;
  }, [n]);

  const plot =
    n === 0 ? (
      <ChartEmpty>{isLoading ? "Loading" : emptyMessage}</ChartEmpty>
    ) : (
      <div className="ds-colchart" data-plot-h={plotHeight(height)}>
        <div aria-hidden="true" className="ds-colchart-axis">
          <div className="ds-colchart-area">
            {ticks.map((t) => (
              <span key={t} className={`ds-colchart-tick ds-b-${pctOf(t, top)}`}>
                {formatTick(t)}
              </span>
            ))}
          </div>
        </div>
        <div className="ds-colchart-scroll">
          <div ref={plotRef} onKeyDown={(e) => e.key === "Escape" && hide()} className="ds-colchart-plot">
            <div aria-hidden="true" className="ds-colchart-area">
              {ticks.map((t) => (
                <span key={t} className={`ds-colchart-gridline ds-b-${pctOf(t, top)}`} data-base={t === 0 ? "" : undefined} />
              ))}
            </div>
            <div role="group" aria-label={title} className="ds-colchart-area ds-colchart-slots">
              {data.map((d, i) => {
                const pct = pctOf(d.value, top);
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
                  >
                    <span className={`ds-chart-bar ds-chart-bar-col ds-h-${pct}`} data-nz={d.value > 0 ? "" : undefined} />
                    {showValue(i) ? (
                      <span aria-hidden="true" className={`ds-colchart-value ds-cap-${pct}`}>
                        {formatTick(d.value)}
                      </span>
                    ) : null}
                  </div>
                );
              })}
            </div>
            <div aria-hidden="true" className="ds-colchart-labels">
              {data.map((d, i) => (
                <span key={`${i}-${d.label}`} className="ds-colchart-label">
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
      tip={
        active ? (
          <ChartTip x={tip.x} y={tip.y} figureRef={figureRef}>
            <TipBody tone="primary" value={valueFormat(active.value)} label={active.fullLabel || active.label} />
          </ChartTip>
        ) : null
      }
    />
  );
}
