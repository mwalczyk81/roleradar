# Data Model: Posting and Application Rejection Sync

No new collections, fields or indexes. Two new string values and two new result members.

## Application event (`application_events`)

| Field | Change |
| --- | --- |
| `event_type` | New allowed value `note`. Full list: `applied`, `follow_up`, `interview`, `offer`, `offer_accepted`, `offer_declined`, `rejected`, `withdrawn`, `note`. |
| `event_source` | New conventional value `posting_transition` (max 100 chars, existing limit). Manual events keep `web`, `mcp` and so on. |
| `event_date` | Unchanged (`YYYY-MM-DD`). For a posting-driven event: caller's `eventDate`, else today UTC. |
| `notes` | For a posting-driven event: the transition reason, trimmed to the existing 500 character limit. |

API read shape: `ApplicationEventDto` gains nullable `eventSource` so the page can show it. No storage change.

## Application (`applications`)

No schema change. Outcome moves `applied -> rejected` through `Application.ApplyEvent(Rejected, ...)`. `closed` outcomes are never changed by a posting transition.

Derived fields (`has_interview`, `has_first_response`, `last_event_at`, `has_last_event`) are recomputed only in `MongoApplicationRepository.AppendEventAsync`. Rule change: `note` events are ignored by every one of them.

## Posting

No schema change. Behaviour change only in the handler: `applied -> rejected` now has an application side effect.

## Transition result (Application DTO)

`PostingTransitionResultDto` gains:

| Member | Type | Notes |
| --- | --- | --- |
| `Warnings` | `IReadOnlyList<TransitionWarningDto>` | Empty when none. |

`TransitionWarningDto`: `Code` (`application_still_rejected` or `application_closed`), `Message` (fixed string). The same two members appear in the API posting detail response as nullable `warnings`. The API posting detail response also gains nullable `applicationId` (`Guid?`, null when the posting has no application).

## State changes

```text
posting applied --reject--> posting rejected
                              + application applied --rejected event--> application rejected   (no rejected event yet)
                              + application rejected                                         (no change)
                              + application closed                                           (no change, warning application_closed)

posting rejected --reopen (override reason)--> posting lead
                              application rejected -> unchanged, warning application_still_rejected
```

## Audit

Per `applied -> rejected` with a new event: one `posting_transition` entry (posting) and one `application_event_append` entry (application, `event_source: posting_transition`), same transaction. Warning codes are recorded in the posting entry's details.
