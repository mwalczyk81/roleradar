**ToastRegion** — fixed stack in the bottom-right corner (full width on narrow screens) that holds Toasts.

```jsx
<ToastRegion>
  {toasts.map((t) => <Toast key={t.id} {...t} onDismiss={() => remove(t.id)} />)}
</ToastRegion>
```

Render it once near the root and add or remove Toast children as notifications come and go, newest last. The region is a labelled landmark (`label`, default "Notifications") and lets clicks pass through everywhere except the toasts themselves.

Props: `label`, `children`.
