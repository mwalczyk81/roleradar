# Data Model: Front Door

No domain entity or collection changes. The demo writes existing entities through existing repositories.

## Demo dataset (anchored to `asOf`, default today UTC)

| Entity | Count | Natural key | Notes |
| --- | --- | --- | --- |
| Company | 14 | name | Names in the spec. A mix of research statuses and ownership types. The hero company, Harborlight Systems, has four category tags. |
| Posting | 48 | canonical URL `https://jobs.example.com/<slug>` | Lead 8, Unscored 3, Scored 7, ScreenedOut 7, Applied 12, Rejected 6, Closed 5. First seen 150 to 2 days before `asOf`. Generic engineering-leadership titles and short invented descriptions. Four scored postings post a compensation range (text plus min and max, USD). The hero posting (Harborlight Systems, Head of Software Development) has a compensation text and range, an assessment note (its screening note) and three opportunity signals. |
| Application | 20 | posting (one per posting) | 12 applied, 6 rejected, 2 of the closed. Applied 5 to 110 days before `asOf`. |
| Application event | about 45 | belongs to its application | Interview, Offer, OfferAccepted, OfferDeclined, Rejected, Withdrawn, FollowUp, Note. An existing application's timeline is never appended to. At least 3 old applications with no events (ghosted); median first response between 6 and 12 days. |
| Note | 6 | title and area | Markdown limited to what the renderer allows (R-008); http and https links to `example.com` only; no raw HTML, no images. One of the six is the hero posting's assessment note (area `assessment`, a scoring table, risks and a task list). Notes are written before postings so the hero posting can carry the note's id. |
| Posting signal | 3 | written only when the posting has none | On the hero posting only: parallel posting, repost, deadline (the deadline after the application date). No "no salary" signal, since the posting has a range. |
| Career source | 1 | company, provider and URL | On the hero company only: provider `custom`, URL `https://jobs.example.com/harborlight-systems`, stored the way the company editor stores one (the URL in the slug). |
| Search run | 8 | started date | Yields 0 to 14 over 150 days, one outside the 90-day window. |
| Criteria revision | 1 | label | So the criteria page is not empty. |

Every id is `Guid.CreateVersion7()`, created only when the natural-key lookup finds nothing. The owner is `OwnerId`. Timestamps are UTC.

The enrichment (pay, signals, the assessment note, tags, the career source) changes no count above and no Insights number: the Insights reads use dispositions, applications and events only.

Notes are not linked to a company or posting. The note commands take no link, and the note detail endpoint returns no linked id and passes the note's area as the linked type, so a note page reads `Linked <area> (Unspecified)` whatever the seed does. Changing that is a product change, outside this feature.

Window checks the dataset must satisfy (unit-tested): in the 90-day cohort at least one funnel denominator is under 10 ("small sample") and at least one is 10 or more; every disposition appears at least once inside the window; the drill-down for the largest Insights count returns as many rows as the tile.

## Idempotency

First run creates everything. A later run finds the natural keys, creates nothing, reports counts and exits 0, including with a different `--as-of` (teardown first to refresh dates).

## Demo account (Identity SQLite, volume `roleradar-demo-identity`)

| Field | Value | Committed? |
| --- | --- | --- |
| User id | fixed v7-shaped UUID; equals the API's allowed subject in the demo compose file | yes, non-secret |
| Username | `demo-owner` | yes, non-secret |
| Password | generated, stored as a hash; printed once by `demo credentials` | never |
| Authenticator key | generated; printed once by `demo credentials`; read by `demo totp` | never |

`demo seed` creates the account with a generated password and key that are discarded, so a fresh stack has an account nobody can sign in to until `demo credentials` resets it.

## Generated demo secrets (R-014)

Client secrets and the session key are generated into the volume `roleradar-demo-secrets` by a one-shot step on first start. Never committed, never printed.

## Guard decision table

| Demo mode (`ROLERADAR_DEMO=1`) | Database name | Host | Result |
| --- | --- | --- | --- |
| on | `roleradar-demo` | `demo-mongo` (production callers) or the explicit allowed host (tests only) | allow |
| on | `roleradar` | any | refuse |
| on | empty, near-miss, other case | any | refuse |
| on | `roleradar-demo` | `127.0.0.1`, `localhost`, `host.docker.internal`, `mongo`, several servers | refuse |
| off or not exactly `1` | `roleradar-demo` | any | refuse (the demo database is never served or written as real) |
| off | `roleradar` | any | not applicable; demo commands refuse, normal commands unaffected |

## Image manifest (`docs/images/manifest.json`)

Fourteen entries named `{view}-{light|dark}.png` for `insights`, `insights-drilldown` (the applications list reached from the Insights "Interviewed" stage link; the capture checks the list has as many rows as the link's count), `postings-list`, `posting-detail`, `application-timeline`, `company`, `note`. A `hero` object names the README hero image (`insights`, light with a dark variant) and the hero records the capture opens (company, posting, note, drill-down lens); the manifest test checks them against the seed's constants. A `placeholder.png` (1x1 PNG) is committed early; the test requires it to be removed once all fourteen exist.
