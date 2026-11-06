# Research: UI Polish and Theming

Facts were read from the code at `317f1fc` on branch `009-ui-polish-theming`. No command was run against the developer's stack. Counts come from `grep` over the working tree; the plan's Phase 1 re-counts from the ratchet test.

## R1. How many inline styles, and whose?

- **Decision**: App ceiling is **203** `style={{` props in 17 files under `web/app` (excluding `web/app/ds`). Kit emits **150** `style={` props in **27** files under `web/app/ds/components/src`.
- **Evidence**: per-file app counts are in spec US1. Kit per file: `DataTable` 17, `chartParts` 15, `ColumnChart` 15, `Skeleton` 11, `Toolbar` 11, `Modal` 10, `StatTile` 10, `BarChart` 9, `Toast` 7, `Alert` 6, `Menu` 5, `EmptyState` 5, `Switch` 4, `Radio` 4, `Tooltip` 3, `Select` 3, `Input` 3, `Checkbox` 3, `Tabs` 2, `AppShell` 2, `Badge` 2, `Avatar` 2, `Textarea` 1, `Button` 1, `ToastRegion` 1, `Card` 1, `Sparkline` 1.
- **App values are all static.** No value is computed from data. The spec's earlier "22 kit components" was wrong; it is 27 files (corrected in the spec).
- **Rationale**: the ratchet needs one number to start from and the final phase needs an exact list of kit emitters.

## R2. Why the kit blocks the CSP flip

- **Decision**: The CSP flip waits for K1. Once `style-src-attr 'unsafe-inline'` is gone, the directive falls back to `style-src 'self' 'nonce-...'`, which does not allow attributes, so the browser drops every server-rendered `style` attribute. React setting `element.style` on the client goes through the CSS object model, which CSP allows, so the UI would look right only after hydration and be unstyled before it. That is the same "paints empty until hydration" failure the owner ruled out for charts.
- **Alternatives rejected**: CSP hashes for each attribute (`'unsafe-hashes'`): hundreds of distinct values, breaks on any kit bump. A nonce on style attributes: not supported by CSP. Stripping `style` in the server stream: edits behaviour, hides real breakage.

## R3. Theme logic today

- **Facts**: `root.tsx` keeps its own state, a `useEffect` with the `prefers-color-scheme` listener, an inline bootstrap script (nonce), and parses the `roleradar_theme` cookie in the loader. `use-theme.ts` repeats the cookie regex, the media-query effect and the attribute write, and adds `toggleTheme` (writes the cookie with `Path=/; SameSite=Lax; Max-Age=31536000` and `; Secure` on HTTPS). The regex `/roleradar_theme=(light|dark)/` appears four times (loader, root state, root effect, script, hook effect).
- **Kit fact**: `tokens.css` selects light with `:root, [data-theme="light"]` and dark with `[data-theme="dark"]` only. There is no `@media (prefers-color-scheme)` rule. So a page with no cookie and no script renders light, and "follow the system" always needs a script.
- **Decision**: one module `web/app/lib/theme.ts` holds the pure pieces: `THEME_COOKIE`, `parseThemeCookie(header)`, `serializeThemeCookie(theme, isHttps)`, `systemTheme(mql)`, `applyTheme(theme)`, and `THEME_BOOTSTRAP_SCRIPT` (a string built from the same constants). `use-theme.ts` stays the one hook (`useTheme(initialTheme?)`, same return shape `{ theme, toggleTheme }`) and uses the module. `root.tsx` calls `useTheme(serverTheme)` and renders the script from the module. The loader uses `parseThemeCookie`.
- **First paint**: server renders `data-theme` from the cookie when present. With no cookie, the blocking bootstrap script (in `<head>`, nonce) sets it from `matchMedia` before the body paints. The hook's first client render uses the same value the server rendered (`initialTheme`), then reconciles in an effect, so there is no hydration mismatch and no second flip.
- **Alternatives rejected**: moving to `localStorage` (changes storage behaviour); dropping the script and relying on CSS (the kit cannot do it, see above).

## R4. Ratchet for new inline styles

- **Decision**: a Vitest architecture test `web/tests/architecture/inline-style-ratchet.test.ts` counts `style={` in `web/app/**/*.tsx` excluding `ds/`, per file, and compares with `inline-style-ceiling.json` (per-file ceilings, initial total 203). It fails when any file exceeds its ceiling (no new styles) and when a ceiling is higher than the real count (forces the owner to lower it as each phase lands). At zero it is replaced by a lint rule (`no-restricted-syntax` on JSX attribute `style`) in the same phase that reaches zero.
- **Why a test and not lint first**: a lint rule with 203 existing violations needs suppressions; the per-file ceiling is a single reviewable number that only goes down.
- **Counting rule**: the `style=` JSX attribute (object or identifier), not the word "style". `<style>` elements in JSX would be counted separately and are expected to stay at zero (none exist).

