# Contract: theme module (`web/app/lib/theme.ts`)

One implementation of theme handling. `root.tsx` and `useTheme` are consumers; no other file may parse or write `roleradar_theme`.

## Exports

| Name | Behaviour |
| --- | --- |
| `THEME_COOKIE` | `"roleradar_theme"` |
| `type Theme` | `"light"` or `"dark"` |
| `parseThemeCookie(cookieHeader)` | Takes `string`, `null` or `undefined`. Returns the value only when it is exactly `light` or `dark`; anything else is `undefined`. Used by the root loader (server) and the hook (client). |
| `serializeThemeCookie(theme, isHttps)` | `roleradar_theme=<theme>; Path=/; SameSite=Lax; Max-Age=31536000` plus `; Secure` when `isHttps`. Byte-identical to today's string. |
| `systemTheme(mql)` | `dark` when the query matches, else `light`. |
| `applyTheme(doc, theme)` | Sets `data-theme` on `documentElement`. |
| `THEME_BOOTSTRAP_SCRIPT` | The blocking inline script for `<head>`: reads the cookie with the same pattern, falls back to `matchMedia`, sets `data-theme`, swallows errors. Built from `THEME_COOKIE` so the name exists once. Rendered with the request nonce. |

## Hook (`useTheme(initialTheme?: Theme)`)

- Signature and return `{ theme, toggleTheme }` unchanged.
- First render: on the server, `initialTheme` (the cookie the loader read) or light. In the browser, the cookie, then the attribute the head script set, then a dark system, then `initialTheme`, then light. An effect then follows the system setting while no cookie exists. This is the order the hook already used; the root differs only in that it renders `data-theme` on the server from the cookie alone.
- With no cookie it follows `prefers-color-scheme` live; once a cookie exists it stops following.
- `toggleTheme` flips, writes the attribute and writes the cookie via `serializeThemeCookie`.

## Root

- Loader returns `{ theme: parseThemeCookie(Cookie header), nonce }`.
- `<html lang="en" data-theme={theme}>` is rendered by the server whenever the cookie exists.
- `root.tsx` has no theme state, no theme effect and no regex of its own.

## Invariants (tested)

1. The pattern for the cookie appears in exactly one source module (plus the string that module builds for the script).
2. Server HTML for a request with `roleradar_theme=dark` has `data-theme="dark"` before any script runs (JavaScript-disabled browser context).
3. An invalid cookie value is ignored.
4. Toggling twice returns to the starting theme and leaves one cookie.
5. The kit has no `prefers-color-scheme` rule, so a no-cookie, no-script page is light; this is unchanged.
