**Input** — single-line text field; always give it a `<label>` or `aria-label`.

```jsx
<label className="field">Email</label>
<Input type="email" placeholder="you@example.com" />
<Input invalid leadingIcon={<SearchIcon />} placeholder="Search…" />
```

Props: `size` (`sm`/`md`/`lg`), `invalid`, `leadingIcon`, `disabled`. Passes through native `<input>` attributes.
