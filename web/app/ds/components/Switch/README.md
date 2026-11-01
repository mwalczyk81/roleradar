**Switch** — instant on/off toggle for settings that apply immediately (no Save step). For form values that need submitting, use **Checkbox** instead.

```jsx
<Switch label="Enable notifications" defaultChecked />
<Switch checked={dark} onChange={e => setDark(e.target.checked)} label="Dark mode" />
```

Props: `label`, `checked`, `defaultChecked`, `disabled`, `onChange`.
