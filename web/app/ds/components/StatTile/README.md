**StatTile** — one headline number with its context, built on Card. Use a row of them for headline figures; use a chart for anything with more than one value.

```jsx
<StatTile label="Applied" value="22%" of="4 of 18 scored" denominator={18} />
<StatTile label="Postings scored" value={1284} delta={{ value: "+12%", direction: "up", good: true, period: "vs previous 30 days" }} trend={weekly} trendLabel="Scored per week" />
```

`value` leads, in the font's proportional digits (tabular figures belong in columns). A numeric value is formatted 1,284 / 12.9K / 4.2M; pass a string for a percentage or anything else. `of` keeps the count next to a percentage. A `denominator` under `smallSampleBelow` (default 10) adds a visible "Small sample" note; `note` replaces its wording. `delta` colours the change by whether it is good (`good: true` green, `false` red, omitted neutral), and the arrow and signed text say which way it went, so colour never carries it alone. `trend` draws a Sparkline.

Semantically the tile is a description list: the label is the term, the figures are its details.

Props: `label`, `value`, `of`, `delta`, `trend`, `trendLabel`, `note`, `denominator`, `smallSampleBelow`, `valueFormat`, plus Card's.
