# ADR 0033: Rejecting an Applied Posting Records the Rejection on Its Application

- **Status**: Accepted
- **Date**: 2026-11-05
- **Feature**: `008-posting-application-rejection-sync`

## Context and Problem Statement

The posting and its application can disagree about a rejection because the sync is one-way. Appending a rejected application event sets the posting to rejected, and withdrawn, offer accepted and offer declined set it to closed. The reverse was missing: moving an applied posting to rejected changed only the posting, so the owner rejected the same job twice and Insights undercounted rejections.

## Decision Outcome

Two directions, each with a fixed owner of the rule, and no path from one back into the other.

- **Application event to posting (unchanged).** `RecordApplicationCommandHandler` keeps syncing: rejected sets the posting to rejected; withdrawn, offer accepted and offer declined set it to closed.
- **Posting to application (new, one case).** `TransitionPostingCommandHandler`, for `applied -> rejected` only, appends a `rejected` application event with source `posting_transition` in the same transaction, through the existing `UpsertApplicationAsync` and `AppendEventAsync` pair. `AppendEventAsync` stays the only place milestones and derived read-model fields are written (ADR 0031). The event date is the caller's `eventDate` or today (UTC). Two audit entries (`posting_transition` and `application_event_append`) are written in that transaction and share one `correlation_id` in their `After` details.
- **No loop, structurally.** The transition handler depends only on the transaction context and the clock. It has no reference to the application-event handler, so it cannot trigger the application-to-posting sync. Nothing is guarded by a flag.
- **Idempotent.** No event is added when the application is already rejected or already has a rejected event. A posting with no application behaves exactly as before.
- **`closed` stays posting-only.** A listing can be taken down while the application is still alive, so `applied -> closed` never touches the application. Withdrawn and the two offer outcomes still close the posting from the application side, because those are the owner's own decisions about the application.
- **Warnings, not blocks.** Two cases leave the records disagreeing on purpose and return a warning from a closed code list instead of failing: reopening a rejected posting whose application is rejected (`application_still_rejected`; events are never deleted, so the application cannot be un-rejected), and rejecting a posting whose application ended another way (`application_closed`; the application keeps its outcome). Hosts show the returned message and never branch on the code.

### Consequences

- Good: one transition, one correction; the fix lives in Application, so every host inherits it.
- Good: no new seam, repository method, audit operation or schema change.
- Bad: the two directions are now both automatic, so a future change to either handler must keep the "no reference between them" property. A unit test pins the transition handler's constructor dependencies.
- Bad: a reopen can leave a lead posting next to a rejected application. That is visible (warning, audit) rather than hidden.

### Alternatives considered

- Block the reopen until the application is handled: rejected, because a rejected event can never be removed, so the posting could never be reopened.
- Derive the posting state from the application at read time: rejected, a much larger change to the read models and Insights for one transition.
- A separate sync command or event bus: rejected, a second path to forget and prohibited without an ADR by the constitution (principle IV).
- Sync `closed` too: rejected, a taken-down listing does not mean the application is over.
