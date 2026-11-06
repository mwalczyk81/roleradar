**Card** — the default surface container: surface bg, 1px border, `shadow-sm`, radius `lg`, `space-6` padding.

```jsx
<Card>
  <h3>Card title</h3>
  <p>Body content.</p>
</Card>
<Card padded={false}><table>…</table></Card>
<Card interactive onClick={open}>Clickable card</Card>
<Card padding="md">Tighter padding (space-5)</Card>
```

Props: `padded` (default true), `padding` (`none`, `sm` = space-4, `md` = space-5, `lg` = space-6, the default; wins over `padded`), `interactive` (hover elevation + pointer).
