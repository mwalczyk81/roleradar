# Implementation Plan: UI Polish and Theming

**Branch**: `009-ui-polish-theming` | **Date**: 2026-11-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-ui-polish-theming/spec.md`

## Summary

Frontend polish with no API, MCP, Domain or data change. Six reviewable phases, in this order: guards and a baseline, one theme implementation, conversion of the app's 203 inline styles to classes, the visual polish pass, themed Identity sign-in pages, and a final CSP step that only runs if the kit has stopped emitting style attributes.

- **Guards first** (Phase 1): a console and `securitypolicyviolation` listener as an auto fixture for every Playwright test, a per-file ratchet test that stops new `style={` props and forces the ceiling down as phases land, and a before-screenshot harness that writes outside the repo and is skipped unless an environment variable is set.
- **Theme** (Phase 2): `web/app/lib/theme.ts` holds cookie parsing and writing, system-theme detection, attribute writing and the bootstrap script. `useTheme` and `root.tsx` both use it. Same cookie, same first-paint behaviour.
- **Conversion** (Phase 3): route families, one commit-sized slice each, with a stop-able boundary after the five big files (`companies.$id`, `postings.$id`, `applications.$id`, the two dialogs: 150 of 203). Screenshot comparison per slice, both themes.
- **Polish** (Phase 4): each item is driven by a failing computed-style, contrast or layout test. The search-run yield becomes text plus a small kit `ColumnChart`.
- **Identity** (Phase 5): kit CSS and fonts copied into `wwwroot/ds` at build time from `web/app/ds`, committed `identity.css` and `theme.js`, a CSP for `/Account` pages whose `form-action` is derived from the registered clients, no inline style or script. `.dockerignore` and its coverage test are extended so `.css` and `.js` cannot be dropped the way `*.cshtml` once was.
- **CSP flip** (Phase 6): remove `style-src-attr 'unsafe-inline'` only if K1 is vendored (a scripted check finds zero `style=` in `web/app/ds/components/src`). Otherwise the phase writes `kit-style-emitters.md` with the exact list and ends. `web/app/ds` is never edited.
- **Decision record**: ADR 0034 (Identity serves kit assets and a CSP, with the form-action rule).

Findings are in [research.md](./research.md); the small amount of state in [data-model.md](./data-model.md); interface rules in [contracts/](./contracts/); validation steps in [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: TypeScript 5.9 / Node 22 (web, React Router v7 SSR); C# 13 / .NET 10 (Identity host only).

**Primary Dependencies**: none new. Playwright `toHaveScreenshot` is already in `@playwright/test` 1.64.1; axe-core is already used.

**Storage**: none. The only persisted value stays the `roleradar_theme` cookie.

**Testing**: Vitest (unit, components, architecture, security), Playwright Chromium with the shared reduced-motion axe helper, xUnit v3 (Unit, Contract, Integration with Docker for the Identity image). No new test projects.

**Target Platform**: Windows dev, Linux containers (Linux CI gate in `node:22`).

**Project Type**: monorepo: React Router web app plus the Identity host.

**Performance Goals**: no regression. Identity serves three small static files and about 6 font files, cached by the browser; no server work per request beyond the header middleware.

**Constraints**: no change to `web/app/ds`; imports through `web/app/ui.ts`; no `any`; untrusted text as React text nodes; LF only; fictional data only; nothing runs against the developer's stack or Mongo; no commit or push; WCAG AA in both themes for every colour pair touched.

**Scale/Scope**: 203 app inline styles in 17 files; 17 web routes and components; 2 Identity pages; 150 kit-emitted style props in 27 files (read-only input to Phase 6).

## Constitution Check

*GATE: passed before Phase 0 research; re-checked after Phase 1 design. No violations.*

| Principle | Result | How |
| --- | --- | --- |
| I Privacy first | Pass | Fictional fixtures only; baseline screenshots stay outside the repo and are never committed; the denylist check runs in `pre-commit` each phase. |
| II Tests first; isolated containers | Pass | Every phase starts red with the failing output captured. Visual items fail on computed-style, contrast or layout assertions, not on screenshot diffs. Integration uses Docker images built from the working tree, never the running stack. |
| III Layering enforced | Pass | Frontend and Identity host only. Architecture tests stay green. The Identity change stays inside the Identity project (the only project allowed to reference Duende). |
| IV Named seams only | Pass | No new abstraction beyond a small theme module and a header middleware. No generic repository, mediator or bus. |
| V Untrusted text; narrow audited tools | Pass | No MCP change. Markdown still renders through React text nodes with the same link rules; the polish touches CSS only. |
| VI stdout hygiene | Pass | No stdio host change. |
| VII Durable data | Pass | No data or script that destroys data. The Identity change adds static files only. |
| VIII ADRs; pinned deps | Pass | ADR 0034 for the Identity assets and CSP. No new package or image tag. |
| IX Baseline conventions | Pass | UTC dates unchanged. No new collections. |

AGENTS web rules mapped: contract first (no API/contract change), vendored design system copy-only (Phase 6 never edits `ds`; K1 is a request), `ui.ts` imports only, no `any`, shared helpers (`theme.ts` becomes one; no copied logic), tests first, axe with the shared reduced-motion helper, fictional data.

## Project Structure

### Documentation (this feature)

```text
specs/009-ui-polish-theming/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── theme-module.md
│   ├── inline-style-ratchet.md
│   ├── identity-assets-and-csp.md
│   └── csp-policies.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
web/app/
├── lib/theme.ts                       # new: the one theme implementation (Phase 2)
├── lib/use-theme.ts                   # delegates to theme.ts (Phase 2)
├── root.tsx                           # uses useTheme + theme.ts script; ErrorBoundary body class (Phases 2, 3)
├── app.css                            # classes replacing 203 inline styles; polish rules (Phases 3, 4)
├── routes/*.tsx, components/*.tsx     # style props removed (Phase 3); Insights, postings list, Markdown (Phase 4)
└── ds/                                # NEVER EDITED

web/scripts/kit-style-emitters.mjs     # new: per-file kit count (Phase 6)
web/server.ts                          # Phase 6 only: drop style-src-attr 'unsafe-inline' if K1 vendored

web/tests/
├── architecture/inline-style-ratchet.test.ts    # new (Phase 1)
├── architecture/inline-style-ceiling.json       # new (Phase 1; lowered each phase)
├── architecture/kit-style-emitters.test.ts      # new (Phase 6 precondition check)
├── architecture/theme-single-source.test.ts     # new (Phase 2)
├── unit/theme.test.ts                           # new (Phase 2)
├── unit/{root.test.tsx,use-theme.test.ts}       # edited (Phase 2)
├── e2e/support/{csp-guard.ts,app-harness.ts}    # new (Phase 1)
├── e2e/visual-baseline.spec.ts                  # new, env-gated, with the census (Phase 1)
├── e2e/theme-first-paint.spec.ts                # new (Phase 2)
├── e2e/converted-styles.spec.ts                 # new: computed-style pins per slice (Phase 3)
├── e2e/identity-signin.spec.ts                  # new: Identity image in Chromium, Docker (Phase 5)
├── e2e/polish.spec.ts                           # new: layout, contrast, computed style (Phase 4)
└── e2e/*.spec.ts                                # import swap to guarded test (Phase 1)

src/RoleRadar.Identity/
├── Pages/Account/{Login,Logout}.cshtml          # no <style>, no style=, link css and script (Phase 5)
├── wwwroot/css/identity.css                     # new, committed (Phase 5)
├── wwwroot/js/theme.js                          # new, committed (Phase 5)
├── wwwroot/ds/                                  # generated at build from web/app/ds, git-ignored
├── Services/AccountSecurityHeaders.cs           # new: CSP middleware for /Account (Phase 5)
├── Program.cs                                   # UseStaticFiles, middleware (Phase 5)
└── RoleRadar.Identity.csproj                    # copy target (Phase 5)

deploy/Dockerfile.identity                       # COPY web/app/ds (Phase 5)
.dockerignore                                    # !src/RoleRadar.Identity/wwwroot/** (Phase 5)
.gitignore                                       # src/RoleRadar.Identity/wwwroot/ds/ (Phase 5)
docs/adr/0034-identity-serves-kit-assets-and-csp.md   # new (Phase 5)

tests/
├── RoleRadar.Tests.Unit/Containers/DockerIgnoreCoverageTests.cs  # extended (Phase 5)
├── RoleRadar.Tests.Unit/Identity/                                # header middleware, no-inline-style page tests (Phase 5)
├── RoleRadar.Tests.Contract/Identity/                            # served assets and headers if the test host can (Phase 5)
└── RoleRadar.Tests.Integration/Containers/IdentityImageLoginPageTests.cs  # extended (Phase 5)
```

**Structure Decision**: existing layout; no new projects. New files are one theme module, two e2e support modules, one ratchet test, one Identity middleware and two committed Identity static files.

## Phases

Every phase stops at its boundary and reports. **Gate at the end of every phase**: `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, then `bash scripts/linux-web-check.sh`, `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`. `pwsh ./scripts/review-gate.ps1` runs at Phase 5 (Identity host, Dockerfile, `.dockerignore`) and at Phase 6 (it changes `web/server.ts`, which ships in the web image). Numbers in reports come from command output only.

Each phase report lists: new numbers from command output, remaining inline-style count (from the ratchet), files changed (split into production, tests, config/docs), every existing test edited and why, what could not be run, and every judgement call left for Matt.

### Phase 1 - Baseline and guards (no production change)

- **Scope**: `support/csp-guard.ts` (auto fixture failing any test on a CSP violation, `securitypolicyviolation` and console text); `support/app-harness.ts` (extracted stubbed API, fictional fixtures and session seeding from `csp-style-attribute.spec.ts`); `visual-baseline.spec.ts` (env-gated by `ROLERADAR_VISUAL_DIR`, every route, light and dark, 1280 by 800, reduced motion, fixed clock, plus a style-attribute census: per route, the elements in the server-rendered HTML (JavaScript disabled) that carry a `style` attribute, counted and listed as `tag.class`); `inline-style-ratchet.test.ts` and `inline-style-ceiling.json` (per file, total 203).
- **Tests first**: ratchet test is added with a ceiling of 202 for one file and shown failing, then set to the true values; guard fixture is shown failing by a deliberate throwaway page whose strict CSP blocks an inline `<script>`; both throwaways are removed.
- **Existing tests edited**: `csp-style-attribute.spec.ts` (extraction and guarded `test` import, no assertion change); one-line import swap in the other e2e specs. `playwright-config.test.ts` only if it pins the config object.
- **Produces**: the baseline screenshots (outside the repo), the per-route blocked-attribute numbers, the real ceiling file. No visual change.
- **Judgement calls**: none.

### Phase 2 - Theme consolidation (US2)

- **Scope**: `theme.ts`, `use-theme.ts` and `root.tsx` per [contracts/theme-module.md](./contracts/theme-module.md). Cookie name, values, path, SameSite, max-age, Secure-on-HTTPS unchanged.
- **Tests first**: `theme.test.ts` (parse present, absent, invalid; serialize with and without HTTPS; system theme; bootstrap script source contains the shared cookie name and is valid on a fake document), a Playwright check that the server HTML for `Cookie: roleradar_theme=dark` already has `data-theme="dark"` before any script runs (JavaScript disabled context), the existing "Theme and Page Reset" e2e tests unchanged, and a source test that the regex for the cookie exists in exactly one module. All shown failing, then passing.
- **Existing tests edited**: `root.test.tsx` and `use-theme.test.ts` (reason in research R12).
- **Visual check**: baseline comparison unchanged in both themes.
- **Judgement calls**: none.

### Phase 3 - Inline styles to classes (US1)

- **Order and stop points**, with the ratchet ceiling lowered at each boundary:
  1. Small routes: `root.tsx` ErrorBoundary (1), `not-authorized` (2), `service-unavailable` (3), `auth.logout` (2), `auth.callback` (5), `search-runs` (3), `criteria` (5): 21 styles. Ceiling 182.
  2. List routes: `companies._index` (4), `applications._index` (9), `postings._index` (5), `notes._index` (6), `notes.$id` (8): 32. Ceiling 150.
  3. **Stop-able boundary A**: `postings.$id` (37) and `TransitionPostingDialog` (17): 54. Ceiling 96.
  4. **Stop-able boundary B**: `companies.$id` (40): ceiling 56.
  5. **Stop-able boundary C**: `applications.$id` (32) and `CreateApplicationDialog` (24): ceiling 0. Then the ratchet is replaced by a lint rule on the JSX `style` attribute.
- **Method per file**: group repeated style objects into named classes in `app.css` using `--ds-*` tokens only (existing token-discipline lint rule applies: no raw hex or px); one class per distinct need, no merging of look-alikes with different intent. The 203 values are static; any computed value found is listed as an exception with a data attribute or custom property.
- **Tests first**: per slice, the ratchet ceiling is lowered first (red), then the slice makes it green; the per-slice census count on those routes falls by the number of removed app styles that render there, and the end-of-phase census is by definition the kit-only list (the ratchet guarantees app styles are 0); computed-style assertions (padding, gap, widths such as `12rem`, `7rem`, `white-space: nowrap`) for the values that matter are added in the same slice and fail before the class exists.
- **No visual change**: baseline compared in both themes for every touched route.
- **Existing tests edited**: `remaining-entities.component.test.tsx` (four `style.whiteSpace` assertions become class assertions); the EmptyState check in `csp-style-attribute.spec.ts` for `/service-unavailable` and `/not-authorized` only as far as page-level styles change.
- **Judgement calls**: any pair of near-identical style objects merged or kept apart; listed in the report.

### Phase 4 - Visual polish (US3)

- **Items, each with a failing check first**: role-title link colours and focus ring (computed colour and contrast ratio in both themes); Min Score placeholder contrast (measure first; K4 only if the kit input is the cause); quieter Insights drill-down links (contrast, focus indicator, underline only on hover and focus, axe); equal tile heights per row; "See all postings in this window" moved beside the postings funnel (exactly one, inside that section); window control position confirmed or moved, with `nav` name and `aria-current` kept; Markdown table region full card width with prose at 72ch and no horizontal page scroll; h4 to h6 computed style differs from `<p>` and h5 from h6; search-run yield becomes the latest-run text plus a small `ColumnChart` (`framed={false}`, reduced `height`) with the kit table view, and the local toggle, table and `Sparkline` are removed.
- **Existing tests edited**: `insights-trends.test.tsx`, `insights-trends.spec.ts` (and `insights-page.test.tsx` if it names the removed elements), because the Sparkline and the run-table toggle go away. Their assertions on the weekly chart stay.
- **Axe**: the shared reduced-motion helper on Insights, Postings, Notes and a Markdown note with a wide table, both themes.
- **Judgement calls for Matt's visual review**: J1 (quieter links), J2 (placement of the postings link), J3 (window control stays at the top), J4 (is the small `ColumnChart` readable enough), J5 (h4 to h6 treatment), J6 (equal-height method), J7 (hover colour).

### Phase 5 - Identity sign-in pages (US4)

- **Scope**: per [contracts/identity-assets-and-csp.md](./contracts/identity-assets-and-csp.md): delete both `<style>` blocks and the one `style=` attribute; link `/ds/tokens.css`, `/ds/components/bundle.css`, `/css/identity.css` and `/js/theme.js`; build-time copy of kit CSS and fonts; `UseStaticFiles`; CSP middleware for `/Account`; ADR 0034; `.dockerignore`, `.gitignore`, `Dockerfile.identity`.
- **Tests first**: header-middleware unit tests (policy string, `form-action` built from the clients' redirect and post-logout origins, only `/Account` responses); a page test that Login and Logout HTML has no `<style>` and no `style=`; `DockerIgnoreCoverageTests` extended to `.css` and `.js` plus a pinpoint test for the two Identity-owned files (red before the `.dockerignore` line is added); `IdentityImageLoginPageTests` extended to fetch the assets from the container and re-check the HTML; Playwright against the in-process or image host for both system schemes (computed font family and primary colour, axe with the shared helper, zero CSP violations).
- **Behaviour unchanged**: the existing login e2e (`real-stack.spec.ts`), the contract tests in `tests/RoleRadar.Tests.Contract/Identity/`, lockout and rate-limit tests, and the manual PKCE walk in [005a quickstart](../005a-api-and-login/quickstart.md) section 5a must pass untouched.
- **Existing tests edited**: `DockerIgnoreCoverageTests`; `LoginModelRazorPageTests` only if it asserts the old markup; `IdentityImageLoginPageTests` is extended, not changed.
- **Gate**: the standard gate plus `pwsh ./scripts/review-gate.ps1` and `pwsh ./scripts/test-images.ps1` if it covers the Identity image.
- **Judgement calls**: J8 confirmed (system scheme only, no cookie); layout of the sign-in card (spacing, which kit button variant); both listed for visual review.

### Phase 6 - CSP flip, only if K1 is vendored

- **Precondition test** `kit-style-emitters.test.ts`: counts `style=` in `web/app/ds/components/src/**` (today 150 in 27 files). Zero means K1 is vendored.
- **If zero**: remove `style-src-attr 'unsafe-inline'` from `web/server.ts`; update `server.test.ts` and the EmptyState test to computed-style-before-hydration through classes; run the full suite with the CSP guard on every route in both themes (this is where a kit-emitted attribute would show up); replace the ratchet by the lint rule if not yet done; `review-gate.ps1`.
- **If not zero**: record the block in `specs/009-ui-polish-theming/kit-style-emitters.md` with the exact per-file list and counts, which routes each kit component appears on (from the Phase 3 end census), and the K1 wording unchanged; leave the CSP as is; end. A small follow-up feature picks it up after Matt vendors K1.
- **Do not**: edit `web/app/ds`, add local copies of kit components, or loosen the CSP differently (hashes, nonces on attributes).
- **Judgement calls**: none; the outcome is binary.

## Scope Notes

1. **Out of scope**: README, screenshots for docs, demo command, SECURITY.md, CONTRIBUTING, CHANGELOG, hosts-file requirement, enum generation, CLI transition command, passkeys (spec Scope).
2. **Kit requests that remain**: K1 (blocks Phase 6 only) and K4 (only if the placeholder fails and the kit input is the cause).
3. **`review-gate.ps1` placement** is the plan's reading of "any image": Phases 5 and 6 touch the Identity image or `web/server.ts`. If Matt wants it at every phase, say so.

## Complexity Tracking

No constitution violations to justify.
