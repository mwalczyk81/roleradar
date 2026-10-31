**EmptyState** — placeholder for an empty list, a search with no results, a missing page or an unavailable service.

```jsx
<EmptyState
  icon={<SearchIcon />}
  title="No postings match"
  description="Try removing a filter or searching for something else."
  action={<Button onClick={clearFilters}>Clear filters</Button>}
/>
```

The title is a heading, level 2 by default; set `headingLevel` so it fits the page outline. Say what is empty and what to do next, in plain sentences. Use `role="status"` when the state replaces content after an update so it is announced. `action` and `secondaryAction` are nodes, usually Buttons.

Props: `icon`, `title`, `description`, `action`, `secondaryAction`, `headingLevel`.
