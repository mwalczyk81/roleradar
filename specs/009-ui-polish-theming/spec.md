# Feature Specification: UI Polish and Theming

**Feature Branch**: `009-ui-polish-theming`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Feature 009: UI polish and theming (branch 009-ui-polish-theming from main at 317f1fc). Frontend-only polish. No API, MCP, Domain or data changes, no new pages, no new behaviour. Inline styles to classes then a stricter CSP, theme logic consolidation, a visual polish pass, and themed Identity sign-in pages."

## Background

RoleRadar should look like the owner's own site (mattwalczyk.com): bronze accent, stone neutrals, IBM Plex Sans, Serif and Mono. The web app already loads the vendored design kit (`web/app/ds`), but three things still hold it back, found by reading the code at `317f1fc`:

- **Inline styles.** The web app writes 203 inline `style` props in 17 files (counted, not estimated; the brief said about 160). The CSP still allows `style-src-attr 'unsafe-inline'` because of them.
- **Duplicated theme logic.** `root.tsx` and `use-theme.ts` each implement the same cookie, media-query and attribute handling.
- **Unthemed sign-in.** The Identity Login and Logout pages carry their own `<style>` block in the old slate and sky-blue palette, and Login has one inline `style` attribute. Identity sends no CSP today.

Reading the vendored kit also changed one assumption in the brief, recorded here because it decides what this feature can finish on its own.

1. **The kit emits inline styles itself.** Exactly 150 `style={` props sit in 27 kit source files (`DataTable` 17, `chartParts` 15, `ColumnChart` 15, `Skeleton` 11, `Toolbar` 11, `Modal` 10, `StatTile` 10, `BarChart` 9, and 19 more files with 1 to 7 each). Server rendering writes them as `style` attributes. App code cannot change that, and `web/app/ds` is copy-only. Dropping `style-src-attr 'unsafe-inline'` from the CSP will break every kit component unless the kit stops emitting them (kit request K1). K1 is not assumed: the conversion of the app's own styles ships first, and the CSP step waits for it (see Clarifications).

## Clarifications

### Session 2026-11-05

- Q: If K1 is not published, ship US1 stopping at converted app styles, or hold it? → A: Ship in two steps. Convert the app's own styles first. Removing `style-src-attr 'unsafe-inline'` is the last task of US1 and happens only if K1 is vendored by then; otherwise it is recorded as blocked and becomes a small follow-up feature.
- Q: How are the kit's dynamic chart values to be styled without inline styles? → A: Not by script after hydration (server-rendered charts would paint empty). K1 asks for integer-percent classes, 0 to 100, generated in `bundle.css`, with values rounded to whole percents.
- Q: Is Fraunces wanted as the display face? → A: No. IBM Plex Serif stays; the kit's identity pass dropped Fraunces on purpose. Kit request K2 is withdrawn and FR-040 to FR-042 now require only that the shipped identity applies everywhere, Identity pages included.
- Q (plan, 2026-11-05): Can the ColumnChart be made quiet? → A: No quiet mode and no K5. FR-024 and scenario 9 are relaxed to a small, readable chart with labels and values; judged in the owner's visual review (J4 returns to the judgement table).
- Q: Sparkline or column chart for search-run yield? → A: The kit `ColumnChart`, which has a table view. Kit requests K3 and K5 are withdrawn. K4 stays only if the placeholder measurement fails AA.
- Accepted as written: J8 (Identity follows the system colour scheme only), copying the kit CSS and fonts into the Identity image at build time, and keeping before and after screenshots uncommitted as working evidence. J1 to J3 and J5 to J7 stay judgement calls for the owner's visual review.

## Scope

In scope: the four user stories below, in `web/` and the Identity host (`src/RoleRadar.Identity`), plus the tests and deploy files they need.

Out of scope (belongs to later features or is excluded outright): README, screenshots for docs, the demo command, SECURITY.md, CONTRIBUTING, CHANGELOG, the hosts-file requirement, enum generation, the CLI transition command, passkeys. Also out of scope: any API, MCP, Domain, Application, Infrastructure or data change, any new page, any new behaviour, and any edit to `web/app/ds`.

