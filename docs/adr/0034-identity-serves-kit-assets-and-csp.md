# ADR 0034: Identity Serves the Kit Assets and a CSP for Its Sign-In Pages

- **Status**: Accepted
- **Date**: 2026-11-06
- **Feature**: `009-ui-polish-theming`

## Context and Problem Statement

The Identity host renders two pages, `/Account/Login` and `/Account/Logout`. Each carried its own inline `<style>` block (a generic dark palette and system fonts, plus one `style=` attribute) and no Content-Security-Policy. The pages looked unrelated to the web app, ignored the system light or dark setting, and could not be given a strict policy while the styles were inline. The `.dockerignore` is deny-by-default, and it has already shipped an Identity image without its compiled Razor page; a stylesheet or script dropped the same way would build, report healthy and serve an unstyled page.

## Decision Outcome

- **Build-time copy of the kit, not a second copy in git.** An MSBuild target in `RoleRadar.Identity.csproj` runs before build and publish and copies `web/app/ds/tokens.css`, `web/app/ds/components/bundle.css` and `web/app/ds/fonts/*.woff2` into `wwwroot/ds/`, keeping the relative layout so `tokens.css` finds its fonts. It fails with a clear message when `web/app/ds/tokens.css` is missing. `wwwroot/ds/` is git-ignored. `deploy/Dockerfile.identity` copies `web/app/ds/` into the build stage before restore.
- **Two committed Identity-owned files.** `wwwroot/css/identity.css` (layout of the sign-in card, kit tokens only, no raw colours or pixel values) and `wwwroot/js/theme.js` (a blocking script that sets `data-theme` from `prefers-color-scheme` and follows live changes; no cookie, no storage).
- **System colour scheme only.** The Identity origin cannot read the web app's `roleradar_theme` cookie, and a separate preference there would be a second source of truth. The pages follow the operating system setting.
- **Pages without inline style or script.** `Login.cshtml` and `Logout.cshtml` link the four assets with `theme.js` first. Form fields, names, hidden inputs, the anti-forgery token, handlers and the page models are unchanged.
- **A CSP on `/Account` only**, with `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`:

  `default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self' <origins>`

  `<origins>` is the distinct set of http(s) origins of every registered client's redirect and post-logout URIs, taken from the same clients the host registers (`IdentityServerConfig.GetClients`), never hard-coded. Chromium enforces `form-action` on the redirect chain that follows a form post, and the sign-in and sign-out posts end at the web app, so `'self'` alone would block a legitimate sign-in. Adding a client or changing its redirect configuration changes the policy with it. `/connect/*`, the discovery document and `/health` get no header from this middleware.
- **The ignore-file defect cannot recur silently.** `.dockerignore` allows `src/RoleRadar.Identity/wwwroot/**`. `DockerIgnoreCoverageTests` covers `.css` and `.js` under `src/` and the two Identity-owned files by name, and `IdentityImageLoginPageTests` fetches the stylesheet, the font, the theme script and the kit CSS from a running container and fails on a 404.

### Consequences

- Good: one design system, no drift, no second committed copy; both apps share tokens, fonts and the two themes.
- Good: the sign-in pages are accessible in both schemes (checked by contrast and axe in Playwright against the real image) and run under a strict policy.
- Bad: the Identity build now depends on `web/app/ds` being present, so it must be built from a full checkout, and a change to the kit invalidates the Identity restore layer in Docker.
- Bad: the policy is only as accurate as the registered redirect URIs. A deployment that redirects to an origin the configuration does not list is also rejected by IdentityServer itself, so the two fail together rather than silently.
- Bad: the in-process contract host cannot serve `wwwroot` (its content root is the test output folder), so asset serving is checked against the built image only.

### Alternatives considered

- A second committed copy of the kit CSS and fonts under `src/RoleRadar.Identity`: rejected, it drifts from `web/app/ds`.
- Fetching the assets cross-origin from the web app: rejected, it needs `style-src` and `font-src` entries for another origin, couples sign-in availability to the web app and weakens the policy.
- A CDN: rejected, it adds a third party to the sign-in page and an origin to every policy.
- A hard-coded `form-action` origin: rejected, it breaks as soon as the web app's origin changes, which is the failure the derived rule prevents.
- A separate theme preference on the Identity origin: rejected, see "System colour scheme only".
