**Checkbox** — boolean toggle; works controlled or uncontrolled.

```jsx
<Checkbox label="Email me updates" defaultChecked />
<Checkbox label="Accept terms" checked={agreed} onChange={e => setAgreed(e.target.checked)} />
```

Props: `label`, `checked`, `defaultChecked`, `disabled`, `onChange`.
