**Tabs** — underline tab bar for switching sibling views; controlled or uncontrolled.

```jsx
<Tabs
  defaultValue="profile"
  items={[
    { value: "profile", label: "Profile" },
    { value: "billing", label: "Billing" },
    { value: "team", label: "Team" },
  ]}
  onChange={setTab}
/>
```

Active item: `primary` underline + `text`. Inactive: `text-muted`. Props: `items`, `value`, `defaultValue`, `onChange`, `idBase`.

Keyboard: Left/Right move and select, Home/End jump; only the active tab is in the tab order. Render your panel with `role="tabpanel"`, `id={`${idBase}-panel-${value}`}` and `aria-labelledby={`${idBase}-tab-${value}`}`; pass the same `idBase` to `Tabs`.
