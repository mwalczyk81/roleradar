**Modal** — centered dialog over a dimmed backdrop; closes on Esc, backdrop click, or the × button. Pair the footer with **Button**s.

```jsx
const [open, setOpen] = React.useState(false);
<Modal
  open={open}
  onClose={() => setOpen(false)}
  title="Delete project?"
  footer={<>
    <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
    <Button variant="danger" onClick={confirm}>Delete</Button>
  </>}
>
  This can't be undone.
</Modal>
```

Props: `open`, `onClose`, `title`, `ariaLabel`, `footer`, `width` (default 480).

Focus moves into the dialog on open, Tab stays inside, focus returns to the opener on close, and page scroll is locked while open. `title` labels the dialog; pass `ariaLabel` when there is no visible title.
