# Excel and Power BI (prospective follow-up)

**Status: not implemented and not supported.** No Excel or Power BI integration exists in RoleRadar today, and none is planned for a specific release. This note records what the OData read API from feature 006 would make possible, so a future feature starts from facts rather than guesses.

## What the API provides

- An OData v4 service root at `https://roleradar.example/odata`. Entity sets sit directly under it, for example `https://roleradar.example/odata/Postings`. The service document and metadata are at `/odata` and `/odata/$metadata`.
- Six read-only entity sets: `Postings`, `Companies`, `Applications`, `Notes`, `Criteria`, `SearchRuns`. Query options and limits are in [docs/api/odata-queries.md](../api/odata-queries.md).
- Bearer authentication. Every request needs `Authorization: Bearer <access token>` for the single configured owner. There is no anonymous access and no cookie or basic-auth mode.
- A read-only guarantee. `POST`, `PUT`, `PATCH` and `DELETE` on any entity set return `405` with `Allow: GET`, so a client cannot change data through OData. Writes happen only through the `/api/v1` command endpoints, which Excel and Power BI would not use.

## Open questions for a future feature

- **Token handling.** Excel and Power BI connectors that speak OData natively expect their own sign-in flows (organizational account, API key header, or basic). A bearer token supplied by hand expires. Whether a client can obtain and refresh a token without a custom connector is unverified.
- **Paging.** Responses are capped at 100 rows per request and carry no `@odata.nextLink`. A client must page with `$top` and `$skip`; the stock Power BI OData connector's paging behavior against this service has not been tested.
- **Transport.** The API is meant to be reached over HTTPS from a trusted host. Exposing it to a desktop client implies deciding how that client reaches it and how CORS and TLS are handled; the API ships with no CORS middleware.
- **Data sensitivity.** Posting, company and note text is personal job-search data. Any report built on it leaves the application's access controls.

Until a feature specifies and tests these points, treat Excel and Power BI as unsupported.
