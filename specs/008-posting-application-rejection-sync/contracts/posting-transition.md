# Contract: posting transition

Additive changes only. Every parameter keeps an explicit type.

## Application command

`TransitionPostingCommand` gains `string? EventDate = null` (before `Actor`; named argument at all call sites).

Validation: when present and non-empty it must parse as `yyyy-MM-dd` (invariant culture), otherwise `ValidationError` ("Event date must be in YYYY-MM-DD format."), no write. Ignored when the transition creates no event.

## MCP: `posting_transition`

New optional parameter:

| Name | Type | Description |
| --- | --- | --- |
| `eventDate` | `string?` | "Date (YYYY-MM-DD) recorded on the application when rejecting an applied posting. Defaults to today (UTC)." |

The description string gains one sentence: rejecting an applied posting also records a rejected event on its application. The JSON result gains `warnings` (array of `{ code, message }`, empty when none). Tool count stays 12.

## API: `POST /api/v1/postings/{id}/transition`

Request `TransitionPostingRequestDto` gains `eventDate: string | null` (optional, `yyyy-MM-dd`).

Response `PostingDetailDto` gains `warnings: Array<{ code: string; message: string }> | null`. Set (possibly empty) by this endpoint, `null` on GET.

Response `PostingDetailDto` also gains `applicationId: string (uuid) | null`: the id of the posting's application, `null` when the posting has none. Set by `GET /api/v1/postings/{id}` and by the transition response, which use the same builder (one owner-scoped lookup by posting id). The web page uses it to decide whether to show the application notice (FR-015).

Errors unchanged: 400 for validation (including a bad `eventDate`, field name `eventDate`), 404, 409 stale version token, 401, 403.

## API: application events

`ApplicationEventDto` (inside `ApplicationDetailDto.events`) gains `eventSource: string | null`, mapped from the stored `event_source`. Additive; the application page shows it next to each event.

## Warning codes

| Code | When | Message |
| --- | --- | --- |
| `application_still_rejected` | Rejected posting reopened to lead while its application is rejected | "The application is still rejected." |
| `application_closed` | Applied posting rejected while its application is closed | "The application is closed and was not changed." |

The set of codes is a closed list in the Application layer; hosts display the returned message and never build their own.

## Regeneration

`contracts/openapi` and `contracts/client/api-types.ts` are regenerated; the existing drift tests must pass unchanged apart from the new members.
