**Tooltip** — a short hint for one control, shown on hover and keyboard focus.

```jsx
<Tooltip content="Opens the posting in a new tab">
  <Button variant="ghost" aria-label="Open posting"><ExternalIcon /></Button>
</Tooltip>
```

Wrap exactly one element. The tooltip shows after a short hover (`delay`) or at once on focus, hides on Escape, and stays open while the pointer is over the hint. The element gets `aria-describedby`, so assistive technology reads the hint as its description. A tooltip adds detail; the control still needs its own accessible name, and nothing essential should live only in a tooltip.

Props: `content`, `children`, `placement` (`top` or `bottom`), `delay`.
