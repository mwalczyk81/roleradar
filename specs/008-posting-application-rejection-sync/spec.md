# Feature Specification: Posting and Application Rejection Sync

**Feature Branch**: `008-posting-application-rejection-sync`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Posting and application rejection sync (008). Rejecting an applied posting must also record the rejection on its application, from every entry point, in one transaction. Add a note-only application event type so corrections stop being filed as rejections."

## Background

Today the sync between a posting and its application is one-way. Appending a rejected event to an application sets the posting to rejected. Appending withdrawn, offer accepted or offer declined sets the posting to closed. The reverse is missing: moving an applied posting to rejected (posting page, MCP `posting_transition`) changes only the posting. The application stays applied with no event, so the owner rejects the same job twice, and Insights undercounts rejections while overstating open and ghosted applications. Reported 2026-11-04. The owner's real data is currently consistent, so no data repair is needed.

## Clarifications

### Session 2026-11-05

- Q: When a rejected posting whose application is rejected is reopened, allow it or block it? → A: Allow the reopen, leave the application rejected, and show a warning on the posting page and in the MCP output (not only in the audit entry).
- Q: If the application already has a final outcome other than rejected (withdrawn, offer accepted or declined) and its posting is moved to rejected, succeed or refuse? → A: Succeed. The application keeps its outcome and gets no rejected event. A warning is shown on the posting page and in the MCP output, and recorded in the audit entry.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rejecting a posting rejects its application (Priority: P1)

The owner gets a rejection email and marks the applied posting as rejected, from the posting page or the MCP tool. The linked application records a rejected event dated today (or the date the owner gives) and shows as rejected, with no second step.

**Why this priority**: This is the defect. Without it the owner keeps entering the same rejection twice and the Insights rejection, open and ghost numbers stay wrong.

**Independent Test**: With a fictional applied posting that has an application, transition the posting to rejected through each entry point and check the posting, the application outcome, the new event and the audit entries.

**Acceptance Scenarios**:

1. **Given** an applied posting with an application that has no rejected event, **When** the owner transitions the posting to rejected with a reason, **Then** the posting becomes rejected, the application gains one rejected event and becomes rejected, all as one all-or-nothing change.
2. **Given** the scenario above, **When** the change completes, **Then** one audit record set covers both the posting change and the application event, and a failure in either write leaves both unchanged.
3. **Given** the owner supplies an event date, **When** the posting is rejected, **Then** the new event carries that date; **and** without a date it carries today's date in UTC.
4. **Given** a rejection recorded this way, **When** the owner views the application's events, **Then** the event's source shows it came from a posting transition, distinct from a manually entered event.
5. **Given** the same transition made through the web page and the MCP tool, **When** each is run against equivalent fictional data, **Then** the resulting posting, application, event and audit records are identical apart from actor and timestamps.
6. **Given** the new rejected event, **When** milestones and derived read-model fields are recomputed, **Then** they reflect the rejection exactly as they would for a manually entered rejected event.

---

### User Story 2 - Repeating or crossing the sync is safe (Priority: P1)

The owner may already have rejected the application first (which sets the posting to rejected), or may reject twice by habit. Neither case creates duplicate events, errors, or loops between the two sides.

**Why this priority**: The sync becomes two-way. Without a guard it would duplicate events or recurse, which would corrupt the counts this feature exists to fix.

**Independent Test**: Run each ordering (application first, posting first, both repeated) and count events and audit records.

**Acceptance Scenarios**:

1. **Given** an application that already has a rejected event, **When** its posting is transitioned to rejected, **Then** no second event is added and the transition succeeds.
2. **Given** an application already in the rejected outcome, **When** its posting is transitioned to rejected, **Then** no second event is added and the transition succeeds.
3. **Given** an application, **When** the owner appends a rejected event to it, **Then** the posting becomes rejected as today and the application receives exactly one rejected event, not two.
4. **Given** a posting rejected through the new path, **When** the owner later appends a manual rejected event, **Then** the existing behaviour for repeated events applies and nothing loops back to the posting.

---

### User Story 3 - Corrections no longer masquerade as rejections (Priority: P2)

The owner can add a note-only event to an application to record a correction or a remark. It is never a response, a rejection or an interview. The application page shows it as a note.

**Why this priority**: The existing second rejected event on one real application exists only to carry a correction. Until a neutral type exists, every correction either skews Insights or has nowhere to go. It is independent of the sync and can ship separately.

