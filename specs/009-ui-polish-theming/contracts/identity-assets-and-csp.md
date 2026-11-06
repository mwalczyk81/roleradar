# Contract: Identity assets and CSP

## Served files (Identity origin)

| URL | Source | In git |
| --- | --- | --- |
| `/ds/tokens.css` | copy of `web/app/ds/tokens.css` | no (generated) |
| `/ds/components/bundle.css` | copy of `web/app/ds/components/bundle.css` | no (generated) |
| `/ds/fonts/*.woff2` | copy of `web/app/ds/fonts/*.woff2` (all six; `tokens.css` references them relatively) | no (generated) |
| `/css/identity.css` | `src/RoleRadar.Identity/wwwroot/css/identity.css` (layout for the sign-in card, tokens only, no raw hex or px) | yes |
| `/js/theme.js` | `src/RoleRadar.Identity/wwwroot/js/theme.js` (sets `data-theme` from `prefers-color-scheme`, live; no cookie, no storage) | yes |

Copy happens in an MSBuild target in `RoleRadar.Identity.csproj` that runs before build and publish. It fails the build with a clear message if `web/app/ds/tokens.css` is missing. `Dockerfile.identity` copies `web/app/ds/` into the build stage before restore. `.dockerignore` adds `!src/RoleRadar.Identity/wwwroot/**`; `web/**` is already allowed. `.gitignore` adds `src/RoleRadar.Identity/wwwroot/ds/`.

## Pages

- `Login.cshtml` and `Logout.cshtml`: no `<style>`, no `style=`, no inline script. They link the four assets above in `<head>`, `theme.js` first (blocking) so the theme is set before paint.
- Markup, form fields, names, anti-forgery token, hidden inputs and flow are unchanged. Classes are renamed only where the old ones carried the old palette.
- Body face is Plex Sans, headings Plex Serif, colours and radii from `--ds-*` tokens, both themes.

## Headers (responses under `/Account`)

```text
Content-Security-Policy: default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self' <origins>
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

`<origins>` is the distinct set of origins of every registered client's redirect URIs and post-logout redirect URIs, taken from the same configuration `IdentityServerConfig.GetClients` reads. Reason: Chromium enforces `form-action` on the redirect chain that follows a form post, and the sign-in and sign-out posts end at the web app. The middleware never touches `/connect/*` or `/health`.

## Must not change

Login and TOTP behaviour, redirects, cookies, error messages, lockout and rate limiting, the existing login end-to-end test and the manual PKCE walk in the 005a quickstart (section 5a).

## Tests

| Check | Where |
| --- | --- |
| Header policy string and origin derivation | Unit |
| Login and Logout HTML contain no `<style>` and no `style=` | Unit or Contract (in-process host) |
| `.dockerignore` allows every `.css`/`.js` under `src/` and the two Identity-owned files by name | Unit (`DockerIgnoreCoverageTests`) |
| Assets and fonts served; no inline style; headers present | Integration (`IdentityImageLoginPageTests`, Docker) |
| Both system schemes: computed font family and primary colour, axe, zero CSP violations | Playwright against the image or in-process host |
