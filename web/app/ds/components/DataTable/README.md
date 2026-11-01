**DataTable** — sticky-header table for lists of records, with page-local sorting, numeric columns and paging.

```jsx
<DataTable
  caption="Postings"
  columns={[
    { key: "title", header: "Role", sortable: true, render: (r) => <a href={`/postings/${r.id}`}>{r.title}</a> },
    { key: "status", header: "Status", render: (r) => <Badge tone="primary">{r.status}</Badge> },
    { key: "score", header: "Score", numeric: true, sortable: true },
  ]}
  data={rows}
  pagination={{ total: 232, limit: 50, offset: 0, onPageChange: setOffset }}
  onRowClick={(r) => navigate(`/postings/${r.id}`)}
/>
```

Sorting reorders only the rows it is given and the footer says so ("Sorting applies to the rows on this page only"). If the API sorts, sort upstream and leave `sortable` off. Empty values sort last in both directions and show an em dash.

Put numbers, scores and money in `numeric` columns: monospace, right-aligned, tabular figures. Headers are typed in sentence case and shown uppercase.

Give the table a `caption` (visually hidden) or `ariaLabel`. With `onRowClick`, rows are focusable and respond to Enter and Space; links and buttons inside a row keep their own behaviour, so put the real navigation in a link and treat the row click as a convenience. `isLoading` shows pulse rows and marks the table `aria-busy`; `emptyMessage` and `emptyAction` fill the no-results state. For a scrolling body set `maxHeight`; the header stays put and the scroll area is keyboard-focusable.

Props: `columns`, `data`, `keyField`, `caption`, `ariaLabel`, `onRowClick`, `isLoading`, `loadingRows`, `emptyMessage`, `emptyAction`, `sortColumn`, `defaultSortColumn`, `sortDirection`, `defaultSortDirection`, `onSortChange`, `pagination`, `stickyHeader`, `maxHeight`.
