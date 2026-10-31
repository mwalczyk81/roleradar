**Toolbar** — search box, filter controls and removable filter chips for the area above a table.

```jsx
<Toolbar
  searchValue={q}
  onSearchChange={setQ}
  filters={[{ id: "disp", label: "Disposition", value: "Lead", onRemove: () => setDisp(null) }]}
  onClearAll={clearFilters}
  actions={<Button>New posting</Button>}
>
  <Select aria-label="Disposition" value={disp} onChange={...}>...</Select>
</Toolbar>
```

`onSearchChange` fires after typing pauses (`debounceMs`, default 300), immediately on Enter (which also calls `onSearchSubmit`), and on Escape or the clear button. If the parent changes `searchValue` from outside, the box follows.

Each chip is a button: click it, or press Backspace or Delete while it has focus, to remove the filter. Focus moves to the next chip, or the search box when none are left. `onClearAll` adds a "Clear all" button once there are two or more filters. The container is a `search` landmark and the box has a visually hidden label (`searchLabel`).

Props: `searchValue`, `defaultSearchValue`, `onSearchChange`, `onSearchSubmit`, `searchPlaceholder`, `searchLabel`, `debounceMs`, `filters`, `onClearAll`, `actions`, `children`.
