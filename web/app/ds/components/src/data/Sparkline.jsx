import React from "react";
import { formatCount } from "./chartParts.jsx";

const PAD = 6; // the end dot is 8px across plus a 2px ring, so the line stays 6px clear of every edge

/**
 * Sparkline — a tiny trend line with no axes. The line is the de-emphasis gray and the latest
 * point is the accent, so the eye lands on "now". It has no hover layer and no table of its
 * own: it is a word-sized picture, so put the numbers in the text beside it (StatTile does)
 * and use a ColumnChart where readers need to read values. The accessible name summarises the
 * range; pass `label` to say what the line is.
 */
export function Sparkline({ values = [], width = 96, height = 28, label, valueFormat = formatCount, surface = "var(--ds-surface)", className, style, ...rest }) {
  const n = values.length;
  if (n === 0) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min;
  const x = (i) => (n === 1 ? width / 2 : PAD + (i * (width - PAD * 2)) / (n - 1));
  const y = (v) => (span === 0 ? height / 2 : height - PAD - ((v - min) / span) * (height - PAD * 2));
  const points = values.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const summary = `${n} points, from ${valueFormat(values[0])} to ${valueFormat(values[n - 1])}, low ${valueFormat(min)}, high ${valueFormat(max)}`;
  return (
    <svg
      {...rest}
      role="img"
      aria-label={label ? `${label}: ${summary}` : `Trend: ${summary}`}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      className={["ds-sparkline", className].filter(Boolean).join(" ")}
      style={style}
    >
      {n > 1 ? (
        <polyline
          className="ds-chart-line"
          points={points}
          fill="none"
          stroke="var(--ds-chart-muted)"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      ) : null}
      <circle className="ds-chart-dot" cx={x(n - 1)} cy={y(values[n - 1])} r="4" fill="var(--ds-chart-primary)" stroke={surface} strokeWidth="2" />
    </svg>
  );
}
