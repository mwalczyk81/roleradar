# Feature Specification: Insights Dashboard

**Feature Branch**: `007-insights-dashboard`

**Created**: 2026-11-04

**Status**: Implemented (tasks Phases 1 to 5); ADRs 0029 to 0032 await acceptance (T095)

**Input**: User description: "A read-only Insights page showing the job-search funnel and rates, built on the 006 read model, plus Markdown rendering of note bodies as a separate small story."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Funnel and rates (Priority: P1)

The owner opens the Insights page and sees how the search is going: two funnels, each over one cohort (postings first seen in the window: leads, reviewed, applied-to; applications applied in the window: applied, responded, interviewed, offers), plus stat tiles for application rate, screen-out rate, interview rate, rejection rate, offer rate, ghost rate and the open count. The default window is the last 90 days (90 whole UTC days ending on and including the as-of date, so today), by application date for applications and by first-seen date for postings, with a toggle for all time. Every percentage is shown with its numerator and denominator ("x of y"), and a denominator under 10 is labeled "small sample".

**Why this priority**: This is the reason the feature exists. Without trustworthy, explained numbers the owner falls back to a spreadsheet.

**Independent Test**: Load a fictional seed database with known counts, open the Insights page, and check every funnel bar, rate, numerator and denominator against hand-computed values from the metric definitions below.

**Acceptance Scenarios**:

1. **Given** a seed database with known postings, applications and events, **When** the owner opens Insights, **Then** both funnels show the expected counts for the last 90 days, within each funnel every stage is at most the stage before it, and each rate shows "x of y" matching the metric definitions.
2. **Given** the owner is on the 90-day view, **When** they switch to all time, **Then** every count, rate and denominator recomputes for all time and the active window is clearly labeled.
3. **Given** a rate whose denominator is under 10, **When** the page renders, **Then** the value is shown with "small sample" next to it.
4. **Given** a rate whose denominator is 0, **When** the page renders, **Then** the tile shows "no data" and "0 of 0", never a percentage, NaN or an error.
5. **Given** a posting that is closed with no application, **When** reviewed postings are counted, **Then** it counts as expired and is excluded from reviewed.
6. **Given** an application still in applied status, with no response event, older than 30 days, **When** ghosted is counted, **Then** it is ghosted; **and** an application younger than 30 days or with any response event is not.

---

### User Story 2 - Drill-down that agrees with the list (Priority: P1)

Every count on the Insights page is a link to the postings or applications list page, pre-filtered with the same criteria that produced the count. The number on the dashboard and the number of rows in the list always agree.

**Why this priority**: Equal in weight to Story 1. A number the owner cannot verify is not trustworthy, and trust is the stated goal.

**Independent Test**: For every linked count on the page, follow the link and compare the list's total row count to the dashboard number, using the fictional seed database. An automated test performs this comparison for every funnel bar and counted tile in both windows.

**Acceptance Scenarios**:

1. **Given** any funnel bar or tile count, **When** the owner follows its link, **Then** the list page opens with the equivalent filter applied and shows exactly that many rows.
2. **Given** the 90-day window is active, **When** the owner follows a link, **Then** the list filter includes the same date window.
3. **Given** a count that cannot be expressed as a list filter, **When** the page is designed, **Then** that count is not shown (or the list gains the filter), so no unlinked, unverifiable count exists.

---

### User Story 3 - Trends (Priority: P2)

The owner sees applications per week as a column chart and search-run yield (new postings found per search run) as a sparkline, over the active window.

**Why this priority**: Useful context on pace and on whether searches are still finding new roles, but the funnel stands without it.

**Independent Test**: Seed applications across known weeks and search runs with known yields; verify bar heights and sparkline points against the data, including weeks with zero applications.

**Acceptance Scenarios**:

1. **Given** applications spread over several weeks, **When** Insights renders, **Then** each week in the window has a column, including empty weeks shown as zero.
2. **Given** several search runs, **When** Insights renders, **Then** the sparkline shows new postings per run in chronological order.
3. **Given** a chart, **When** the owner chooses the table view, **Then** the same data appears as an accessible table.

---

### User Story 4 - Response time (Priority: P2)

The owner sees the median number of days from applying to the first response, with its sample size.

**Why this priority**: Answers "how long should I wait before calling it ghosted", but depends on the same data as Story 1.

**Independent Test**: Seed applications with known applied and first-response dates, including follow-up and withdrawn events that must not count; verify the median.

**Acceptance Scenarios**:

