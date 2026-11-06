---

description: "Task list for 009-ui-polish-theming"
---

# Tasks: UI Polish and Theming

**Input**: Design documents from `/specs/009-ui-polish-theming/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present). ADR 0034 is drafted in Phase 5 and accepted by Matt at T095.

**Tests**: MANDATORY (Constitution II, AGENTS.md). In every phase the test tasks come first, are run **red**, and the failing output is pasted into the phase report before any implementation task starts. For visual items the failing test is a computed-style, contrast or layout assertion, never a screenshot diff.

**Organization**: One phase per plan phase. Phase 1 is guards and baseline (no story label), Phase 2 theme (US2), Phase 3 inline-style conversion (US1), Phase 4 polish (US3), Phase 5 Identity (US4), Phase 6 CSP flip (US1, last task of US1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task). Tasks that add classes to `web/app/app.css` are never [P] with each other.
- **[Matt]**: a task only Matt can do or approve. Agents stop and wait.
- Paths are repo-relative.
- **Never**: edit `web/app/ds`; run anything against the developer's running stack or Mongo; `docker compose down -v`, `wipe`, `restore --force-overwrite`; read or print `web/.env.local` or `deploy/.env`; commit or push; write CRLF; touch `bin/`, `obj/`, `dist/`. Fictional data only. Avoid the private denylist word in every file (write "evidence" or "check").
- **Phase gate** (the last task before each Boundary, unless a task says otherwise): `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, then `bash scripts/linux-web-check.sh`, `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, and an LF check (`git ls-files --eol` shows no `w/crlf` for files this phase changed; convert any that do). Phases 5 and 6 add `pwsh ./scripts/review-gate.ps1`. Phases 1 to 4 do not run it.
- Report numbers from command output only, say what could not be run, and stop at each **Boundary** task. Each Boundary report lists: new numbers from command output, remaining inline-style count (from the ratchet), files changed (production, tests, config/docs listed separately), every existing test edited and why, and every judgement call left for Matt.
- Out of scope: README, screenshots for docs, demo command, SECURITY.md, CONTRIBUTING, CHANGELOG, hosts-file requirement, enum generation, CLI transition command, passkeys, any edit to `web/app/ds`.

---

## Phase 1: Baseline and guards (plan Phase 1; no production change)

**Purpose**: a violation guard on every Playwright test, a ratchet that stops new inline styles, and a before-screenshot harness that never touches the repository. Record the starting numbers.

### Tests first (run red, paste failing output)

- [X] T001 Record the starting counts from command output into new `specs/009-ui-polish-theming/baseline-counts.md`: (a) app `style` props: `grep -rnoE "style=\{" web/app --include=*.tsx --exclude-dir=ds | wc -l` and the per-file list (expect 203 in 17 files); (b) kit `style={` props: the same command over `web/app/ds/components/src` (expect 150 in 27 files). Paste the exact commands and outputs. If a number differs from 203, 150 or 27, stop and tell Matt before continuing, because the ratchet schedule in `contracts/inline-style-ratchet.md` depends on them.
- [X] T002 [P] Write the ratchet test in new `web/tests/architecture/inline-style-ratchet.test.ts` per `contracts/inline-style-ratchet.md`: parse `web/app/**/*.{ts,tsx}` (not `web/app/ds/**`) with the TypeScript compiler API already in `typescript`, count JSX attributes named `style` per file, load `web/tests/architecture/inline-style-ceiling.json`, fail when a count is above its ceiling or a ceiling is above its count, and treat a missing file as ceiling 0.
- [X] T003 [P] Write `web/tests/e2e/support/csp-guard.ts`: `export const test = base.extend(...)` with an auto fixture that, for every page in the context, listens for the `securitypolicyviolation` event (via `page.addInitScript`) and for console messages containing "content security policy" or "violates the following" (case-insensitive), and fails the test at teardown with the collected messages. Re-export `expect`.
- [X] T004 Create `web/tests/architecture/inline-style-ceiling.json` with a deliberately wrong ceiling (one file one below its real count) and run `pnpm --filter web run test -- inline-style-ratchet`. Paste the red output (the failure must name the file and the two numbers).
- [X] T005 Create a throwaway spec `web/tests/e2e/tmp-csp-guard-red.spec.ts` that imports `test` from `./support/csp-guard.js`, serves a page with a blocked inline `<script>` through a loopback server with a strict CSP, and expects the guard to fail it. Run it and paste the red output. Delete the throwaway in T013.

### Implementation

- [X] T006 Correct `web/tests/architecture/inline-style-ceiling.json` to the real per-file counts from T001 (total 203). Run the ratchet test and paste the green output.
- [X] T007 Extract the stubbed API, fictional fixtures, TLS setup, session seeding and server start/stop (the `beforeAll`/`afterAll` bodies and their module-level state, roughly lines 1 to 607) from `web/tests/e2e/csp-style-attribute.spec.ts` into new `web/tests/e2e/support/app-harness.ts`, exporting a `startAppHarness()` that returns the base URL, mutable stub state setters and a `stop()`. Behaviour-preserving only. Before the extraction run `pnpm --filter web exec playwright test tests/e2e/csp-style-attribute.spec.ts` and record the pass count; after it, the count must be identical.
- [X] T008 Switch `web/tests/e2e/csp-style-attribute.spec.ts` to import `test` and `expect` from `./support/csp-guard.js`, and use `startAppHarness()`. No assertion changes. Re-run and record the pass count.
- [X] T009 [P] One-line import swap to `./support/csp-guard.js` in `web/tests/e2e/insights.spec.ts`, `insights-drilldown.spec.ts`, `insights-isolation.spec.ts`, `insights-trends.spec.ts`, `notes-markdown.spec.ts`, `posting-rejection-sync.spec.ts`, `postings-server-sort.spec.ts` and `legacy-api-retirement.spec.ts`. Do not change `real-stack.spec.ts`. Run the e2e suite and record the pass count against the pre-change count.
- [X] T010 [P] Check `web/tests/unit/playwright-config.test.ts` for assertions that pin the config object. If it pins it, list the exact change; otherwise record "not affected".
- [X] T011 Write `web/tests/e2e/visual-baseline.spec.ts` (uses `startAppHarness()` and the guarded `test`): skipped unless `ROLERADAR_VISUAL_DIR` is set; when set, use `snapshotPathTemplate` rooted there; for every route in `web/app/routes.ts` in light and dark (set `data-theme` and `emulateMedia({ reducedMotion: "reduce" })`, viewport 1280 by 800, fixed clock) take a full-page `toHaveScreenshot`. Cover: `/postings`, `/postings/:id` (lead, applied, and with the transition dialog open), `/companies`, `/companies/:id`, `/applications`, `/applications/:id` (and with the append-event dialog open), `/criteria`, `/notes`, `/notes/:id` (including the wide-table fixture from `web/tests/fixtures/notes-markdown-fictional.json`), `/search-runs`, `/insights?window=90d` and `window=all` (use `support/insights-stub.ts`), `/not-authorized`, `/service-unavailable`, the logout result, the callback error state, and the root `ErrorBoundary`. Add the style-attribute census: for each route, request the server-rendered HTML with JavaScript disabled, parse it, count elements that carry a `style` attribute and list each as `tag.class`, and write the table to `$ROLERADAR_VISUAL_DIR/census.json`. No CSP header is involved and nothing tries to tell app from kit; the end-of-Phase-3 census is the kit-only list. Never write images under the repository.
- [X] T012 Run the spec once with `ROLERADAR_VISUAL_DIR` unset (expect all skipped), once with it set and no baseline (expect red: missing snapshots; paste), then once with `--update-snapshots` to write the baseline under `%TEMP%\roleradar-visual\baseline`. Paste the route count, the census table, and `git status --short` proving no image or snapshot is inside the repository.
- [X] T013 Delete `web/tests/e2e/tmp-csp-guard-red.spec.ts`. Confirm `git status` lists only intended files.
- [X] T014 Phase gate (see header). Paste the counts. No visual change is expected, so the baseline comparison is skipped in this phase.
- [X] T015 **Boundary: Phase 1.** Report per the header: ratchet total 203, kit 150 in 27 files (from T001), census table per route, files changed. Wait for "go" before Phase 2.

**Phase 1 implementation notes** (what differed from the task text, recorded after the work):

- T007: the stubbed API handler moved to `web/tests/e2e/support/app-harness.ts` as `startAppHarness(state, sessionId)` plus a `StubState` interface and `defaultStubState()`. The spec keeps its own `let` variables and hands the harness an object of accessors over them, so no test body changed. Extracting plain setters would have rewritten about 216 references.
- T011: the baseline spec has two describe blocks because Insights and the wide-table note use the existing `insights-stub.ts` harness. The census parser lives in `web/tests/e2e/support/style-census.ts` with a unit test (`web/tests/unit/style-census.test.ts`), written red first. `playwright.config.ts` gained a `snapshotPathTemplate` that applies only when `ROLERADAR_VISUAL_DIR` is set.

**Checkpoint**: any CSP violation on any Playwright test fails it; new inline styles fail the ratchet; a baseline exists outside the repo.

---

## Phase 2: One theme implementation (plan Phase 2; US2)

**Goal**: the theme logic exists once; first paint is right; the cookie and storage behaviour are unchanged.

**Independent test**: unit tests on `theme.ts`, plus a JavaScript-disabled browser check of the server HTML.

### Tests first (run red, paste failing output)

- [X] T016 [P] [US2] New `web/tests/unit/theme.test.ts` per `contracts/theme-module.md`: `parseThemeCookie` (header with `roleradar_theme=dark`, `light`, absent, `roleradar_theme=purple`, `null`, `undefined`, a cookie among others, a name that merely ends in the same text), `serializeThemeCookie("dark", false)` equals `roleradar_theme=dark; Path=/; SameSite=Lax; Max-Age=31536000` and with `true` ends in `; Secure`, `systemTheme({ matches: true })` is `dark`, `applyTheme` sets `data-theme`, and `THEME_BOOTSTRAP_SCRIPT` evaluated against a fake `document` and `matchMedia` sets the right attribute for cookie dark, cookie light, no cookie with dark system, no cookie with light system, and does not throw when `matchMedia` is missing.
- [X] T017 [P] [US2] New `web/tests/architecture/theme-single-source.test.ts`: scan `web/app` (not `ds`) and assert the cookie name literal `roleradar_theme` appears in exactly one source file (`web/app/lib/theme.ts`) and `matchMedia("(prefers-color-scheme` appears only there and in nothing under `web/app/root.tsx`.
- [X] T018 [P] [US2] New `web/tests/e2e/theme-first-paint.spec.ts` (guarded `test`, `app-harness`): with a browser context created with `javaScriptEnabled: false` and a `roleradar_theme=dark` cookie, fetch `/postings` and assert `document.documentElement.getAttribute("data-theme") === "dark"`; same for `light`; with no cookie and no script the attribute is absent and the computed `--ds-surface` equals the light value (documents today's behaviour).
- [X] T019 [US2] Run T016 to T018 and paste the failing output. T016 fails on a missing module, T017 on four files, T018 should already pass for the cookie cases (it pins today's behaviour); say so in the report.

### Implementation

- [X] T020 [US2] Create `web/app/lib/theme.ts` with the exports in `contracts/theme-module.md`. The script string is built from `THEME_COOKIE` and contains no template placeholders that depend on user input.
- [X] T021 [US2] Rewrite `web/app/lib/use-theme.ts` to use `theme.ts`: same signature `useTheme(initialTheme?)` and return `{ theme, toggleTheme }`; first render returns `initialTheme`; the effect reconciles with cookie, DOM attribute and system setting and follows the system live only while no cookie exists.
- [X] T022 [US2] Edit `web/app/root.tsx`: loader uses `parseThemeCookie`; the component calls `useTheme(serverTheme)`; the head script is `THEME_BOOTSTRAP_SCRIPT` with the nonce; remove the local state, effect and regexes. Leave `meta`, `links`, the nonce handling and the ErrorBoundary alone (its inline style is Phase 3).
- [X] T023 [US2] Edit `web/tests/unit/root.test.tsx` and `web/tests/unit/use-theme.test.ts` only where they assert the removed duplicated state, effect or script text; list each changed assertion and the reason in the report. Loader, meta, links and ErrorBoundary assertions stay unmodified.
- [X] T024 [US2] Run T016 to T018 plus the existing "Theme and Page Reset" block of `web/tests/e2e/csp-style-attribute.spec.ts`; paste green output. Run the baseline comparison (`ROLERADAR_VISUAL_DIR` set, no `--update-snapshots`) and confirm every route, light and dark, matches.
- [X] T025 Phase gate (see header). Lower nothing in the ratchet (still 203; `root.tsx` ErrorBoundary is untouched).
- [X] T026 **Boundary: Phase 2.** Report per the header. Judgement calls: none. Wait for "go" before Phase 3.

**Phase 2 implementation notes**:

- T016 to T019 produced these test files: `web/tests/unit/theme.test.ts`, `web/tests/architecture/theme-single-source.test.ts`, `web/tests/e2e/theme-first-paint.spec.ts`, plus `web/tests/unit/root-theme-render.test.tsx` (the root document for each cookie state and for no cookie) and `web/tests/unit/use-theme.server.test.tsx` (the hook with no document). The last two and the first-paint spec pin today's behaviour, so they passed before the change; the red tests were `theme.test.ts` (module missing) and the single-source test (three failures naming the duplicate files).
- T023: `root.test.tsx` and `use-theme.test.ts` needed no edit; every assertion passes unmodified. The test that did need an edit is `web/tests/security/markdown-invariants.test.ts` (the one `dangerouslySetInnerHTML` invariant named `themeScript` in `root.tsx`; it now names `THEME_BOOTSTRAP_SCRIPT` and checks the template in `lib/theme.ts` interpolates only two module constants).
- Cookie parsing is now anchored (a whole cookie name, value exactly `light` or `dark`). The old pattern also matched `x_roleradar_theme=dark` and `roleradar_theme=darkish`. No real cookie differs.

**Checkpoint**: US2 holds; the cookie string is byte-identical; first paint is correct with and without script.

---

## Phase 3: Inline styles to classes (plan Phase 3; US1)

**Goal**: all 203 app inline styles become classes in `web/app/app.css` using `--ds-*` tokens, no visual change.

**Independent test**: ratchet at the slice ceiling, baseline comparison matches in both themes, the census count on the converted routes falls by the number of app styles removed that render there, and computed-style assertions hold.

**Method for every slice**: (1) lower the ceiling in `web/tests/architecture/inline-style-ceiling.json` and run the ratchet red; (2) add computed-style assertions for the values that matter in the slice and run them red against the still-inline code only if they can fail (a class that does not exist yet); where the value is already satisfied by the inline style, the assertion pins it and passes before and after, which is stated in the report; (3) group repeated style objects into named classes, one class per distinct need, tokens only (the lint rule forbids raw hex and px); never merge look-alikes with different intent; (4) delete the `style` props; (5) compare to the baseline; (6) phase gate on the slice. The 203 values are static. If a value is found to be computed, stop and ask Matt, list it as an exception in `spec.md`, and use a data attribute or a custom property set outside a JSX `style` attribute.

### Slice 3a: small routes (21 styles; ceiling 203 to 182)

- [X] T027 [US1] Lower the ceiling for `web/app/root.tsx` (1), `web/app/routes/not-authorized.tsx` (2), `service-unavailable.tsx` (3), `auth.logout.tsx` (2), `auth.callback.tsx` (5), `search-runs.tsx` (3) and `criteria.tsx` (5) to 0 in `inline-style-ceiling.json`. Run the ratchet and paste the red output.
- [X] T028 [P] [US1] Add computed-style assertions in new `web/tests/e2e/converted-styles.spec.ts` (guarded `test`, `app-harness`) for these routes: the ErrorBoundary body padding and font family, the `white-space: nowrap` on the criteria date cells, and the layout of `/not-authorized`, `/service-unavailable` and the callback/logout result. Run and paste the output (red or pinned-green as stated in the method).
- [X] T029 [US1] Convert the 21 styles in the seven files above to classes in `web/app/app.css`. Check `service-unavailable.tsx` and `not-authorized.tsx` still apply their styles before hydration (the existing EmptyState tests in `csp-style-attribute.spec.ts`).
- [X] T030 [US1] Edit `web/tests/routes/remaining-entities.component.test.tsx`: the `style.whiteSpace` assertions that belong to the routes converted in this slice (find them by route; the file asserts it in four places, at about lines 112, 168, 209 and 254) become class assertions; leave the others for their own slice. Edit the EmptyState checks in `web/tests/e2e/csp-style-attribute.spec.ts` for `/not-authorized` and `/service-unavailable` only as far as the page-level inline styles they inspected are gone; keep the assertion about the kit `EmptyState` computed styles before hydration (the kit still emits them). List each edit and reason.
- [X] T031 [US1] Run the baseline comparison for the touched routes in both themes and the census; paste route-by-route matches and the census counts before and after.
- [X] T032 [US1] Phase gate (see header). Report remaining count 182.
- [X] T033 **Boundary: Slice 3a.** Wait for "go".

### Slice 3b: list routes (32 styles; ceiling 182 to 150)

- [X] T034 [US1] Lower ceilings to 0 for `web/app/routes/companies._index.tsx` (4), `applications._index.tsx` (9), `postings._index.tsx` (5), `notes._index.tsx` (6) and `notes.$id.tsx` (8). Paste the red ratchet output.
- [X] T035 [P] [US1] Add computed-style assertions in `web/tests/e2e/converted-styles.spec.ts` for these routes: the `12rem` wrapper width in `applications._index.tsx`, the `7rem` width in `postings._index.tsx` (Min Score input wrapper), `white-space: nowrap` on the `notes._index.tsx` date cells, and the `notes.$id.tsx` layout values. Run and paste the output.
- [X] T036 [US1] Convert the 32 styles to classes in `web/app/app.css`.
- [X] T037 [US1] Edit `web/tests/routes/remaining-entities.component.test.tsx`: the `style.whiteSpace` assertions that belong to the routes converted in this slice (the companies link at about line 112, and the notes dates) become class assertions. List each edit and reason.
- [X] T038 [US1] Baseline comparison and census for `/companies`, `/applications`, `/postings`, `/notes`, `/notes/:id`, both themes; paste results.
- [X] T039 [US1] Phase gate (see header). Report remaining count 150.
- [X] T040 **Boundary: Slice 3b.** Wait for "go".

### Slice 3c: stop-able boundary A, `postings.$id` and the transition dialog (54 styles; ceiling 150 to 96)

- [X] T041 [US1] Lower ceilings to 0 for `web/app/routes/postings.$id.tsx` (37) and `web/app/components/TransitionPostingDialog.tsx` (17). Paste the red ratchet output.
- [X] T042 [P] [US1] Add computed-style assertions in `web/tests/e2e/converted-styles.spec.ts` for `/postings/:id`: the definition-list `margin: 0` cells, section spacing, action row layout, and the open transition dialog's field spacing. Run and paste.
- [X] T043 [US1] Convert the 54 styles to classes in `web/app/app.css`. Component tests (`web/tests/components/transition-dialog.test.ts`, `posting-detail-safety.test.ts`, `web/tests/routes/postings.detail.test.tsx`) must pass unmodified; if one asserts a style attribute, list it and the reason before editing.
- [X] T044 [US1] Baseline comparison and census for `/postings/:id` (lead, applied, dialog open), both themes; paste results.
- [X] T045 [US1] Phase gate (see header). Report remaining count 96.
- [X] T046 **Boundary: Slice 3c (optional stop).** Wait for "go".

### Slice 3d: stop-able boundary B, `companies.$id` (40 styles; ceiling 96 to 56)

- [X] T047 [US1] Lower the ceiling for `web/app/routes/companies.$id.tsx` (40) to 0. Paste the red ratchet output.
- [X] T048 [P] [US1] Add computed-style assertions in `web/tests/e2e/converted-styles.spec.ts` for `/companies/:id` (edit form spacing, career-source rows, tag list). Run and paste.
- [X] T049 [US1] Convert the 40 styles to classes in `web/app/app.css`. `companies.component.test.tsx`, `companies.action.test.ts` and `companies.a11y.test.ts` pass unmodified, or the edit is listed.
- [X] T050 [US1] Baseline comparison and census for `/companies/:id`, both themes; paste results.
- [X] T051 [US1] Phase gate (see header). Report remaining count 56.
- [X] T052 **Boundary: Slice 3d (optional stop).** Wait for "go".

### Slice 3e: stop-able boundary C, `applications.$id` and the create-application dialog (56 styles; ceiling 56 to 0)

- [X] T053 [US1] Lower the ceiling for `web/app/components/CreateApplicationDialog.tsx` (24) to 0 and for `web/app/routes/applications.$id.tsx` (32) to 2 (the two kit `Card` padding overrides, K6). Paste the red ratchet output.
- [X] T054 [P] [US1] Add computed-style assertions in `web/tests/e2e/converted-styles.spec.ts` for `/applications/:id` (timeline spacing, event rows, append-event dialog) and the create-application dialog on `/postings/:id`. Run and paste.
- [X] T055 [US1] Convert 54 of the 56 styles to classes in `web/app/app.css`. `create-application-dialog.test.tsx`, `applications.component.test.tsx` and `applications.a11y.test.ts` pass unmodified, or the edit is listed.
- [X] T056 [US1] Baseline comparison and census for `/applications/:id` (plain, append-event dialog open) and `/postings/:id` with the create dialog open, both themes; paste results.
- [ ] T057 [US1] **Deferred to Phase 6** (amended). The ratchet stays a vitest test with per-file ceilings, because the K6 leftovers (8 inline styles on kit `Card` and `Input`) keep the total above zero. In Phase 6, once K1 is vendored and the ceiling can reach 0, add an ESLint rule to `web/eslint.config.js` (`no-restricted-syntax` on `JSXAttribute[name.name='style']`, applied to `web/app/**` except `web/app/ds/**`), shown red first by temporarily adding a `style` prop in a scratch file, then remove the scratch file, `web/tests/architecture/inline-style-ratchet.test.ts` and `inline-style-ceiling.json`, and update `contracts/inline-style-ratchet.md` rule 6 to "done". Until then `contracts/inline-style-ratchet.md` rule 6 stays open.
- [X] T058 [US1] Full-route baseline comparison for every route in both themes and the census table; paste. The remaining census entries are kit-rendered except the 8 known K6 leftovers (the census cannot tell them apart, see the slice 3e notes); they are the input for Phase 6. Saved as `census-end-phase3.json` beside the baseline (outside the repo).
- [X] T059 [US1] Phase gate (see header). Report remaining app count 8 (K6), kit count 150 in 27 files re-measured.
- [X] T060 **Boundary: Phase 3.** Report the full list of near-identical style objects that were merged or kept apart (judgement calls). Wait for "go".

**Phase 3 slices 3a and 3b implementation notes**:

- Slice 3a (21 styles, ceiling 203 to 182) and slice 3b (31 of 32 styles, ceiling 182 to 151): the ceiling is 151, not 150, because one style stays. The Min Score `Input` in `web/app/routes/postings._index.tsx` keeps `style={{ width: "7rem" }}`: the kit `Input` sets an inline `width: 100%` after spreading the consumer's style, so no class can override it. Recorded as kit request K6 (a `width` prop, or K1, which moves the kit's own styling into classes). The ratchet allows exactly one inline style in that file.
- `notes.$id.tsx`: the `Card` `style={{ padding }}` was removed, not converted. It repeated the kit default (`padded` gives `var(--ds-space-6)`), so the computed padding is unchanged.
- `applications._index.tsx` posting title link: its colour named a token that does not exist (`--ds-text-primary`), so it has always inherited. The class keeps the declaration as it was (`.entity-title-link`); phase 4 decides the real colour.
- `web/tests/e2e/converted-styles.spec.ts` pins each converted element: the new class is present, the style attribute is gone, and each old declaration (verbatim, tokens included) resolves to the same computed value on a probe element.

**Phase 3 slice 3c implementation notes**:

- Ceiling 101, not 96: `postings.$id.tsx` keeps four inline styles, the `padding: var(--ds-space-5)` override on four kit `Card`s. The kit `Card` applies its own inline padding after the consumer's style, so no class can change it. Together with the Min Score `Input` in `postings._index.tsx` that makes five leftovers (kit request K6: a `padding` prop on `Card`, a `width` prop on `Input`, or K1). `TransitionPostingDialog.tsx` converted fully (17 of 17); it reuses the existing `.dialog-field`, `.dialog-field-label` and `.dialog-field-error` classes.
- `.filter-group` and `.event-source` gained the aliases `.action-group` and `.caption-small` (same declarations, selector grouping) instead of duplicate rules.
- `web/tests/e2e/support/computed-style.ts` now compares each element with a deep-copied stand-in placed next to it that carries the old declarations as an inline style. That keeps parent context (flex items, grid track sizes, inheritance, percentage widths) identical, so the earlier special cases (flex-item `display`, `width: 28rem`) were removed from the pins.

**Phase 3 slice 3d implementation notes**:

- Ceiling 62 (was 101): `companies.$id.tsx` keeps one inline style, the header `Card` padding override (`var(--ds-space-5)`, kit request K6). The second `<Card style>` (the edit form card, `var(--ds-space-6)`) repeated the kit default and was removed.
- `visual-baseline.spec.ts` gained three company detail captures, recorded before converting: `company-detail-edit` (renamed company, another status, a new tag chip, a half-filled career source), `company-detail-no-sources` (the empty caption) and `company-detail-errors` (a rejected save: the error banner and three inline field errors).
- The CSS minifier writes `background: none` as `background: 0 0`, so the `chip-remove` pin uses the longhands (`background-color: transparent`, `background-image: none`); the computed result is the same.

**Phase 3 slice 3e implementation notes**:

- Final ceiling 8 (was 62): `applications.$id.tsx` keeps two inline styles, the `padding: var(--ds-space-5)` override on the summary `Card` and the timeline `Card` (kit request K6). `CreateApplicationDialog.tsx` converted fully (24 of 24). 54 of 56 converted.
- Ratchet kept, not replaced: T057 is deferred to Phase 6 (lint rule replaces the ratchet once K1 is vendored and the ceiling can reach 0).
- New classes: `link-primary`, `icon-leading`, `timeline-header`, `timeline-heading`, `timeline`, `timeline-event`, `timeline-event-terminal`, `timeline-event-head`, `timeline-event-label`, `timeline-event-notes`, `field-label`, `field-label-medium`, `field-label-hint`, `inline-field-error`, `sync-note`, `modal-actions`, `dialog-subject`, `salary-row`, `override-panel`. Reused unchanged: `page-stack`, `back-link`, `detail-header-row`, `detail-title`, `detail-meta`, `action-group`, `caption-small`, `field-caption`, `card-stack`, `dialog-field-label`.
- Kept apart on purpose: `inline-field-error` (colour `--ds-danger`) vs `dialog-field-error` and `form-error` (`--ds-danger-text`); `field-label-medium` (weight `--ds-weight-medium`) vs `form-label` (600); `modal-actions` (gap space-2, margin space-2) vs `dialog-actions` (space-3); `timeline-event-notes` keeps the undefined `--ds-text-primary` colour token, so it inherits, as before.
- The census cannot split app-owned from kit-rendered elements: kit components emit bare `div`, `span`, `button` with no stable class or data attribute (only a few carry `ds-` classes). Proposal for K1: have every kit component that still emits a style set `data-ds-component="<Name>"`, or drop the style (K1 itself).
- `visual-baseline.spec.ts` gained five captures, recorded before converting: `application-detail-timeline`, `application-append-event-dialog-error`, `application-append-event-dialog-sync-note`, `posting-create-application-dialog-override`, `posting-create-application-dialog-errors`.

**Checkpoint**: US1 steps 1 and 2 hold: zero app inline styles, no visual change, no CSP violation on any route. The CSP flip is Phase 6.

---

## Phase 4: Visual polish (plan Phase 4; US3)

**Goal**: the specific polish items, each with a computed-style, contrast or layout check; aesthetic calls go to Matt.

**Independent test**: `web/tests/e2e/polish.spec.ts` (both themes) plus the shared reduced-motion axe helper.

### Tests first (run red, paste failing output)

- [X] T061 [P] [US3] New `web/tests/e2e/polish.spec.ts` (guarded `test`, `app-harness`, `support/axe.ts`): add a small helper for relative-luminance contrast ratio of two computed colours and use it for every ratio below. Tests: (a) `/postings` role-title link computed colour equals the body text colour at rest and a bronze token colour on hover and on focus, ratio at least 4.5 against the row background in both themes, and a visible focus indicator; (b) the Min Score input placeholder colour against its background, at least 4.5 in both themes.
- [X] T062 [P] [US3] Same file, Insights (using `support/insights-stub.ts`): (c) drill-down links have no underline at rest and an underline on hover and focus, ratio at least 4.5, visible focus; (d) all `.insights-tile` elements in the same grid row have equal bounding-box heights at 1280 wide, including the "No data" tile (add a stub state with a "No data" rate); (e) exactly one link with the text "See all postings in this window" exists and is a descendant of the postings funnel section; (f) the window control is a `nav` with an accessible name and `aria-current` on the active link; (g) the search-run block shows text with the latest run's new postings and the point count, a figure named for the run series, and the kit table view lists every run; no element with class `insights-run-table` or `insights-run-toggle` exists.
- [X] T063 [P] [US3] Same file, notes (using the wide-table fixture in `web/tests/fixtures/notes-markdown-fictional.json`): (h) the table scroll region width equals the note card content width, exceeds the prose column width, and `document.documentElement.scrollWidth <= clientWidth`; (i) prose paragraphs keep their reading width, and the existing Markdown safety tests (`markdown-body.hostile.test.tsx`, `markdown-invariants.test.ts`) stay unmodified (FR-028); (j) computed font size, weight or text transform of h4, h5 and h6 each differ from a `<p>`, and h5 differs from h6.
- [X] T064 [P] [US3] Same file: the shared reduced-motion axe check (`checkAxeInThemes`) on `/postings`, `/insights`, a Markdown note with the wide table, both themes, zero serious or critical violations. Add the identity check (FR-040 to FR-042) to this file too: on `/postings` and `/insights`, in both themes, the computed `font-family` of the page heading starts with `IBM Plex Serif`, of body text with `IBM Plex Sans`, and a primary button or link resolves to the kit primary token.
- [X] T065 [US3] Placeholder measurement: run (b) and paste the measured ratio per theme and the source of the colour (the kit `Input` rule or an app rule). Decide and record in `research.md` R11: if the kit `Input` default fails AA in either theme, K4 stands as written in `spec.md` and the Min Score item is marked blocked on K4 (do not add an app `::placeholder` rule); if the failure comes from an app override, fix it with a class in T071.
- [X] T066 [US3] Run T061 to T064 and paste the failing output; confirm each fails for the intended reason (current inline colour, underlines, unequal heights, orphan link, clipped table, identical headings, sparkline present).
- [X] T067 [P] [US3] Update `web/tests/components/insights-trends.test.tsx` and `web/tests/e2e/insights-trends.spec.ts` (and `web/tests/components/insights-page.test.tsx` if it names the removed elements) so the search-run assertions describe the new behaviour (text plus kit `ColumnChart` with its own table view); keep every weekly-chart assertion unchanged. Run them red against the current Sparkline code and paste; list each edited assertion and its reason.

### Implementation

- [X] T068 [US3] Role-title link: add `.role-title-link` to `web/app/app.css` (text colour at rest; the bronze token chosen by computing the ratio from `web/app/ds/tokens.json` in both themes on hover and focus; focus ring from the kit token) and use it in `web/app/routes/postings._index.tsx`; apply the same class wherever other lists render role titles (check `applications._index.tsx` and `companies.$id.tsx`). Do not copy colour logic into routes.
- [X] T069 [US3] Insights links: restyle `.insights-stage-links a`, `.insights-tile-links a` and `.insights-help a` in `web/app/app.css` (secondary text colour, underline on hover and focus only, kept contrast and focus ring).
- [X] T070 [US3] Tile heights: `.insights-tiles` and `.insights-tile` in `web/app/app.css` so tiles in a row are equal height (grid auto rows and a full-height tile), without changing the tile content.
- [X] T071 [US3] Placeholder: only if T065 found an app-side cause, fix it with a class in `web/app/app.css`; otherwise skip and record K4 in the report.
- [X] T072 [US3] Move "See all postings in this window" in `web/app/routes/insights.tsx` into the postings funnel section (after the funnel and its help text); remove it from "How these are calculated". Keep the `lensListHref("leads", ctx)` target.
- [X] T073 [US3] Window control: keep it at the top; confirm `nav` name and `aria-current` (T062f). If Matt's review (T082) moves it, that is a later change.
- [X] T074 [US3] Markdown: in `web/app/app.css` give prose children the reading width cap and let `.markdown-body .markdown-table-scroll` use the full card content width; set distinct h4, h5 and h6 styles (size steps plus weight, letter spacing or small caps, tokens only). Do not touch `web/app/components/MarkdownBody.tsx` rendering rules (untrusted text stays React text nodes).
- [X] T075 [US3] Search-run yield in `web/app/routes/insights.tsx`: replace the `runsAsTable` state, the "Table view of search runs" button, the local `<table className="insights-run-table">` and the `Sparkline` with a text line (latest run's new postings and the number of points, from `stats.runs.rows`) and a kit `ColumnChart` from `web/app/ui.ts` with `framed={false}`, a reduced `height`, `labelHeader="Run date"` and `valueHeader="New postings"`, mapping each row to `{ label, fullLabel, value }`. Remove `.insights-sparkline`, `.insights-run-toggle` and `.insights-run-table` rules from `web/app/app.css`. If `Sparkline` is no longer imported anywhere, remove the import from `web/app/routes/insights.tsx` (leave `web/app/ui.ts` alone).
- [X] T076 [US3] Run T061 to T064 and T067 and paste green output (the placeholder test green or recorded as blocked on K4).
- [X] T077 [US3] Baseline comparison: the only expected differences are on `/postings`, `/insights` (both windows) and `/notes/:id`. List every differing route and confirm each difference is one of the listed polish items; every other route must match.
- [X] T078 [US3] Phase gate (see header).
- [X] T079 [US3] Collect the review evidence for Matt in `%TEMP%\roleradar-visual\review` (outside the repo): before images from the baseline and after images for `/postings`, `/insights` (90d and all), a wide-table note, in light and dark. Do not commit.
- [X] T080 **Boundary: Phase 4.** Report per the header, with the measured contrast ratios for every colour pair touched, the K4 status, and J1 to J7 listed for review. Wait for "go" before Phase 5.
- [ ] T081 [Matt] Visual review of J1 to J8 against the before and after evidence from T079 (J8 is checked in Phase 5, so mark it "pending Phase 5" if the Identity pages are not built yet). This is not a blocker for Phase 5; changes Matt asks for are collected as a follow-up list in `specs/009-ui-polish-theming/review-notes.md`.

**Phase 4 implementation notes** (what differed from the task text):

- Tests: `web/tests/e2e/polish.spec.ts` (22 tests), `web/tests/e2e/support/contrast.ts` with `web/tests/unit/contrast.test.ts`, and a `setZeroedLens` hook in `support/insights-stub.ts` (a "No data" rate). Contrast and hover reads wait for transitions to finish (the kit transitions colour even under reduced motion).
- T068 (J7): `.role-title-link` uses `--ds-primary-subtle-text` on hover and focus, not `--ds-primary`: `--ds-primary` is 4.05:1 on the dark hover row. It is used on the postings and applications lists. The undefined `--ds-text-primary` is gone (applications title link, timeline notes).
- T065, T071 (K4): the kit `Input` placeholder is 4.61:1 light and 3.29:1 dark, so K4 applies (recorded in `research.md` R11 and `spec.md`). A stopgap `.placeholder-muted` class on the Min Score input gives 4.80:1 and 6.01:1 without an inline style or a wrapper; remove it when K4 is vendored.
- T070 (J6): the tile grid rows are shared with the tiles through CSS subgrid. The tile boxes were already equal in height (grid items stretch); the visible difference was the cards inside them, so the assertion is on the cards, the link rows and the help rows.
- T072 (J2): `FunnelSection` takes children, so the postings help text and "See all postings in this window" sit inside the postings funnel section.
- T074 (J5): a prose child cap (`.markdown-body > :not(.markdown-table-scroll)`) replaces the cap on `.markdown-body`. Markdown headings are shifted down two levels (`#` is h3), which is why h4 to h6 come from `##` to `####` in the tests.
- T075 (J4): the run chart has no h3 above it (the chart carries the title); the empty state keeps its h3. `fullLabel` is "Run on <date>", so the kit table shows that text in the Run date column.
- T067: edited `insights-trends.test.tsx` (4 edits) and `insights-trends.spec.ts` (3 edits); `insights-page.test.tsx` needed none. Existing pins in `converted-styles.spec.ts` for the title links and the timeline notes colour were updated (the old pins asserted the inherited colour of the undefined token).
- T079: contact sheet at `%TEMP%\roleradar-visual\review-phase4\index.html` (sections J1 to J8 and K4), outside the repo.
- Ratchet ceiling unchanged at 8; no new K6.

**Checkpoint**: US3 items pass their automated checks; the aesthetic calls are with Matt.

---

## Phase 5: Identity sign-in pages (plan Phase 5; US4)

**Goal**: Login and Logout use the kit tokens and fonts, light and dark, no inline style or script, with a CSP, and behave exactly as today.

**Independent test**: Unit and Integration tests for headers, markup and assets; the existing login end-to-end test and the manual PKCE check stay valid.

### Tests first (run red, paste failing output)

- [ ] T082 [P] [US4] Extend `tests/RoleRadar.Tests.Unit/Containers/DockerIgnoreCoverageTests.cs`: add `.css` and `.js` to `BuildInputExtensions` and add a pinpoint test that `src/RoleRadar.Identity/wwwroot/css/identity.css` and `src/RoleRadar.Identity/wwwroot/js/theme.js` exist and are included by the `.dockerignore` rules. Create both files as empty placeholders only in T088 (the test must be red first because the files and the allow line do not exist). Run and paste red.
- [ ] T083 [P] [US4] New `tests/RoleRadar.Tests.Unit/Identity/AccountSecurityHeadersTests.cs`: given a configuration with `Clients:Web:RedirectUri` and `Clients:Web:PostLogoutRedirectUri` (read the keys from `src/RoleRadar.Identity/Configuration/IdentityServerConfig.cs`), the middleware adds the CSP from `contracts/identity-assets-and-csp.md` with `form-action 'self'` plus the distinct origins of every client redirect and post-logout URI, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` for paths under `/Account`, and adds nothing for `/connect/token`, `/.well-known/openid-configuration` and `/health`. Fictional origins only. Run and paste red (type missing).
- [ ] T084 [P] [US4] Extend `tests/RoleRadar.Tests.Unit/Identity/LoginModelRazorPageTests.cs` (or a sibling `AccountPageMarkupTests.cs` if that file does not render markup): rendered Login (password step and TOTP step, with and without validation errors) and Logout (signed in and signed out) contain no `<style` and no ` style=`, link `/ds/tokens.css`, `/ds/components/bundle.css`, `/css/identity.css` and `/js/theme.js` with `theme.js` before the first stylesheet, and keep every existing field name (`Input.Username`, `Input.Password`, `TwoFactorInput.Code`, `ReturnUrl`, `RequiresTwoFactor`, the anti-forgery token). Run and paste red.
- [ ] T085 [P] [US4] Extend `tests/RoleRadar.Tests.Integration/Containers/IdentityImageLoginPageTests.cs` (Docker; isolated container, random loopback port, throwaway volume, never the developer's): after the existing assertions, request `/ds/tokens.css`, `/ds/components/bundle.css`, `/ds/fonts/ibm-plex-sans-latin-wght-normal.woff2`, `/css/identity.css` and `/js/theme.js` and assert 200 and the right content type; assert the login HTML has no `<style` and no ` style=`; assert the `Content-Security-Policy` header on `/Account/Login` equals the contract string for the test configuration. Existing assertions stay unmodified. Run and paste red (or "Docker unavailable" if skipped, stated plainly).
- [ ] T086 [US4] Contract-test host (plan fallback applies): check whether `tests/RoleRadar.Tests.Contract/Identity/IdentityContractTestServer.cs` can serve `wwwroot` from the project folder. Record the finding. If it can, add static-file and header assertions in a new `tests/RoleRadar.Tests.Contract/Identity/IdentityStaticAssetsContractTests.cs` (red first). If it cannot, do not change the host; static-file checks stay in T085 and header checks stay in T083.
- [ ] T087 [US4] Add a Playwright check `web/tests/e2e/identity-signin.spec.ts` (guarded `test`, `support/axe.ts`) that starts the locally built Identity image on a free loopback port with an isolated throwaway volume and fictional owner seed (same pattern as `IdentityImageLoginPageTests`; skipped with a stated reason when Docker is unavailable), loads `/Account/Login` with `colorScheme` light and dark, and asserts: zero CSP violations, computed `font-family` of the heading starts with `IBM Plex Serif` and of body text with `IBM Plex Sans`, the primary button background equals the kit primary token for that theme, the shared reduced-motion axe check has no serious or critical violations, and `data-theme` equals the emulated scheme. Run and paste red. Never use real-stack ports.

### Implementation

- [ ] T088 [US4] Create `src/RoleRadar.Identity/wwwroot/css/identity.css` (layout of the centred sign-in card, form spacing, error alert, buttons, links; kit tokens only; no raw hex or px) and `src/RoleRadar.Identity/wwwroot/js/theme.js` (blocking; sets `data-theme` from `matchMedia('(prefers-color-scheme: dark)')` and updates on change; no cookie, no storage; no inline use). Add `!src/RoleRadar.Identity/wwwroot/**` to `.dockerignore`. Add `src/RoleRadar.Identity/wwwroot/ds/` to `.gitignore`.
- [ ] T089 [US4] Edit `src/RoleRadar.Identity/RoleRadar.Identity.csproj`: a target that runs before build and publish, copies `web/app/ds/tokens.css`, `web/app/ds/components/bundle.css` and `web/app/ds/fonts/*.woff2` into `wwwroot/ds/` keeping the relative layout, and fails with a clear error if `web/app/ds/tokens.css` is missing. Edit `deploy/Dockerfile.identity` to `COPY web/app/ds/ web/app/ds/` in the build stage before restore. Confirm `.dockerignore` already allows `web/**`.
- [ ] T090 [US4] Create `src/RoleRadar.Identity/Services/AccountSecurityHeaders.cs` (middleware plus an extension method) and wire it and `UseStaticFiles()` in `src/RoleRadar.Identity/Program.cs` before `UseRouting()`. The origins come from the same configuration keys `IdentityServerConfig.GetClients` reads. Do not touch `/connect/*` or `/health`.
- [ ] T091 [US4] Rewrite `src/RoleRadar.Identity/Pages/Account/Login.cshtml` and `Logout.cshtml`: remove both `<style>` blocks and the `style="margin: 0;"` attribute, link the four assets, rename classes only where needed. Do not change form fields, names, hidden inputs, anti-forgery, handlers or `.cshtml.cs` files.
- [ ] T092 [US4] Run T082 to T087 and paste green output (state plainly which could not run). Run the existing Identity tests unmodified: `tests/RoleRadar.Tests.Contract/Identity/*`, `tests/RoleRadar.Tests.Unit/Identity/LockoutAndRateLimitingTests.cs`, and the real-stack login end-to-end test only through `pwsh ./scripts/test-e2e-real.ps1`, which builds its own isolated Mongo, Identity, API and Web images on a throwaway network and cleans up; never against the developer's stack. Paste its summary, or say plainly that it could not run and leave it to T094 and T095.
- [ ] T093 [US4] Draft `docs/adr/0034-identity-serves-kit-assets-and-csp.md` (MADR, status Proposed, short): context (unthemed inline-styled pages, no CSP, earlier ignore-file defect), decision (build-time copy from `web/app/ds`, committed `identity.css` and `theme.js`, `/Account` CSP with `form-action` derived from registered clients, system theme only), consequences, alternatives (second committed copy, cross-origin fetch, CDN). Add it to `docs/adr/README.md`.
- [ ] T094 [US4] Phase gate (see header) plus `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/review-gate.ps1` and `pwsh ./scripts/test-images.ps1` if it covers the Identity image. Report numbers from the output.
- [ ] T095 [Matt] Real browser sign-in on the compose stack with the new Identity CSP and `form-action`, as `specs/005a-api-and-login/quickstart.md` section 5a describes: PKCE authorize, password, TOTP, token exchange, then sign out and the post-logout redirect. Check in DevTools that the sign-in post and its redirect chain produce no CSP violation, that the page is themed in a light and a dark system setting, and that the web app's own sign-in and sign-out still work. Also accept or amend ADR 0034. **Phase 5 is not done until this passes.**
- [ ] T096 **Boundary: Phase 5.** Report per the header. Do not close the phase until T095 is recorded as passed. J8 goes to Matt's review here.

**Checkpoint**: US4 holds in tests and in a real sign-in.

---

## Phase 6: CSP flip, only if K1 is vendored (plan Phase 6; US1, last task)

**Goal**: remove `style-src-attr 'unsafe-inline'` from the web CSP, or record exactly what blocks it.

### Precondition

- [ ] T097 [P] [US1] New `web/scripts/kit-style-emitters.mjs` that scans `web/app/ds/components/src/**/*.jsx` for `style=` and prints per-file counts and the total as JSON; and new `web/tests/architecture/kit-style-emitters.test.ts` that runs the same scan and reports the total. Run it and paste the output (expect 150 in 27 files at the start of the phase).
- [ ] T098 [US1] Decide the path from T097: total 0 means K1 is vendored (go to Path B); any other total means Path A.

### Path A: K1 not vendored (the phase ends here)

- [ ] T099 [US1] Write `specs/009-ui-polish-theming/kit-style-emitters.md` from the T097 output and the end-of-Phase-3 census: per kit file the count, which kit component it is, which app routes render it (from `census-end-phase3.json`), and the unchanged K1 wording (integer-percent classes 0 to 100 in `bundle.css`, no post-hydration script). State that `web/server.ts` is unchanged and that a small follow-up feature picks the flip up after Matt vendors K1.
- [ ] T100 [US1] Update `specs/009-ui-polish-theming/spec.md` SC-003 with the recorded count and file list reference. Run `pre-commit run --all-files` and the LF check. No `web/server.ts` change.
- [ ] T101 **Boundary: Phase 6, Path A.** Report: the blocked state, the exact list, and that the feature is otherwise complete. Stop.

### Path B: K1 vendored

- [ ] T102 [Matt] Publish the kit version that satisfies K1 and vendor it using the `web/app/ds/SYNC.md` procedure (copy-only, never hand-edited), updating `SYNC.md` hashes so `web/tests/architecture/ds-sync.test.ts` passes. Then tell the agent to continue.
- [ ] T103 [US1] Re-run the Phase 1 visual baseline comparison against the new kit (`ROLERADAR_VISUAL_DIR` set, no `--update-snapshots`) for every route in both themes. Differences of one percent in chart sizes are expected from rounding; every other difference is reported and stops the phase until Matt rules on it. Re-run `kit-style-emitters` and paste a total of 0.
- [ ] T104 [US1] Tests first: edit `web/tests/unit/server.test.ts` to expect a CSP without `style-src-attr` and run it red; add to `web/tests/e2e/csp-style-attribute.spec.ts` an assertion that the response header lacks `style-src-attr 'unsafe-inline'`; edit the kit `EmptyState` check so it asserts computed styles apply before hydration through classes (no `style` attribute on the element). Paste the red output.
- [ ] T105 [US1] Remove the `style-src-attr 'unsafe-inline';` clause from `applySecurityHeaders` in `web/server.ts`. Update `contracts/csp-policies.md` to "done".
- [ ] T106 [US1] Run the whole Playwright suite with the guard on every route in light and dark; paste the pass count and the count of CSP violations (expect 0). Any violation stops the phase and is reported with its route and element.
- [ ] T107 [US1] Phase gate (see header) plus `pwsh ./scripts/review-gate.ps1` and the Phase 5 Identity checks (`web/tests/e2e/identity-signin.spec.ts` is unaffected).
- [ ] T108 **Boundary: Phase 6, Path B.** Report per the header. J-items: none.

---

## Dependencies and Execution Order

- Phase 1 first. Phase 2 before Phase 3 (the conversion is judged on one theme implementation). Phase 3 slices run in order a, b, c, d, e; c, d and e are optional stop points. Phase 4 needs Phase 3 only for the files it shares (`postings._index.tsx`, `insights.tsx`, `app.css`), so it starts after T060. Phase 5 is independent of Phases 3 and 4 except `app.css` is not touched, so it may be started in parallel after Phase 1 if Matt wants, but the plan orders it after Phase 4 and T081 does not block it. Phase 6 last.
- Within a phase: tests (red) before implementation; implementation before the baseline comparison; comparison before the gate; gate before the Boundary.
- [P] tasks within one test block touch different files.

### Parallel examples

- Phase 1: T002, T003 (and T010) in parallel; T009 once T003 exists.
- Phase 2: T016, T017, T018.
- Phase 4: T061 to T064 and T067.
- Phase 5: T082 to T085.

## Implementation Strategy

- **Smallest useful slice**: Phases 1 and 2 plus Slice 3a. They add the guards, remove the duplication, and prove the conversion method on 21 styles.
- **Stop points**: Slices 3c, 3d and 3e are explicit boundaries; the ratchet makes the remaining count visible at each.
- **Blocked item**: Phase 6 depends on K1, owned by Matt. The feature is complete without it except SC-003 (recorded as blocked).
- **Remaining kit requests**: K1 (blocks Phase 6 only), K4 (only if T065 finds the kit input is the cause).
