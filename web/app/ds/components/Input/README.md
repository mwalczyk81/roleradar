**Input** — single-line text field; always give it a `<label>` or `aria-label`.

```jsx
<label className="field">Email</label>
<Input type="email" placeholder="you@example.com" />
<Input invalid leadingIcon={<SearchIcon />} placeholder="Search…" />
<Input width="sm" aria-label="Min score" placeholder="Min score" />
```

Props: `size` (`sm`/`md`/`lg`), `invalid`, `leadingIcon`, `width` (`xs` 5rem, `sm` 7rem, `md` 12rem; omit to fill the container), `disabled`. Passes through native `<input>` attributes.