1. **Given** applications with first responses, **When** Insights renders, **Then** the tile shows the median (not the mean) in days and "n responses".
2. **Given** an application whose only later events are follow-up or withdrawn, **When** response time is computed, **Then** it is excluded.
3. **Given** no application has a response, **When** the page renders, **Then** the tile shows "no data".

---

### User Story 5 - Notes render as Markdown (Priority: P3)

On the note detail page, the note body is displayed as formatted Markdown (headings, lists, tables, task lists, strikethrough, line breaks, links) instead of plain text. Content is never trusted: no raw HTML, no images, no remote loading.

**Why this priority**: A small quality-of-life improvement, fully independent of the Insights page, so it can ship alone.

**Independent Test**: Open a fictional note containing each supported element plus hostile content (script tags, javascript: links, image syntax, raw HTML) and verify formatting renders and hostile content is inert.

**Acceptance Scenarios**:

1. **Given** a note with headings, lists, tables, task lists and strikethrough, **When** the detail page loads, **Then** each renders as formatted content, and a single newline in the body produces a visible line break.
2. **Given** a note with raw HTML or script, **When** rendered, **Then** it appears as inert text or is dropped, and never executes or injects markup.
3. **Given** a link, **When** its address is http or https, **Then** it renders as a link that opens safely (no opener or referrer leak); **and** any other scheme renders as plain text, not a link.
4. **Given** image syntax, **When** rendered, **Then** no image is displayed and no network request is made for it.
5. **Given** a wide table on a phone-width screen, **When** rendered, **Then** the table scrolls horizontally inside its own area and the page itself does not.

---

### Edge Cases

- A posting first seen before the window but applied to inside it: it is outside the application-rate cohort, yet its application still counts in Funnel B's applied stage, which is windowed by application date.
- A reviewed posting with no application (for example screened out or scored only): its has-application fact is false, and it appears in the cohort denominator only.
- Many postings share one first-seen date because they were imported in bulk: the window includes or excludes them together, so Funnel A can jump at that date. The help text from FR-008a says so; no special handling is applied.
- A brand-new database with no postings or applications: every tile reads "no data", charts show an empty state, and nothing errors.
- Only one application exists: ratios are computed and flagged "small sample"; the median equals that single value.
- An application with an applied date in the future or missing: excluded from window and trend calculations and not counted as an error.
- Applications exactly 14 or 30 days old: "at least 14 days old" includes an application exactly 14 days old. The ghost-rate denominator ("at least 30 days old") includes one exactly 30 days old, but the ghosted numerator ("older than 30 days") does not, so on day 30 an application is in the denominator and cannot yet count as ghosted. This one-day difference is deliberate, stated in the page's "How these are calculated" text and in `contracts/insights-lenses.md`, and pinned by boundary tests at 29, 30 and 31 days.
- Posting disposition was overwritten (for example, applied then rejected from an application event): post-apply stages are derived from applications and their events, never from posting disposition.
- An application with a rejection recorded before any interview versus after: split into the two buckets, and the two buckets sum to the total rejected.
- An application with both a rejected event and an offer event: it is counted in each rate it qualifies for, and the page does not double count it within a single funnel stage.
- The window changes between the dashboard and the list (user edits the filter after drilling down): the dashboard number is still correct for the dashboard's window; only the unmodified drill-down link is guaranteed to match.
- The page is loaded while the owner session has expired: standard sign-in behavior applies and no data is shown.
- A hostile or malformed note body or a very large note: rendering stays inert and the page remains responsive.

## Requirements *(mandatory)*

### Functional Requirements

**Page and window**

- **FR-001**: The system MUST provide an Insights page, reachable from the main navigation, available only to the signed-in owner.
- **FR-002**: The page MUST default to the last 90 days by application date and MUST offer an all-time toggle; the active window MUST be visible and shareable via the page address.
- **FR-003**: The page MUST be read-only: no control on it creates, changes or deletes data, and the feature MUST introduce no write path.
- **FR-004**: All figures MUST be scoped to the owner's data only.

**Funnel**

