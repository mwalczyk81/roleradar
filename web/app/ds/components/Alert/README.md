**Alert** — inline banner for a status message; variant sets color + icon.

```jsx
<Alert variant="success">Saved successfully.</Alert>
<Alert variant="warning" title="Heads up">Double-check this field.</Alert>
<Alert variant="danger" onClose={dismiss}>Something went wrong.</Alert>
```

Variants: `info` · `success` · `warning` · `danger`. Props: `title`, `onClose`. Warning and danger render `role="alert"` (announced immediately); info and success render `role="status"`.
