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

function deltaTone(good) {
  if (good === true) return "good";
  if (good === false) return "bad";
  return "neutral";
}

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * StatTile — one headline number with its context. `label` names it, `value` leads, `of` keeps
 * the count next to a percentage ("4 of 18"), `delta` compares with a named period, `trend`
 * draws a Sparkline. A denominator under `smallSampleBelow` (default 10) adds a visible
 * "Small sample" note, or pass `note` for your own wording. The delta's colour says whether the
 * change is good (`good`), the arrow and text say which way it went. Large figures use the
 * font's proportional digits, not tabular ones.
 */
export function StatTile({ label, value, of, delta, trend, trendLabel, note, denominator, smallSampleBelow = SMALL_SAMPLE, valueFormat = formatCount, className, ...rest }) {
  const noteText = note !== undefined ? note : denominator !== undefined && denominator < smallSampleBelow ? "Small sample" : null;
  const shown = typeof value === "number" ? valueFormat(value) : value;
  return (
    <Card {...rest} padding="md" className={cx("ds-stat", className)}>
      <dl className="ds-stat-list">
        <dt className="ds-stat-label">{label}</dt>
        <dd className="ds-stat-main">
          <span className="ds-stat-figure">
            <span className="ds-stat-value">{shown}</span>
            {of ? <span className="ds-stat-of">{of}</span> : null}
          </span>
          {trend && trend.length > 0 ? <Sparkline values={trend} label={trendLabel} valueFormat={valueFormat} /> : null}
        </dd>
        {delta || noteText ? (
          <dd className="ds-stat-meta">
            {delta ? (
              <span className="ds-stat-delta">
                <span className="ds-stat-delta-change" data-tone={deltaTone(delta.good)}>
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
