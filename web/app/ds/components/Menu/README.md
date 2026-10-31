**Menu** — dropdown menu opened from a button, with full keyboard support.

```jsx
<Menu
  label="Posting actions"
  trigger="Actions"
  align="end"
  items={[
    { label: "Open", href: "/postings/42" },
    { label: "Mark as screened out", onClick: screenOut },
    { separator: true },
    { label: "Archive", danger: true, onClick: archive },
  ]}
/>
```

The button has `aria-haspopup="menu"` and `aria-expanded`. Enter, Space or ArrowDown opens the menu on the first item; ArrowUp opens it on the last. Inside, Up and Down move (wrapping), Home and End jump, a letter jumps to the next item that starts with it, Escape closes and returns focus to the button, and Tab closes. Clicking outside closes. Disabled items are skipped. Selecting an item runs its handler, closes the menu and returns focus to the button.

Items with an `href` render as links; pass `linkComponent` (a router Link) for client-side navigation. For an icon-only trigger, pass the icon as `trigger` and always set `label`. `variant="ghost"` drops the border.

Props: `trigger`, `label`, `items`, `align`, `variant`, `linkComponent`.
