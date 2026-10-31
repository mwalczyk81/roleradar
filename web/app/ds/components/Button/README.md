**Button** — the primary tap/click target; use one `primary` per view, `secondary`/`ghost` for the rest, `danger` for destructive actions.

```jsx
<Button variant="primary" onClick={save}>Save changes</Button>
<Button variant="secondary">Cancel</Button>
<Button variant="ghost" size="sm">Skip</Button>
<Button variant="danger" leadingIcon={<TrashIcon />}>Delete</Button>
```

Variants: `primary` · `secondary` · `ghost` · `danger`. Sizes: `sm` (32px) · `md` (40px, default) · `lg` (48px). Props: `fullWidth`, `disabled`, `leadingIcon`, `trailingIcon`. Passes through native `<button>` attributes (`onClick`, `type`, etc.).