## R5. Baseline screenshots without new packages

- **Decision**: use Playwright's built-in `toHaveScreenshot` in a new spec `visual-baseline.spec.ts`, enabled only when `ROLERADAR_VISUAL_DIR` is set (skipped otherwise, so the normal suite and the Linux gate never run it). `snapshotPathTemplate` points at that directory, which defaults to `%TEMP%\roleradar-visual\<label>\` outside the repository; nothing is committed. Baseline run uses `--update-snapshots` on the Phase 1 code (identical to `main` for app markup); each later run compares.
- **Why built-in**: AGENTS and the constitution require pinned packages and an ADR for decisions; a pixel-diff dependency adds both. Playwright already ships the comparison.
- **Limits**: screenshots are platform-specific, so they are a Windows local check, not a CI check. The assertions for correctness remain computed-style, contrast and layout tests (spec FR-009).
- **Routes covered** (every route in `web/app/routes.ts`): `/` (redirect target), `/postings`, `/postings/:id` (lead, applied, with the transition dialog open), `/companies`, `/companies/:id`, `/applications`, `/applications/:id` (with the append-event dialog open), `/criteria`, `/notes`, `/notes/:id`, `/search-runs`, `/insights` (90d and all), `/not-authorized`, `/service-unavailable`, `/auth/logout` result, `/auth/callback` error state, and the root `ErrorBoundary`. Each in light and dark, 1280 by 800, reduced motion, fixed clock.
- **Harness reuse**: the stubbed API, fictional fixtures and session seeding live inside `csp-style-attribute.spec.ts` (about 530 lines in `beforeAll`). They move to `web/tests/e2e/support/app-harness.ts` (behaviour-preserving extraction) so the baseline spec and the CSP spec share them. This edits an existing test file; it is listed in the plan.

## R6. CSP violation guard

- **Decision**: `web/tests/e2e/support/csp-guard.ts` exports a `test` built with `test.extend` and an auto fixture that attaches a `console` and `securitypolicyviolation` listener to every page and fails the test in teardown if any violation was seen. Specs import `test` from it instead of `@playwright/test`. Specs that already track violations by hand keep their own checks (no behaviour removed).
- **Why**: today six specs each roll their own `trackCsp`; a violation on an untracked route passes silently.
- **Window**: with `style-src-attr 'unsafe-inline'` still on, the listener catches script, font, image and style-element violations. After the flip (Phase 6) it also catches style attributes. Phase 1 adds a **style-attribute census** instead of a Report-Only policy (a violation event does not name the element, so it cannot tell app from kit): the visual spec loads each route's server-rendered HTML with JavaScript disabled and lists every element carrying a `style` attribute as `tag.class`. Each conversion slice must lower the count by the number of app styles it removed that render on the route, and the census at the end of Phase 3 (app styles are 0, enforced by the ratchet) is by definition the kit-only list that Phase 6 reports.

## R7. Identity: how kit CSS and fonts reach the image