**Independent Test**: Add a note event to a fictional application and confirm that milestones, Insights and ghost rules give identical results with and without it.

**Acceptance Scenarios**:

1. **Given** an applied application older than 30 days with no response, **When** a note event is added, **Then** it is still ghosted and still has no response milestone.
2. **Given** an application with a note event, **When** Insights counts responses, rejections and interviews, **Then** the note contributes to none of them.
3. **Given** the same fictional dataset with and without note events, **When** the Insights oracle and the web/API parity tests run, **Then** every figure is identical.
4. **Given** a note event on an application, **When** the owner opens the application page, **Then** it appears in the event list labelled as a note.
5. **Given** the application event type list changes, **When** the contract snapshot, the .NET drift test and the web equality test run, **Then** all three agree on the new list and fail together if any one is stale.
6. **Given** a note event is added to a posting's application, **When** the sync runs, **Then** the posting's disposition is not changed.

---

### User Story 4 - The posting page says what will happen (Priority: P2)

When the owner rejects an applied posting on the posting page, the page states that the rejection will also be recorded on the application.

**Why this priority**: The behaviour changes what a click does to a second record. The owner should not discover that afterwards.

**Independent Test**: Open the reject action on an applied fictional posting and on a posting with no application and compare the wording.

**Acceptance Scenarios**:

1. **Given** an applied posting with an application, **When** the owner opens the reject action, **Then** the page says the rejection is also recorded on the application.
2. **Given** a posting with no application, **When** the owner opens the reject action, **Then** no such statement appears.

---

### User Story 5 - Reopening a rejected posting (Priority: P3)

The owner reopens (rejected to lead, reason required) a posting whose application is rejected. Events are never deleted, so the application cannot be un-rejected. The spec must define the outcome.

**Why this priority**: Rare, but it must not silently leave the two records disagreeing.

**Independent Test**: Reopen a rejected fictional posting that has a rejected application and check the posting, the application, and what the owner is told.

**Acceptance Scenarios**:

1. **Given** a rejected posting whose application is rejected, **When** the owner reopens it with a reason, **Then** the posting becomes a lead, the application stays rejected, and no application event is created, changed or removed.
2. **Given** the reopen above, **When** it completes, **Then** a warning that the application is still rejected is shown on the posting page, returned in the MCP tool output, and is also recorded in the audit entry.
3. **Given** a rejected posting with no application, or whose application is not rejected, **When** it is reopened, **Then** behaviour is exactly as today and no warning appears.

---

### Edge Cases

