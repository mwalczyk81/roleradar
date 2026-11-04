import React from "react";
import { Card } from "../feedback/Card.jsx";
import { Sparkline } from "./Sparkline.jsx";
import { SMALL_SAMPLE, SampleNote, formatCount } from "./chartParts.jsx";

const DELTA_ICON = {
  up: "m18 15-6-6-6 6",
  down: "m6 9 6 6 6-6",
  flat: "M5 12h14",
};
const DELTA_WORD = { up: "Up", down: "Down", flat: "No change" };

function deltaColor(good) {
  if (good === true) return "var(--ds-success-text)";
  if (good === false) return "var(--ds-danger-text)";
  return "var(--ds-text-secondary)";
}

/**
 * StatTile — one headline number with its context. `label` names it, `value` leads, `of` keeps
 * the count next to a percentage ("4 of 18"), `delta` compares with a named period, `trend`
 * draws a Sparkline. A denominator under `smallSampleBelow` (default 10) adds a visible
 * "Small sample" note, or pass `note` for your own wording. The delta's colour says whether the
 * change is good (`good`), the arrow and text say which way it went. Large figures use the
 * font's proportional digits, not tabular ones.
 */
export function StatTile({ label, value, of, delta, trend, trendLabel, note, denominator, smallSampleBelow = SMALL_SAMPLE, valueFormat = formatCount, style = {}, ...rest }) {
  const noteText = note !== undefined ? note : denominator !== undefined && denominator < smallSampleBelow ? "Small sample" : null;
  const shown = typeof value === "number" ? valueFormat(value) : value;
  return (
    <Card {...rest} style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-2)", padding: "var(--ds-space-5)", minWidth: 0, ...style }}>
      <dl style={{ margin: 0, display: "flex", flexDirection: "column", gap: "var(--ds-space-2)", minWidth: 0 }}>
        <dt style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)", color: "var(--ds-text-secondary)" }}>{label}</dt>
        <dd style={{ margin: 0, display: "flex", flexWrap: "wrap", alignItems: "flex-end", justifyContent: "space-between", columnGap: "var(--ds-space-4)", rowGap: "var(--ds-space-2)" }}>
          <span style={{ display: "flex", alignItems: "baseline", flexWrap: "wrap", columnGap: "var(--ds-space-2)", minWidth: 0 }}>
            <span style={{ fontFamily: "var(--ds-font-display)", fontSize: "var(--ds-text-3xl)", fontWeight: "var(--ds-weight-semibold)", lineHeight: "var(--ds-leading-tight)", fontVariantNumeric: "normal", color: "var(--ds-text)" }}>{shown}</span>
            {of ? <span style={{ fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}>{of}</span> : null}
          </span>
          {trend && trend.length > 0 ? <Sparkline values={trend} label={trendLabel} valueFormat={valueFormat} /> : null}
        </dd>
        {delta || noteText ? (
          <dd style={{ margin: 0, display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--ds-space-2)", fontSize: "var(--ds-text-xs)" }}>
            {delta ? (
              <span style={{ display: "inline-flex", alignItems: "center", gap: "var(--ds-space-1)", color: "var(--ds-text-secondary)" }}>
                <span style={{ display: "inline-flex", alignItems: "center", gap: "var(--ds-space-1)", color: deltaColor(delta.good), fontWeight: "var(--ds-weight-medium)" }}>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d={DELTA_ICON[delta.direction || "flat"]} />
                  </svg>
                  <span className="ds-visually-hidden">{DELTA_WORD[delta.direction || "flat"]} </span>
                  {delta.value}
                </span>
                {delta.period ? <span>{delta.period}</span> : null}
              </span>
            ) : null}
            {noteText ? <SampleNote>{noteText}</SampleNote> : null}
          </dd>
        ) : null}
      </dl>
    </Card>
  );
}
