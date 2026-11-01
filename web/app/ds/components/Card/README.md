**Card** — the default surface container: surface bg, 1px border, `shadow-sm`, radius `lg`, `space-6` padding.

```jsx
<Card>
  <h3>Card title</h3>
  <p>Body content.</p>
</Card>
<Card padded={false}><table>…</table></Card>
<Card interactive onClick={open}>Clickable card</Card>
```

Props: `padded` (default true), `interactive` (hover elevation + pointer).
