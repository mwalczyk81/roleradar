**Skeleton** — placeholder shapes shown while content loads.

```jsx
<Skeleton variant="table" rows={5} columns={4} />
<Skeleton variant="card" />
<Skeleton variant="text" rows={3} />
<Skeleton variant="block" width="12rem" height="8rem" />
```

The group is announced once as "Loading" (change it with `label`) and the shapes are hidden from assistive technology. The pulse changes opacity only and stops under `prefers-reduced-motion`. Size the skeleton like the content it stands in for so the page does not jump when the content arrives.

Props: `variant`, `rows`, `columns`, `width`, `height`, `label`.