- **FR-005**: The page MUST show two funnels, each over a single cohort so that its bars nest (each stage is at most the stage before it) and each step is a true conversion. Funnel A, "Postings first seen in the window": leads, reviewed, applied-to (reviewed postings that have an application, the same count as the application-rate numerator). Funnel B, "Applications applied in the window": applied, responded, interviewed, offers. The two funnels are never combined into one chart and no bar in one is compared with a bar in the other.
- **FR-006**: A posting counts as reviewed when its disposition is neither lead nor unscored, or when it has an application. A closed posting with no application counts as expired, not reviewed.
- **FR-007**: Funnel B (applied, responded, interviewed, offers) MUST be derived from applications and their events, not from posting disposition. Its Offers bar counts applications that have both an offer event and an interview event, so it nests under Interviewed; offers logged without an interview event are shown as a separate note ("n offers had no interview event") with their own drill-down, so they are never hidden. Responded means at least one response event (interview, offer, offer accepted, offer declined or rejected); follow-up and withdrawn are not responses. "Callback" means the first interview event.
- **FR-008**: Funnel A and the postings-side figures (leads, reviewed, applied-to, screened out, expired) are windowed by the date the posting was first seen. Funnel B and the applications-side figures (applied, responded, interviewed, offers, rejection, ghosted, response time, applications per week) are windowed by application date. Each funnel is titled with its cohort and date basis, and the page states that Funnel A's applied-to and Funnel B's applied can legitimately differ.
- **FR-008a**: The Insights page MUST show help text beside the posting-window figures (Funnel A, application rate, screen-out rate) stating that the first-seen date is when RoleRadar first recorded a posting, not when the employer posted it, and that postings imported in bulk share one first-seen date, so a window can group them together. The text is generic and contains no real dates or counts.

**Rates**

- **FR-009**: Application rate uses a single posting cohort: reviewed postings first seen in the window that have at least one application / reviewed postings first seen in the window. It is therefore always 100% or less. Screen-out rate = screened-out postings / reviewed postings, over the same cohort.
- **FR-009a**: A posting MUST carry a stored has-application fact, true when the posting has at least one application. It MUST be written only at the repository choke point, like the other derived read-model fields, and backfilled by the existing reconcile-read-models command (dry run by default, and a dry run after the confirmed run must report zero differences). It MUST be exposed as a postings-list filter, so the application-rate numerator has a matching drill-down. The same filter serves as the "postings I applied to" view. Adding it introduces no new write path for users: the value is derived, never set directly.
- **FR-010**: Interview rate = applications with at least one interview event / applications at least 14 days old (the mature cohort). An all-time version MUST also be shown, visually marked as lower-confidence.
- **FR-011**: Rejection rate = applications with a rejected event / all applications, split into rejected before any interview and rejected after an interview.
- **FR-012**: Offer rate is shown two ways: applications with an offer event / applications with an interview, and offers / all applications.
- **FR-013**: Ghosted = status still applied, no response event, and older than 30 days. The 30-day threshold MUST be a single named, configurable constant. Ghost rate = ghosted / applications at least 30 days old (an application exactly 30 days old is in the denominator but is not yet ghosted; see Edge Cases).
- **FR-014**: Open applications = applications with no terminal event (rejected, withdrawn, offer accepted, offer declined).
- **FR-015**: Every percentage MUST be displayed with its numerator and denominator as "x of y". A denominator under 10 MUST also display "small sample". A zero denominator MUST display "no data" and never a percentage.

**Drill-down**

- **FR-016**: Every count on the page MUST link to the postings or applications list page with a filter that yields exactly that count for the same window and the same date basis (first-seen date for posting counts, application date for application counts). The application-rate numerator links to the postings list filtered by the has-application fact (FR-009a) and the same first-seen window and reviewed criteria.
- **FR-017**: A count that cannot be reproduced by a list filter MUST NOT be shown; instead the list MUST gain the needed filter, as a deliberate and tested change.
- **FR-018**: An automated test MUST compare each dashboard count to the row count of its drill-down list, for both windows, against the fictional seed database.

**Trends and response time**

- **FR-019**: The page MUST show applications per week as a column chart over the active window, with empty weeks shown as zero, and search-run yield (new postings per run) as a sparkline.
- **FR-020**: The page MUST show the median days from applied to first response event, with the number of responses it is based on. It MUST be a median, not a mean.

**Charts and accessibility**

- **FR-021**: Each chart MUST offer an equivalent table view, be fully keyboard operable, work in light and dark themes, and remain usable at phone width.
- **FR-022**: Charts and tiles MUST use the project's design-kit components, added copy-only to the vendored design system with its sync record refreshed, and consumed through the app's single UI entry point.

**Notes Markdown**

- **FR-023**: The note detail page MUST render the body as Markdown supporting standard formatting, GitHub-style tables, task lists and strikethrough, and single-newline line breaks.
- **FR-024**: Note rendering MUST NOT interpret raw HTML, MUST NOT render images or make any remote request for note content, and MUST NOT inject unescaped markup.
- **FR-025**: Only http and https links MUST render as links, and they MUST open without leaking the opener or referrer. Other schemes MUST render as plain text.
- **FR-026**: Tables in notes MUST scroll horizontally inside their own area on narrow screens.
- **FR-027**: Markdown rendering MUST be deliverable and shippable independently of the Insights page.

**Data and privacy**

