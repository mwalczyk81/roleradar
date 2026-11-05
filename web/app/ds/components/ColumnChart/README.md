**ColumnChart** — vertical columns for counts over time buckets, such as applications per week. A `<figure>` with a caption, a hover and focus readout, and a "Table view" button.

```jsx
<ColumnChart
  title="Applications per week"
  subtitle="Last 12 weeks"
  data={weeks.map((w) => ({ label: w.short, fullLabel: `Week of ${w.long}`, value: w.count }))}
/>
```

Columns are capped at 24px wide with a 4px rounded top and a square base on one baseline. The y axis rounds up to clean numbers and shortens from 1,000 (0 / 1K / 2K). Values sit on the column caps while they fit (36px per column); below that only the peak and the latest keep one, and the x-axis labels thin out so they never collide. Columns never get narrower than 20px; past that the plot scrolls sideways. Hover or focus a column for its readout (`fullLabel` is the long form); the chart is one tab stop and arrow keys, Home and End move between columns.

An empty period still gets a column slot and a readout of 0. While `isLoading` the previous render stays, dimmed.

Props: `title`, `subtitle`, `data`, `height`, `valueFormat`, `labelHeader`, `valueHeader`, `framed`, `isLoading`, `emptyMessage`.