- **Facts**: Identity is `Microsoft.NET.Sdk.Web` with Razor Pages only: no `wwwroot`, no `UseStaticFiles`, no CSP, two pages with an embedded `<style>` block and one `style="margin: 0;"` attribute. `.dockerignore` is deny-by-default and allows `src/**/*.cs`, `*.cshtml`, `*.csproj`, `packages.lock.json` and all of `web/**`. It does **not** allow `.css`, `.js` or `.woff2` under `src/`. `DockerIgnoreCoverageTests` only checks `.cs`, `.csproj`, `.cshtml`, `.razor`.
- **Decision (delivery)**: copy at build time, single source. An MSBuild target in `RoleRadar.Identity.csproj` copies `web/app/ds/tokens.css`, `web/app/ds/components/bundle.css` and `web/app/ds/fonts/*.woff2` into a git-ignored `src/RoleRadar.Identity/wwwroot/ds/` (same relative layout, so `tokens.css`'s `url("fonts/...")` still resolves) before build and publish. The Identity-owned files `wwwroot/css/identity.css` and `wwwroot/js/theme.js` are committed. `Dockerfile.identity` adds `COPY web/app/ds/ web/app/ds/` before restore/publish so the target finds its source. `.dockerignore` gets `!src/RoleRadar.Identity/wwwroot/**` (for the committed files) and the `web/**` allowance already covers the kit.
- **The earlier defect, repeated by design**: `.css` and `.js` are not on the allow-list, so without the new line the image would build and then serve an unstyled page. Guard: extend `DockerIgnoreCoverageTests` build-input extensions with `.css` and `.js`, add a pinpoint test for the two Identity-owned files, and extend `IdentityImageLoginPageTests` (Docker) to request `/ds/tokens.css`, `/css/identity.css`, one font and `/js/theme.js` from the running container and to assert the HTML has no `<style>` and no `style=`.
- **Alternatives rejected**: committing a second copy of the kit under Identity (drifts; breaks the copy-only rule). A shared static file served by the web host (Identity is a different origin and must not depend on the web app being up for its login page). A CDN (violates local-first).
- **Fonts**: the same six `woff2` files the web app uses (Plex Sans variable, Plex Serif 400/500/600, Plex Mono 400/500). Identity pages use Sans and Serif (heading) only, but the whole `fonts/` folder is copied so `tokens.css` has no dead URLs.

## R8. Identity CSP

- **Decision**: a small middleware adds, for responses under `/Account`, `Content-Security-Policy: default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self' <web origins>`, plus `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`.
- **form-action matters**: Chromium also applies `form-action` to the redirect chain after a form post. The login POST ends in redirects through `/connect/authorize/callback` and then to the web app's `signin-oidc` (and logout to the post-logout redirect). So `form-action` must list those web origins. They are derived from the configured clients' redirect and post-logout URIs (`IdentityServerConfig.GetClients`), not hard-coded, so the policy cannot drift from the registered clients.
- **Script**: `script-src 'self'` allows only `/js/theme.js`. No inline script anywhere.
- **Scope**: only Razor page responses under `/Account`. Duende's `/connect/*` endpoints and `/health` are untouched.

## R9. Identity theme without a cookie

- **Decision**: `wwwroot/js/theme.js` (about 5 lines, blocking, in `<head>`) sets `data-theme` from `matchMedia('(prefers-color-scheme: dark)')` and listens for changes. It reads no cookie and writes none (J8). Identity and web run on different hosts, so they could not share the cookie anyway.
- **Kit fact**: needed because tokens only react to `data-theme` (R3).

## R10. Search-run yield chart

- **Facts**: `insights.tsx` renders a local `Table view of search runs` toggle, a local table (`.insights-run-table`), and a kit `Sparkline` (320 by 56) for the run series. The weekly chart already uses kit `ColumnChart` with its own table view. `ColumnChartProps` has `title` (required), `subtitle`, `data: {label, value, fullLabel?}[]`, `height` (default 180), `framed` (default true), `labelHeader`, `valueHeader`, `emptyMessage`. There is no prop to hide labels or title, and the owner ruled out a quiet mode (K5 withdrawn).
- **Decision**: replace the local toggle, table and sparkline with a text line (latest run's new postings and the point count) and a kit `ColumnChart` with `framed={false}`, `height` lowered from 180 and `labelHeader="Run date"`, `valueHeader="New postings"`. The run rows already carry `date` and `newPostings` (`stats.runs.rows`), so the data maps one to one: `label` = short date, `fullLabel` = date. Final height is judged by the owner (J4).
- **Tests that change**: `insights-trends.test.tsx` (sparkline and run toggle assertions) and `insights-trends.spec.ts`; see the plan.

## R11. Where the polish items live

- **Role-title link** (`postings._index.tsx`): a `Link` with an inline `color: var(--ds-primary)`. Becomes `.role-title-link` in `app.css`: `color: var(--ds-text)`, hover and focus `var(--ds-bronze-...)` token chosen by contrast (the kit's `--ds-primary` is the bronze role; the exact semantic token is picked from `tokens.json` by computing the ratio in both themes during Phase 4 test-first, not guessed here).
- **Min Score placeholder**: the `Input` is the kit's. First measure the computed placeholder colour against its background in both themes (failing test first). If it fails AA, an app-level `::placeholder` rule on a class is **not** allowed to paper over a kit token; it would be K4. If the kit passes (the failure is only in how the filter control overrides it), a class fixes it.
- **Insights tile links**: `.insights-tile-links a` and `.insights-stage-links a` use `--ds-primary-subtle-text` and underline. Quieter = secondary text colour, underline on hover and focus only, plus the count itself keeps its link role. Judgement J1.
- **Equal tile heights**: `.insights-tiles` is a CSS grid whose items are flex columns. Heights differ because the "No data" tile lacks the note line. Fix with `grid-auto-rows: 1fr` and the tile as `height: 100%` inside the grid. Check: equal bounding-box heights per row.
- **"See all postings in this window"**: lives alone at the end of "How these are calculated". Moves beside the postings funnel (J2). Nav (window control) stays at the top (J3 default).
- **Markdown tables** (`app.css` `.markdown-body`): prose is `max-width: 72ch`; the table scroll region sits inside that column, so it is clipped at 72ch. Fix: the card content is the full-width container; prose children get the 72ch cap, the `.markdown-table-scroll` does not. Check: scroll region width equals card content width and exceeds the prose width.
- **Headings h4 to h6**: h4 is `--ds-text-lg`, h5 and h6 are `--ds-text-base`, all semibold display serif; h5 and h6 are identical and body text is Plex Sans base. Distinct = h4 stays larger, h5 uses sans semibold with letter-spacing, h6 small caps/uppercase small. Judgement J5; the check is that computed style differs from `<p>` at each level and h5 differs from h6.

**Phase 4 measurement (T065, 2026-11-06).** The kit `Input` sets no `::placeholder` rule, so Chromium paints its default `#757575`: 4.61:1 on the light surface (passes) and 3.29:1 on the dark surface (fails AA). The cause is the kit, so K4 stands (the kit needs a placeholder token used by `Input`, `Select` and `Textarea`). An app-side fix exists that needs no inline style and no wrapper, because `Input` passes `className` to the `<input>`: `.placeholder-muted::placeholder { color: var(--ds-text-muted) }` on the Min Score input only, 4.80:1 light and 6.01:1 dark. It is a stopgap and is removed when K4 is vendored.

## R12. Existing tests that assert markup or style being changed

| Test | Phase | Reason |
| --- | --- | --- |
| `web/tests/unit/root.test.tsx` | 2 | Asserts root's own theme state, effect and script text; those move to `theme.ts`. Loader, meta, links and ErrorBoundary assertions stay. |
| `web/tests/unit/use-theme.test.ts` | 2 | Hook now delegates to the module; assertions on exact cookie string and attribute stay, mocks may move. |
| `web/tests/e2e/csp-style-attribute.spec.ts` | 1 | Harness extraction to `support/app-harness.ts`; import of guarded `test`. No assertion changes. |
| Other e2e specs (`insights*.spec.ts`, `notes-markdown`, `posting-rejection-sync`, `postings-server-sort`, `legacy-api-retirement`) | 1 | One-line import swap to the guarded `test`. |
| `web/tests/routes/remaining-entities.component.test.tsx` (4 assertions on `style.whiteSpace`) | 3 | Inline `whiteSpace: nowrap` becomes a class; assertions become class-based. |
| `csp-style-attribute.spec.ts` EmptyState test (`/service-unavailable`, `/not-authorized`) | 3 and 6 | Page-level inline styles on those routes become classes (Phase 3). The assertion that the kit `EmptyState` has inline computed styles before hydration becomes "computed styles apply before hydration" via classes once K1 lands (Phase 6); until then it keeps passing against the kit's inline styles. |
| `web/tests/components/insights-trends.test.tsx`, `web/tests/e2e/insights-trends.spec.ts`, possibly `insights-page.test.tsx` | 4 | Sparkline and local run-table toggle replaced by `ColumnChart`. |
| `web/tests/components/brand.test.tsx` | none | Already asserts no `[style]`; stays unmodified. |
| `tests/RoleRadar.Tests.Unit/Containers/DockerIgnoreCoverageTests.cs` | 5 | Extensions list gains `.css` and `.js`; new pinpoint test. |
| `tests/RoleRadar.Tests.Unit/Identity/LoginModelRazorPageTests.cs` | 5 | Only if it asserts the old markup; checked first, edited only for that. |
| `tests/RoleRadar.Tests.Integration/Containers/IdentityImageLoginPageTests.cs` | 5 | Extended (assets served, no inline style); existing assertions unchanged. |
| `web/tests/unit/server.test.ts` | 6 | Asserts the CSP string; `style-src-attr` clause removed only when the flip happens. |

## R13. Things the plan could not confirm and how they are covered

- Contract-test host (`IdentityContractTestServer`) may not serve `wwwroot` from the project folder. Covered: Phase 5 task starts by checking; if it does not, the static-file test lives in the Docker integration test and the middleware test uses the in-process host for headers only.
- `playwright-config.test.ts` may assert config shape. Covered: the snapshot template is read from the environment inside `defineConfig`; if the test pins the object, it is listed and edited.
- Whether `--ds-primary` is the bronze role in both themes and passes AA as hover colour. Covered: Phase 4 computes it test-first and picks the token by ratio.
