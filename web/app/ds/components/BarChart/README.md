**BarChart** — horizontal bars for counts across categories, with each value at the bar tip. A `<figure>` with a caption, a hover and focus readout, and a "Table view" button that swaps the plot for a native table of the same values.

```jsx
<BarChart
  title="Search funnel"
  subtitle="Last 90 days"
  funnel
  data={[
    { label: "Leads", value: 120 },
    { label: "Scored", value: 96 },
    { label: "Applied", value: 31 },
    { label: "Interviews", value: 9 },
    { label: "Offers", value: 2 },
  ]}
/>
<BarChart title="Postings by source" data={sources.map((s) => ({ label: s.name, value: s.count, highlight: s.name === "Referral" }))} />
```

One colour: the accent for the bars, or a gray for every bar but the `highlight`ed one. Bars are 20px thick with a 4px rounded tip and a square base. `funnel` adds "% of previous stage" under every stage name after the first, and any stage whose previous count (or `denominator`) is under `smallSampleBelow` (default 10) shows "Small sample". Counts always sit next to percentages, never replaced by them.

Keyboard: the chart is one tab stop; arrow keys, Home and End move between bars, and each bar shows its readout on focus as on hover. There is no legend because there is one series. While `isLoading` the previous render stays, dimmed.

Props: `title`, `subtitle`, `data`, `funnel`, `valueFormat`, `labelHeader`, `valueHeader`, `smallSampleBelow`, `framed`, `isLoading`, `emptyMessage`.