- **FR-028**: Median response time and weekly buckets cannot be produced by the existing list queries, so the system MUST provide one small read-only, owner-scoped statistics read for them, through the existing owner-scoped read seam, never reading storage directly.
- **FR-029**: Tests, screenshots and reports for this feature MUST use only the fictional seed database; no automated test or committed artifact may read the real database.

### Key Entities *(include if feature involves data)*

- **Posting**: A job posting with a current disposition (lead, unscored, scored, applied, rejected, screened out, closed), a score, a first-seen date, and a derived has-application fact. Disposition reflects only the latest state.
- **Application**: A submission against a posting, with an applied date, status, and derived facts: has interview, first response date, last event date, age in days.
- **Application event**: A dated step on an application (applied, follow-up, interview, offer, offer accepted, offer declined, rejected, withdrawn). Events, not posting disposition, decide post-apply stages.
- **Search run**: A dated run of the job search with a count of new postings found; the source of yield trend.
- **Note**: Free-form text body, displayed as Markdown on its detail page.
- **Insights snapshot**: The computed set of counts, rates (with numerator, denominator and sample flag), weekly buckets, yield series and median response time for one window. Not stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the fictional seed database, 100% of dashboard counts equal the row count of their drill-down list, in both the 90-day and all-time windows.
- **SC-002**: 100% of displayed percentages show an "x of y" breakdown, and 100% of those with a denominator under 10 show "small sample".
- **SC-003**: Every rate and the median match independently hand-computed values on the seed database with zero discrepancies.
- **SC-003a**: Application rate never exceeds 100% in any window, and after the read-model backfill a dry run reports zero postings whose stored has-application fact disagrees with their actual applications.
- **SC-004**: The owner can answer "how many applications have I sent, and how many got a response" in under 10 seconds from opening the page.
- **SC-005**: The Insights page is fully readable and usable at 360 px width with no horizontal page scroll, in light and dark themes, and passes automated accessibility checks.
- **SC-006**: Every chart can be fully read and operated with the keyboard alone, and its table view contains the same data.
- **SC-007**: A note containing script tags, raw HTML, javascript: links and image syntax produces no executed script, no injected markup, and no outbound requests.
- **SC-008**: With the test database holding at least 1,000 postings and 300 applications, the page is ready to read within 2 seconds on the owner's machine.
- **SC-009**: Notes Markdown can be released with the Insights page absent, and Insights can be released with notes unchanged.

## Assumptions

- **Domain retention check (resolved)**: The Domain retains each posting's current score, current disposition, disposition reason, decided date and first-seen date, plus append-only audit entries that record prior and new disposition for each transition. Disposition history is therefore recoverable only from audit entries, which are not part of the list read model. "Reviewed" is therefore defined from what exists today: current disposition plus presence of an application (FR-006). It does not use the score or the audit history, and it undercounts a posting that was reviewed and then returned to lead. This limitation is shown in the page's definitions help.
- The metric definitions agreed on 2026-11-01 are final and are reproduced in the requirements above. The page includes a short "how this is calculated" explanation for each rate.
- Posting-side figures (leads, reviewed, screened out, application-rate numerator) use first-seen date for the window because postings have no application date. Decided by Matt: application rate uses one posting cohort so it cannot exceed 100%. Consequently Funnel B's "applied" count (applications by application date) and Funnel A's "applied-to" count, which is the application-rate numerator (postings by first-seen date), can legitimately differ, and the page's help text says why.
- First-seen date means when RoleRadar first recorded the posting. Bulk imports give many postings one shared first-seen date, which the posting-window help text must explain (FR-008a). The repository, tests, screenshots and reports contain no real dates or counts.
- The stored has-application fact needs a one-time backfill of existing data via the existing reconcile-read-models command, following its established run order (deploy, backup, dry run, confirm, dry run reports zero).
- Window boundaries and "days old" use whole UTC days, consistent with the rest of the app.
- The existing list pages can express or can be extended to express every filter the drill-downs need (for example offers and ghosted). Where a filter is missing, adding it is in scope (FR-017).
- Whether funnel counts come from separate list-count requests or one statistics call is a planning decision; the requirement is only that drill-down filters stay identical to the counted criteria.
- The weekly bucket week starts on Monday, UTC.
- Reading is on the owner's own machine, single user; no multi-user, sharing or concurrency requirements.
- The design-kit chart components (version 20) are available to copy; the vendored design system itself is never edited by hand.
- Markdown support uses the common GitHub-flavored feature set; extensions beyond that are out of scope.
- Out of scope: writes, scoring changes, exports, public demo hosting, per-company analytics, goals and alerts.
