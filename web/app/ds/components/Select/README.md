**Select** — native `<select>` styled to match inputs, with a chevron.

```jsx
<label className="field">Role</label>
<Select defaultValue="member">
  <option value="admin">Admin</option>
  <option value="member">Member</option>
</Select>
```

Props: `size`, `invalid`, `disabled`. Children are `<option>` elements. Passes through native `<select>` attributes.
