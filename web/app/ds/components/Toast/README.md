**Toast** — a short notification, with an optional action.

```jsx
<ToastRegion>
  <Toast variant="warning" title="Posting changed elsewhere" message="Reload to see the latest state."
         action={{ label: "Reload", onClick: reload }} onDismiss={close} />
</ToastRegion>
```

Warning and danger toasts are announced assertively (`role="alert"`); info and success politely (`role="status"`). A toast dismisses itself after `duration` ms (5000 by default) unless it has an `action`, `duration` is 0, or there is no `onDismiss`. The timer pauses while the pointer is over the toast or focus is inside it. Never put the only copy of an error in a toast that disappears: use an Alert for messages the person must act on, and a toast with an action for the rest.

Props: `variant`, `title`, `message`, `action`, `onDismiss`, `duration`.