- A posting with no application: transitioning to rejected behaves exactly as today. No event is created.
- Target closed: stays posting-only. A listing can be taken down while the application is alive, so the application is untouched.
- Withdrawn, offer accepted and offer declined on the application keep closing the posting as today. Nothing flows back from the posting for those.
- An application with a withdrawn, offer accepted or offer declined outcome, whose posting is then moved to rejected: the transition succeeds, the application's outcome is kept, no rejected event is added, and a warning naming that outcome is shown (see FR-003a).
- A supplied event date that is not a valid calendar date is rejected with a validation error and nothing changes.
- A stale version token on the posting fails the whole transition with no event added.
- A failure while writing the application event rolls the posting change back.
- The reopen path must not create or remove any application event.
- Existing duplicate rejected events (one real application has two) are left alone.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Transitioning an applied posting to rejected MUST, when the posting has an application that is not already rejected, append a rejected event to that application and update the application outcome in the same all-or-nothing change as the posting.
- **FR-002**: One audit record set MUST cover the posting change and the application event of a single transition: the two entries are written in the same transaction and carry one shared identifier so they can be matched.
- **FR-003**: If the application already has a rejected event, or is already rejected, the transition MUST add no event and MUST NOT fail because of it.
- **FR-003a**: If the application has a final outcome other than rejected (withdrawn, offer accepted or offer declined; stored as the closed outcome), the transition MUST still succeed, MUST leave the application and its events unchanged, and MUST return a warning naming that outcome. The posting page and the MCP tool output MUST each show it, and the audit entry MUST record it.
- **FR-004**: The existing application-event-to-posting sync MUST keep its current behaviour. Neither direction may trigger the other again; a single user action produces at most one rejected event and one posting change.
- **FR-005**: The new event's date MUST default to today (UTC) and MUST be overridable by the caller on every entry point.
- **FR-006**: The new event's source MUST identify that it came from a posting transition, so it can be told apart from a manual event, and the application page MUST show each event's source.
- **FR-007**: Application milestones and derived read-model fields for the new event MUST be written through the same single write path used for every other application event. No separate path may be added.
- **FR-008**: Transitioning to closed MUST remain posting-only. Withdrawn, offer accepted and offer declined MUST keep syncing from application to posting as today.
- **FR-009**: A posting with no application MUST behave exactly as today for every transition.
- **FR-010**: The web page and MCP MUST produce the same result for the same transition, because the behaviour lives in the shared application layer. The MCP tool and API schemas MUST keep explicit types for every parameter, including the optional event date.
- **FR-011**: Reopening a rejected posting whose application is rejected MUST succeed, MUST leave the application rejected, and MUST NOT create, change or remove any application event.
- **FR-011a**: In that case the result MUST carry a warning that the application is still rejected, and the posting page and the MCP tool output MUST each show it. The audit entry MUST also record it. The warning MUST NOT appear when the application is absent or not rejected.
- **FR-012**: A new note-only application event type MUST exist. It MUST NOT count as a response, a rejection or an interview in milestones, Insights or the ghost rules, MUST NOT change the posting's disposition, and MUST NOT affect any derived application field, including the last-event date used to order applications.
- **FR-013**: Insights definitions MUST NOT change. The Insights oracle and the web/API parity tests MUST demonstrate that note events leave every figure unchanged.
- **FR-014**: The application event type list is a shared contract. The contract snapshot, the .NET drift test and the web equality test MUST be updated together.
- **FR-015**: The posting page MUST state, when rejecting an applied posting that has an application, that the rejection is also recorded on the application, and MUST NOT state it otherwise.
- **FR-016**: The application page MUST display the note event type with a clear label.
- **FR-017**: Nothing is hard-deleted by any part of this feature, and every write records an append-only audit entry.
- **FR-018**: A read-only consistency check that lists postings and applications disagreeing about rejection SHOULD be provided only if it is cheap to add. It MUST NOT modify data.
- **FR-019**: The decision on sync direction (which side drives which transitions and why closed is excluded) MUST be recorded in a short architecture decision record.

### Key Entities

- **Posting**: A job listing with a disposition (lead, applied, rejected, closed and others). Gains no new fields.
- **Application**: The owner's application to a posting, with an outcome and derived milestones. Its outcome moves to rejected when a rejected event is recorded.
- **Application event**: A dated entry on an application with a type, a source and notes. Gains one type, note. The source value for events created by a posting transition is new.
- **Audit entry**: Append-only record of a write. A rejection transition produces one set covering both records.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After rejecting an applied posting, the owner never has to reject the application separately: 100% of transitions through the web page and MCP leave the posting and its application agreeing.
- **SC-002**: Repeating or reordering the two rejection actions yields exactly one rejected event on the application in every ordering tested.
- **SC-003**: Adding any number of note events changes no Insights figure; the oracle and parity comparisons show zero differences.
- **SC-004**: The same transition run through both entry points (web page and MCP) on equivalent fictional data yields identical posting, application, event and audit results apart from actor and timestamps.
- **SC-005**: Posting transitions for postings without an application, and all closed transitions, show zero behaviour change against the existing test suite.
- **SC-006**: The owner can tell a posting-transition rejection event from a manual one on the application page without opening any other screen.
- **SC-007**: A failure injected into either write of a rejection transition leaves both records unchanged.
- **SC-008**: Every reopen of a rejected posting with a rejected application, and every posting rejection whose application has a different final outcome, shows its warning on both entry points (web page and MCP); zero such cases are silent.

## Assumptions

- The CLI is out of scope: it has no posting transition command today, and adding one is a later, separate item. The entry points for this feature are the web page (through the API) and MCP.
- Single owner, local-first, fictional data in the repo and tests; the owner's live database is never touched by development or tests.
- The new event type is named "note". Its label on the application page is "Note".
- The event source value for posting-driven rejections is a fixed, documented string such as "posting_transition"; its exact value is a planning decision.
- The caller supplies the event date as an optional calendar date (YYYY-MM-DD). The web posting page may default it silently to today.
- No existing data needs repair: the real data is consistent as of 2026-11-04.
- Out of scope: merging or deleting duplicate rejected events, any change to Insights definitions, hosting and account management, repairing existing data, and the generated-enum work on the board.
- Depends on existing behaviour: the posting disposition matrix, the application event append path and its posting sync, the single repository write path for derived fields, and the contract snapshot and drift tests from earlier features.
