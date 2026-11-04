**Sparkline** — a word-sized trend line: a 2px de-emphasis gray line with the latest point as an 8px accent dot ringed in the surface colour.

```jsx
<Sparkline values={[3, 5, 4, 8, 6, 9, 12]} label="Applications per week" />
```

It has no axes, no hover layer and no table of its own, so put the numbers in text beside it (StatTile does) and use a ColumnChart where readers need to read values. The accessible name is the optional `label` followed by a range summary ("7 points, from 3 to 12, low 3, high 12"). A flat series draws a level line; one point draws just the dot; no points draw nothing. Set `surface` when it sits on something other than `--ds-surface`.

Props: `values`, `width` (96), `height` (28), `label`, `valueFormat`, `surface`.