Existing tests must pass unmodified, except a test that asserts the exact markup or styling being changed. Each such edit is listed in the plan with its reason (see FR-005). Known candidates: the CSP spec's checks that the vendored `EmptyState` has inline computed styles, and the unit tests for `root.tsx` and `use-theme.ts`.

## Kit Requests

Anything below needs a kit version published by the owner and re-vendored. Until then the dependent requirement is blocked, not worked around (no local copies of kit components).

| ID | Change wanted | Blocks |
|----|---------------|--------|
| K1 | Every kit component renders its static styling through classes in `bundle.css` (using the kit's own `--ds-*` tokens) instead of `style` attributes. Dynamic values (bar widths, column heights, chart point positions) MUST NOT be set by script after hydration, because server-rendered charts would paint empty until then. Instead `bundle.css` generates integer-percent classes for 0 to 100 (for example `.ds-w-37`, `.ds-h-62`, `.ds-bottom-50`), and components round each value to a whole percent and pick the class during server rendering. Same props, same look, to within one percent. | FR-004 (CSP tightening) |
| K4 | Only if the measured contrast of the kit `Input` placeholder fails WCAG AA in either theme: a placeholder colour token (for example `--ds-text-placeholder`) that passes AA against `--ds-surface` and `--ds-surface-raised`, used by `Input`, `Select` and `Textarea`. | FR-020 |

K1 is large and is the main risk to the schedule; K4 is small and conditional. IDs K2, K3 and K5 were withdrawn in clarification and are not reused. The owner publishes the kit; this feature never edits `web/app/ds`. Until K1 is vendored, the CSP step is blocked and becomes a small follow-up feature (see Clarifications).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No inline styles, then a stricter CSP (Priority: P1)

The owner gets a web app whose own markup carries no inline `style` attributes, so the CSP can stop allowing them. The look does not change at all.

**Why this priority**: It removes the one loose directive in the CSP and is mechanical, so it is the safest first slice. It also finds out early whether the kit blocks the final step.

**Independent Test**: Convert one route, compare before and after screenshots in light and dark, then repeat per route. The CSP change is the last step and is tested by a full-suite run that fails on any CSP violation.

**Real count (at `317f1fc`)**: 203 `style={{ ... }}` props, none of them computed from data. Per file: `companies.$id.tsx` 40, `postings.$id.tsx` 37, `applications.$id.tsx` 32, `CreateApplicationDialog.tsx` 24, `TransitionPostingDialog.tsx` 17, `applications._index.tsx` 9, `notes.$id.tsx` 8, `notes._index.tsx` 6, `criteria.tsx` 5, `postings._index.tsx` 5, `auth.callback.tsx` 5, `companies._index.tsx` 4, `search-runs.tsx` 3, `service-unavailable.tsx` 3, `auth.logout.tsx` 2, `not-authorized.tsx` 2, `root.tsx` 1 (the `ErrorBoundary` body). The plan re-counts before starting and the spec's numbers are replaced by the re-count if they differ.

**Acceptance Scenarios**:

1. **Given** a route with inline styles, **When** its styles are moved to classes in `app.css` using `--ds-*` tokens, **Then** the before and after screenshots of that route match in light and in dark, using fictional data only.
2. **Given** all 17 files are converted, **When** the full browser suite runs with a console listener, **Then** it fails on any CSP violation on every route in both themes, and none occurs.
3. **Given** every conversion is done and the kit supports it (K1), **When** `style-src-attr 'unsafe-inline'` is removed from the CSP in `web/server.ts`, **Then** the browser suite still passes and no route shows a CSP violation.
4. **Given** the app's own `style` attributes are all gone but the kit still emits them, **When** the CSP is checked, **Then** the feature stops at scenario 2, keeps the directive, and records the remaining kit-emitted attributes as blocked on K1.

**Dynamic style exceptions in app code**: none are expected, because all 203 values are static (spacing, `whiteSpace: nowrap`, fixed widths such as `12rem` and `7rem`). Any value found to be computed is listed here with the reason and set through a CSS custom property or a data attribute. The kit's dynamic values (chart heights, positions) are covered by K1.

---

### User Story 2 - One theme implementation (Priority: P1)

The theme logic exists once. The first paint is in the right theme, the cookie and storage behaviour is unchanged, and the owner can still toggle.

**Why this priority**: Two copies of the same logic can drift, and both US1 and US3 touch the theme-dependent screens. It is small and independent.

**Independent Test**: Unit tests on the single implementation, plus a browser check that the first server-rendered frame carries the right `data-theme` before any script runs.

**Acceptance Scenarios**:

1. **Given** the `roleradar_theme` cookie is `dark` (or `light`), **When** any page is requested, **Then** the server-rendered `<html>` already has that `data-theme` and no later script changes it (no flash).
2. **Given** no cookie, **When** the page loads, **Then** the theme follows the system preference on first paint and keeps following it live until the owner picks one.
3. **Given** the owner toggles the theme, **When** the toggle completes, **Then** the cookie is written with the same name, path, SameSite, max-age and Secure-on-HTTPS rules as today, and the `data-theme` attribute updates.
4. **Given** the change, **When** the code is read, **Then** the cookie-parsing, media-query and attribute logic appears in one module that both `root.tsx` and `useTheme` use.

---

### User Story 3 - Visual polish pass (Priority: P2)

Small, specific fixes that make the app read as one design. Each has a check or is marked for the owner's visual review.

**Why this priority**: Pure appearance, no risk to behaviour, and the placeholder item depends on K4 only if the kit is the cause.

**Independent Test**: Each item below has its own check and can ship alone.

**Acceptance Scenarios**:

1. **Given** a Postings list, **When** a role-title link is shown, **Then** it uses the body text colour, turns bronze on hover and focus, and keeps a visible focus ring. Check: computed colour at rest equals the text token, computed colour on hover equals the bronze token, and both pairs meet 4.5:1 against the row background in both themes.
2. **Given** the Min Score filter input, **When** it shows its placeholder, **Then** the placeholder meets 4.5:1 in both themes. Check: computed contrast ratio. If the kit `Input` causes the failure, this is K4.
3. **Given** the Insights tiles, **When** a tile has two or more drill-down links, **Then** they are visually quieter (no persistent underline on every link; underline on hover and focus; a clear affordance remains). Judgement call: the exact treatment is for the owner's visual review. Check that can be automated: each link keeps a 4.5:1 contrast and a visible focus indicator, and axe passes.
4. **Given** a row of Insights tiles including one with "No data", **When** the page renders at desktop width, **Then** all tiles in a grid row have the same height. Check: Playwright asserts equal bounding-box heights per row.
5. **Given** the "See all postings in this window" link, **When** Insights renders, **Then** it sits with the funnel it belongs to (or near the window control), not alone at the end of "How these are calculated". Judgement call on the final spot: listed for the owner's review. Check: the link is a descendant of the chosen section and exists exactly once.
6. **Given** Insights, **When** the window control (Last 90 days, All time) is reviewed, **Then** its position is either confirmed or moved, and the decision is recorded. Judgement call, owner's review. Check: it remains a `nav` with an accessible name and `aria-current` on the active link.
7. **Given** a note whose Markdown has a wide table, **When** the note renders, **Then** prose stays at the reading width while the table's scroll region uses the full card width and is not clipped. Check: Playwright asserts the scroll region's width equals the card content width, is greater than the prose column width, and the page has no horizontal scroll.
8. **Given** a note with h4, h5 and h6 headings, **When** it renders, **Then** each is visibly distinct from body text and from the others where practical (size, weight, case or spacing). Check: computed font-size or weight or text-transform differs from `<p>` for every level, and no two adjacent levels have identical computed style. Whether h5 and h6 should differ from each other is a judgement call for the owner.
9. **Given** the Insights search-run yield, **When** it renders, **Then** it shows the latest run's new-postings count and the number of points as text, followed by a small, readable kit `ColumnChart` (value labels and axis welcome; reduced height and `framed={false}` allowed) with the kit's table view available. Check: the text is in the DOM, the chart has an accessible name, the table view lists every run, and axe passes. Whether the result is small and readable enough is a judgement call for the owner's visual review (J4).
10. **Given** any colour pair this story touches, **When** checked in light and dark, **Then** it meets WCAG AA, and the shared reduced-motion axe helper reports no serious or critical violation on every touched page.

---

### User Story 4 - Themed Identity sign-in pages (Priority: P2)

The Login and Logout pages look like the rest of the app, light and dark, with no inline styles, and behave exactly as before.

**Why this priority**: It is the first screen the owner sees and the last unthemed one, but it sits in a different host and needs a delivery decision.

**Independent Test**: Load Login and Logout from the Identity host, check both themes, check the response CSP, then run the existing login end-to-end test and the manual PKCE check from the quickstart unchanged.

**Acceptance Scenarios**:

1. **Given** the Login page, **When** it loads in light and in dark system themes, **Then** it uses the same tokens, display face (IBM Plex Serif) and body face (IBM Plex Sans) as the web app, and every text and control colour pair meets WCAG AA.
2. **Given** the Login and Logout responses, **When** their markup and headers are inspected, **Then** there is no `<style>` block and no `style` attribute, and the response carries a CSP that allows no inline style. Check: a test requests each page and asserts both, and a browser check shows no CSP violation.
3. **Given** a correct password and TOTP code, **When** the owner signs in, **Then** the flow, redirects, error messages, rate limiting and cookies behave exactly as today. The existing login end-to-end test passes unmodified, except markup-specific selectors listed in FR-005.
4. **Given** a built Identity image, **When** it is inspected, **Then** the pages, the stylesheet and the fonts are all present in it (guards against the earlier defect where the Docker ignore rules dropped `*.cshtml`).

---

### Edge Cases

- A route's inline style is shared by two elements with different needs: split into two classes, do not merge them.
- A style value is computed from data after all: it is recorded as an exception, not forced into a class.
- The theme cookie holds a value other than `light` or `dark`: it is ignored, as today.
- JavaScript is disabled: the first paint still has the correct `data-theme` from the cookie. With no cookie and no script the page renders in the light theme, because the kit selects its dark tokens only by the `data-theme` attribute (it has no `prefers-color-scheme` rule). This is today's behaviour and is kept.
- The Identity host is opened on a different host name from the web app: it follows the system preference and does not read the web app's cookie (see Assumptions).
- A kit version arrives mid-feature: re-vendoring is a separate, copy-only step with its own check, never mixed with a conversion commit.
- Wide table in a note, narrow viewport: the scroll region scrolls, the page does not.
- Placeholder contrast passes on the page but fails on a disabled input: disabled controls are exempt from WCAG contrast, and the check says so.

## Requirements *(mandatory)*

### Functional Requirements

**Cross-cutting**

- **FR-001**: The feature MUST change only the web app, the Identity host's sign-in pages and their delivery, and tests. No API, MCP, Domain, Application, Infrastructure or data change, no new page, no new behaviour.
- **FR-002**: `web/app/ds` MUST NOT be edited. Components MUST be imported only through `web/app/ui.ts`. No local copy of a kit component may stand in for a missing kit feature.
- **FR-003**: Every kit change needed MUST be written as a kit request in this spec with the exact change wanted (see Kit Requests). Work that depends on a kit request MUST be marked blocked until the kit version is vendored.
- **FR-004**: `style-src-attr 'unsafe-inline'` MUST be removed from the CSP in `web/server.ts` as the last task of US1, and only if K1 is vendored by then. The conversion of the app's own inline styles MUST NOT wait for the kit. If K1 is not vendored, the removal is recorded as blocked and becomes a small follow-up feature.
- **FR-005**: Existing tests MUST pass unmodified except a test that asserts the exact markup or styling being changed. Every such edit MUST be listed in the plan with the test, the change and the reason.
- **FR-006**: All screenshots, fixtures and tests MUST use fictional data only. No check may run against the developer's running stack or database.
- **FR-007**: Every colour pair touched MUST meet WCAG AA in light and dark. Accessibility checks MUST use the shared reduced-motion axe helper.
- **FR-008**: Files MUST use LF line endings.
- **FR-009**: Any judgement call MUST be labelled as one, with a check that can be automated (contrast, layout assertion, axe) and the aesthetic part listed for the owner's visual review.

**US1 - inline styles and CSP**

- **FR-010**: All 203 inline `style` props in the 17 listed files MUST be replaced by classes in `app.css` that use `--ds-*` tokens, route by route, with no visual change.
- **FR-011**: Each converted route MUST have a before and after screenshot comparison in light and dark, using fictional data, as the check that nothing changed. The comparison images MUST NOT be committed to the repository unless the plan decides otherwise.
- **FR-012**: A value that is genuinely dynamic MUST be set through a CSS custom property or a data attribute, and MUST be listed in this spec as an exception with its reason.
- **FR-013**: The browser suite MUST attach a console listener that fails the test on any CSP violation, on every route, in light and dark.
- **FR-014**: After FR-004, the same suite MUST pass with the stricter CSP.

**US2 - theme**

- **FR-015**: Theme reading, cookie writing, system-preference handling and `data-theme` application MUST live in one module used by `root.tsx` and `useTheme`.
- **FR-016**: The server-rendered `<html>` MUST carry the correct `data-theme` when the cookie is present, with no change after hydration.
- **FR-017**: Cookie name `roleradar_theme`, values `light` and `dark`, path, SameSite, max-age and the Secure-on-HTTPS rule MUST be unchanged.
- **FR-018**: The no-flash inline script MUST keep working under the existing nonce-based CSP.
- **FR-019**: The single implementation MUST have unit tests (cookie present, absent, invalid, toggle, live system change) and one browser check for first paint.

**US3 - polish**

- **FR-020**: The Min Score placeholder MUST meet 4.5:1 in both themes (K4 if the kit is the cause).
- **FR-021**: Role-title links MUST use the text colour at rest and bronze on hover and focus, with a visible focus ring, in the shared link style, not per-route.
- **FR-022**: Insights drill-down links MUST be quieter than today, keep their contrast and focus indicator, and stay distinguishable as links without relying on colour alone (judgement call, FR-009).
- **FR-023**: Insights tiles in a row MUST have equal heights, including a "No data" tile.
- **FR-024**: The search-run yield MUST show, as text, the latest run's new postings and the point count, followed by a small, readable kit `ColumnChart` (labels and values welcome; reduced height and `framed={false}` allowed) using its own table view. The local "Table view of search runs" toggle and table, and the `Sparkline` usage on that page, MUST be retired in favour of it. No kit request: there is no quiet mode and none is wanted.
- **FR-025**: "See all postings in this window" MUST be placed with the content it relates to, and the time-window control position MUST be reviewed and the decision recorded.
- **FR-026**: In Markdown notes, tables MUST scroll in a region as wide as the card content, prose MUST stay at reading width, and the page MUST NOT scroll horizontally.
- **FR-027**: In Markdown notes, h4 to h6 MUST be visually distinct from body text.
- **FR-028**: Markdown output MUST remain untrusted text rendered as React text nodes, with the existing link rules unchanged.

**US4 - Identity**

- **FR-030**: Login and Logout MUST use the web app's tokens and fonts in light and dark, selected by the system colour scheme.
- **FR-031**: Login and Logout MUST contain no `<style>` block and no `style` attribute. Their stylesheet MUST be a separate file served by the Identity host.
- **FR-032**: Identity MUST send a CSP on these pages that forbids inline style and script, and allows only its own stylesheet, fonts and one same-origin script file (`theme.js`); `form-action` allows only the registered web origins. The font-src MUST match where the fonts are served from.
- **FR-033**: The kit stylesheet and fonts MUST reach the Identity image by copying them from the vendored kit at build time (single source of truth), not by keeping a second hand-edited copy. A test MUST fail if the Identity output lacks the stylesheet, the fonts, or the `*.cshtml` pages, and the Docker ignore rules MUST be checked for the new paths.
- **FR-034**: The login and TOTP flows MUST behave exactly as today. The existing login end-to-end test and the manual PKCE check in the quickstart MUST remain valid.

**Owner-site identity**

- **FR-040**: The shipped kit identity (bronze ramp as the primary accent, stone neutrals, IBM Plex Sans for text, IBM Plex Serif for display, IBM Plex Mono for code) MUST apply on every web page and on the Identity Login and Logout pages, in light and dark. IBM Plex Serif stays the display face; Fraunces is not used.
- **FR-041**: No page may use a colour, font family or neutral that is not a kit token (no raw hex colours, no system-font stack, no slate or sky-blue values left from the old Identity pages).
- **FR-042**: The identity check MUST be computed-style assertions on the heading and body font families and on the primary colour of a link or button, on at least one web page and on both Identity pages, in both themes, plus the owner's visual review; "looks like the site" is not an automated criterion.

### Key Entities

None. No data is created, changed or read differently.

## Judgement Calls (for the owner's visual review)

| # | Call | Default taken | Automated check that remains |
|---|------|---------------|------------------------------|
| J1 | How the Insights drill-down links are quietened | Underline only on hover and focus, secondary text colour at rest | Contrast, focus indicator, axe |
| J2 | Where "See all postings in this window" goes | Beside the postings funnel | Exactly one instance, inside the chosen section |
| J3 | Whether the window control moves | Keep it at the top, confirm | `nav` name and `aria-current` |
| J4 | Whether the small `ColumnChart` for search-run yield is small and readable enough | Unframed, labels on, height reduced from the kit default of 180 (about 120) | Text for latest run and point count, accessible name, table view lists every run, axe |
| J5 | How h4, h5 and h6 differ from body text and each other | Size steps plus weight and small caps on h6 | Computed style differs from `<p>` |
| J6 | Row height equalisation method | Same row height for every tile in a row | Equal bounding-box heights |
| J7 | Bronze hover colour on role titles | Kit bronze token with AA contrast | 4.5:1 in both themes |
| J8 | Whether Identity follows system theme only | Yes; no cookie read across hosts | Both schemes render and pass axe |

## Assumptions

- The brief's "about 160" is replaced by the counted 203; the plan re-counts at the start.
- The kit's identity (bronze, stone, IBM Plex) is already correct in the vendored copy; no font change is requested.
- Identity runs on a different host name from the web app, so it cannot read the web app's `roleradar_theme` cookie; it follows the system colour scheme through one small same-origin script file (no inline script) that sets `data-theme` before first paint, because the kit has no `prefers-color-scheme` rule. It reads no cookie.
- Screenshot comparisons are produced during the work from fictional fixtures and are working evidence, not committed assets.
- Copying the kit CSS and fonts into Identity at build time uses the existing `web/**` allowance in `.dockerignore`, so no new ignore exception is expected; this is checked, not assumed (FR-033).
- Identity gets a new static-file and CSP step in its pipeline; this is configuration, not new behaviour.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero inline `style` attributes are written by RoleRadar's own web markup: 203 reduced to 0 across 17 files, with the before and after screenshot comparison unchanged in both themes on every route.
- **SC-002**: The browser suite finishes with zero CSP violations on every route in light and dark.
- **SC-003**: The CSP no longer contains `style-src-attr 'unsafe-inline'` once K1 is vendored; if K1 is not available, the spec records the count of kit-emitted attributes still blocking it.
- **SC-004**: Theme logic is defined in exactly one module, and a first-paint check shows the right `data-theme` in the server HTML for both cookie values.
- **SC-005**: Every colour pair touched passes WCAG AA (4.5:1 for text) in light and dark, and the shared axe helper reports zero serious or critical violations on each touched page.
- **SC-006**: Insights tiles in a row have equal heights on desktop, and a wide note table is not clipped while the page has no horizontal scroll.
- **SC-007**: Login and Logout responses contain zero `<style>` blocks and zero `style` attributes and carry a CSP without inline-style allowance, and the existing login end-to-end test passes.
- **SC-008**: A built Identity image contains the pages, stylesheet and fonts, verified by a test.
- **SC-009**: Every judgement call in the table above is either closed by the owner's review or carried forward with its default.
