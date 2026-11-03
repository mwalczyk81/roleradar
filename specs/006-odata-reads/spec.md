# Feature Specification: OData Read Service & Multi-Page Server Querying

**Feature Branch**: `006-odata-reads`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "OData for reads (own feature, after 005b). Why: the web list pages sort only the 25 rows loaded, so Matt cannot see all applied postings among 240+; the API has no server-side sort, filter or count. Reads move to OData v4 so sorting, filtering, paging and counts work over all data; writes stay the existing REST command endpoints (posting transition, company PATCH, create application, append event). Dashboard counts and drill-down later reuse the same filters. Scope: read-only OData entity sets for postings, companies, applications (with event-derived fields), notes, criteria and search runs, under /odata (separate from /api/v1). Read models are flattened and sortable: postings carry company name; applications carry posting title, company name, status, hasInterview, firstResponseAt, lastEventAt and age in days (no nested-event lambdas needed by clients, and no per-row API calls from the web). Query options: $filter, $orderby, $top, $skip, $count, $select only. $apply, $expand, batch and writes are out of scope. Strict limits: allowlisted filterable/sortable/selectable properties per entity set, max $top 100, node and depth limits, anything else returns 400 problem details. Ordering: stable id tiebreaker, case-insensitive text sort, disposition sorts in pipeline order (lead, scored, applied, rejected, ...), indexes per orderable field with owner_id first. Security: same owner-token auth as the API (roleradar.api scope, allowlisted owner sub); OwnerId scoping enforced in the Application layer through a named, owner-scoped read seam (an ADR decides the IQueryable boundary; the API never touches collections directly); read-only methods only; untrusted posting text. Contract: $metadata is the contract; commit a snapshot under contracts/ with a .NET drift test and a web equality test; decide a typed TypeScript client or query builder generated from it (evaluate before committing). Web: postings, companies, applications, notes and search runs list pages use OData; column-header sorting writes a whitelisted $orderby to the URL and works across all pages; filters, paging and total counts are server-side; remove the 'sorting applies to this page only' note; detail pages keep the existing REST endpoints. Unknown to resolve first (spike): verify OData on current runtime alongside minimal APIs, and write the ADR. Tests first: query-shape contract tests, abuse tests (huge $top, unknown properties, deep $filter, disallowed operators), an owner-isolation test using a second owner id, isolated database translation tests, a 5,000-row sanity check, web e2e proving server sort across pages. Fictional data only. Out of scope: OData writes, aggregation, hosting or public exposure, contacts (stay CLI-only), Excel and Power BI authentication (document how to call it with a bearer token only)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Global Sorting and Filtering Across All Postings (Priority: P1)

As a job seeker managing a large volume of prospective positions (over 240 postings), I want to sort, filter, and page across my entire catalog of job postings directly on the server, so that I can immediately view and prioritize all postings in specific stages (such as "applied") without being limited to reordering only the current page of 25 items.

**Why this priority**: Currently, list views sort only the 25 items loaded in memory on the active page, making it impossible to see or rank applied or top-scored postings that reside on later pages. Resolving this provides immediate day-to-day usability for the user's primary workflow.

**Independent Test**: Can be tested by loading the postings list with 240+ records spread across multiple pages, clicking the "Stage" or "Score" column header, and confirming that the first page displays the highest-priority postings across the entire dataset rather than only re-sorting the first 25 records.

**Acceptance Scenarios**:

1. **Given** 240 job postings in the system, **When** the user sorts by disposition in descending order, **Then** the first page of results contains postings in the most advanced pipeline stages across all 240 records (following domain progression order: `unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`), followed by a stable secondary sort on `id`.
2. **Given** job postings across multiple companies, **When** the user filters postings by company name or title matching a text search term `q`, **Then** the server filters the complete dataset case-insensitively, returns matching records paginated, and displays the accurate total count of matching records.
3. **Given** postings with and without calculated scores, **When** the user sorts postings by `score` in either ascending or descending order, **Then** postings with a null score appear last in both sort directions.
4. **Given** a user navigating through paginated results, **When** advancing from page 1 to page 2, **Then** consecutive pages do not repeat or omit records even when multiple postings share identical sort values.

---

### User Story 2 - Enriched Application Tracking with Computed Lifecycle Milestones (Priority: P1)

As a job seeker tracking submitted job applications, I want the applications list to show denormalized job posting and company details alongside computed lifecycle metrics (specifically: current status, interview occurrence, date applied, date of first response, date of most recent activity, and application age in days), so that I can sort and filter by responsiveness and recency without requiring repeated per-row API queries.

**Why this priority**: Job applications involve complex timeline events. Flattening these events into precalculated sortable scalar fields allows the user to filter for active interviews or overdue responses instantly without client-side calculation overhead or nested-event queries.

**Independent Test**: Can be tested by navigating to the Applications list page and sorting by "Age (Days)" or filtering by "Has Interview", verifying that the displayed rows accurately reflect event-derived milestones and that sort order evaluates across the entire application history.

**Acceptance Scenarios**:

1. **Given** submitted applications with lifecycle events, **When** the user views the applications list, **Then** each row displays `id`, `postingId`, `companyId`, `postingTitle`, `companyName`, `status` (`applied`, `rejected`, or `closed`), `appliedAt`, `hasInterview`, `firstResponseAt`, `lastEventAt`, and `ageInDays`.
2. **Given** applications spanning various stages, **When** the user filters for applications where `hasInterview` is true and orders by `lastEventAt` descending, **Then** only applications with interview activity are returned, ordered by the most recent event date across all matching records, with null timestamps sorted last.
3. **Given** the web client requesting application records, **When** loading the page, **Then** all flattened attributes and total matching counts are retrieved in a single query without making secondary requests for individual company or posting details.

---

### User Story 3 - Server-Side Querying for Companies, Notes, Criteria, and Search Runs (Priority: P2)

As a user organizing job-search assets and research history, I want consistent server-side sorting, text filtering, projection, and pagination across Companies, Notes, Criteria, and Search Runs, so that my entire search workspace responds consistently to navigation, search inputs, and column sorting.

**Why this priority**: Extends the query capabilities established for Postings and Applications to all remaining collections, eliminating UX inconsistencies across the web application.

**Independent Test**: Can be tested by navigating to the Companies, Notes, Criteria, and Search Runs views, applying column sorts and keyword filters, and verifying that URL query state reflects the active query parameters and updates the table seamlessly.

**Acceptance Scenarios**:

1. **Given** a collection of companies, **When** the user sorts by company name (`name`), status (`status`), or headquarters (`headquarters`), **Then** the server sorts case-insensitively and returns paginated records with total count metadata.
2. **Given** interview and research notes, **When** the user filters notes by heading (`heading`), tags (`tags`), or linked entity (`linkedEntityType`, `linkedEntityId`), **Then** matching notes are returned with selectable fields projected according to the view requirements, while full note `content` is selectable but not sortable or filterable.
3. **Given** scoring criteria with validity dates, **When** filtering by effective date, **Then** the system filters where `effectiveFrom` is on or before the date and `effectiveTo` is either null or on or after the date.
4. **Given** search runs logged by automation agents, **When** the user inspects recent runs ordered by `runDate` descending, **Then** the runs are sorted chronologically across all historical records displaying `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, and `summary`.

---

### User Story 4 - Query Guardrails, Resource Bounds, and Abuse Prevention (Priority: P2)

As a system owner, I want query endpoints to enforce strict resource limits (including maximum page size, allowlisted sortable and filterable properties, allowed filter operators, and rejection of non-GET methods), so that accidental or malicious unbounded queries do not degrade system stability or exhaust memory.

**Why this priority**: Exposing dynamic server-side querying requires rigorous boundaries to prevent memory leaks, deep recursion, unindexed full collection scans, and injection vulnerabilities.

**Independent Test**: Can be tested by submitting non-GET requests (verifying HTTP 405), excessive page sizes (verifying HTTP 400), unindexed field sorts, nested expressions exceeding depth limits, or disallowed operators (such as arithmetic or lambda expressions), and verifying that each produces an explicit rejection with an informative problem details payload.

**Acceptance Scenarios**:

1. **Given** any HTTP method other than GET (such as POST, PUT, PATCH, DELETE) submitted to a read-only query endpoint, **When** processed by the server, **Then** the request is rejected with HTTP 405 Method Not Allowed including an `Allow: GET` header.
2. **Given** a query requesting a page size greater than 100 items (e.g., `$top=500`), **When** processed by the server, **Then** the request is rejected with HTTP 400 Bad Request indicating the maximum allowed page size is 100.
3. **Given** a query attempting to filter or order by an unlisted property or attempting to filter/sort on note `content`, **When** received by the query engine, **Then** the request is rejected with HTTP 400 Bad Request detailing the unrecognized or disallowed property.
4. **Given** a query using disallowed operators (such as arithmetic functions, `any`/`all` lambdas, or unapproved string functions) or disallowed query options (such as `$apply`, `$expand`, or `$batch`), **When** received, **Then** the system rejects the query with HTTP 400 Bad Request.
5. **Given** an authenticated user querying any entity set, **When** evaluating the query filter, **Then** the system automatically applies owner isolation boundaries such that records belonging to any other owner identifier are strictly inaccessible, while never exposing the internal owner identifier in returned payloads.

---

### User Story 5 - Machine-Readable Contract, Client Synchronization & API Deprecation (Priority: P3)

As a developer and system maintainer, I want the query service schema to serve as a machine-readable contract with automated drift verification, and I want legacy list endpoints retired after frontend migration, so that API surface remains clean, maintainable, and free of redundant query paths.

**Why this priority**: Eliminates manual synchronization errors between backend query capabilities and frontend UI state, ensuring contract integrity as entities evolve, while retiring obsolete endpoints to prevent drift.

**Independent Test**: Can be tested by running automated contract drift verification checks against the committed schema metadata snapshot, validating that frontend client models match the published schema definitions, and verifying that legacy `/api/v1` list endpoints are cleanly retired after web migration while single-entity detail and command endpoints remain unaffected.

**Acceptance Scenarios**:

1. **Given** the query service schema metadata, **When** running automated contract verification, **Then** the current running schema matches the checked-in schema snapshot without drift.
2. **Given** frontend query construction, **When** generating query parameters for sorting, filtering, or selecting fields, **Then** query parameters are strictly validated against typed definitions derived from the contract schema.
3. **Given** completion of web client migration to the query service, **When** reviewing the API surface, **Then** the legacy list endpoints (`GET /api/v1/postings`, `GET /api/v1/companies`, `GET /api/v1/applications`, `GET /api/v1/notes`, `GET /api/v1/criteria`, `GET /api/v1/search-runs`) are removed from the API and OpenAPI contract, while single-entity detail endpoints, command endpoints, CLI commands, and agent tools remain fully operational.

---

### Edge Cases

- **Ties in Sort Order**: When multiple records have identical values for an ordered property (such as same stage or same creation date), the system must apply a deterministic secondary sort on the unique record identifier (`id`) to prevent pagination jitter (items appearing on multiple pages or disappearing between page transitions).
- **Domain Pipeline Ordering**: Sorting postings by pipeline disposition must follow the domain pipeline progression rank order (`unscored` -> `lead` -> `scored` -> `applied` -> `rejected` -> `screened_out` -> `closed`) rather than lexicographical string order. Postings dispositions are strictly the 7 values from the domain disposition matrix; lifecycle events (such as interview, offer, or archived) are application events, not posting dispositions.
- **Null Value Sorting**: For nullable sortable fields (`score` on postings, `firstResponseAt` and `lastEventAt` on applications), null values sort last regardless of whether the sort direction is ascending or descending (`nulls last`).
- **Case-Insensitive Text Ordering & Searching**: Company names, job titles, and text filters must sort and compare predictably regardless of casing (e.g., "Acme", "acme", and "ACME" sort consistently and match case-insensitive search filters).
- **Zero-Result Pagination**: When a filter matches zero records, the response must return an empty list accompanied by an explicit total count of 0, rather than an error or omitting count metadata.
- **Negative or Malformed Paging Offsets**: Requests specifying negative skip offsets (`$skip < 0`) or non-numeric pagination values must be rejected immediately with HTTP 400 Bad Request.
- **Non-GET HTTP Methods**: Any non-GET request (POST, PUT, PATCH, DELETE) sent to the read-only query service paths must return HTTP 405 Method Not Allowed with an `Allow: GET` response header.
- **Untrusted Posting Content**: Text fields containing user- or agent-sourced external content (such as job descriptions, titles, and requisition IDs) must be treated as untrusted text, safely serialized, and free from executable markup or script injection vulnerabilities.
- **Owner ID Omission**: The internal owner identifier is strictly used by the server to isolate data and must never be exposed as an attribute in any client-facing query read model or schema definition.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a dedicated, read-only query service endpoint prefix (`/odata`) separate from the existing command API (`/api/v1`).
- **FR-002**: The query service MUST expose distinct entity sets for `Postings`, `Companies`, `Applications`, `Notes`, `Criteria`, and `SearchRuns`.
- **FR-003**: The query service MUST reject all non-GET HTTP methods (POST, PUT, PATCH, DELETE) with HTTP 405 Method Not Allowed, returning an `Allow: GET` header.
- **FR-004**: The query service MUST support server-side filtering (`$filter`), ordering (`$orderby`), paging (`$top` and `$skip`), field projection (`$select`), and total count retrieval (`$count`).
- **FR-005**: The query service MUST reject any attempt to use unsupported query options, including aggregation (`$apply`), entity expansion (`$expand`), batching (`$batch`), and write operations, returning HTTP 400 Bad Request with Problem Details.
- **FR-006**: The system MUST enforce a maximum page size limit of 100 items for any query; requests omitting `$top` MUST default to 25 items, and requests specifying `$top > 100` MUST return HTTP 400 Bad Request.
- **FR-007**: The system MUST support only the following allowlisted `$filter` operators: `eq`, `ne`, `gt`, `ge`, `lt`, `le`, `and`, `or`, `not`, `contains`, `startswith`, `endswith`, and `tolower`. Arithmetic operators, `any`/`all` lambda expressions, and all other functions MUST be rejected with HTTP 400 Bad Request.
- **FR-008**: The system MUST enforce strict allowlists of queryable (filterable, sortable, and selectable) properties per entity set, rejecting queries targeting non-allowlisted properties with HTTP 400 Bad Request. Note `content` MUST be selectable only, and MUST NOT be filterable or sortable.
- **FR-009**: The system MUST enforce query complexity bounds, including maximum expression tree depth and node count limits, rejecting oversized query trees with HTTP 400 Bad Request.
- **FR-010**: The read models MUST expose strictly authentic domain and contract fields, and MUST NOT expose the internal owner identifier:
  - `Postings`: `id`, `companyId`, `companyName`, `title`, `requisitionId`, `location`, `disposition`, `resurfacePolicy`, `score`, `createdAt`, `updatedAt`.
  - `Companies`: `id`, `name`, `status`, `ownershipType`, `headquarters`, `description`, `categoryTags`.
  - `Applications`: `id`, `postingId`, `companyId`, `postingTitle`, `companyName`, `status` (`applied` | `rejected` | `closed`), `appliedAt`, `hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays`.
  - `Notes`: `id`, `heading`, `tags`, `linkedEntityType`, `linkedEntityId`, `createdAt`, `updatedAt`, and selectable-only `content`.
  - `Criteria`: `id`, `name`, `category` (mapped from the current criteria category representation), `ruleText`, `effectiveFrom`, `effectiveTo`, `isActive`.
  - `SearchRuns`: `id`, `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, `summary`. Only `runDate` and `newPostings` are stored; `companiesSearched` (0), `postingsFound` (equal to `newPostings`) and `errorsCount` (0) are constants that mirror the REST `SearchRunDto`, so they are selectable but neither filterable nor sortable.
  - Stored-field mapping (see data-model.md): note `linkedEntityType` is the stored `area` and `linkedEntityId` is always null; criteria `name` and `ruleText` are the stored `key` and `value`; search run `runDate` and `summary` are the stored `started_at` and `notes`.
- **FR-011**: Text property ordering and filtering MUST execute case-insensitively.
- **FR-012**: Sorting by posting disposition MUST order records by the domain pipeline progression rank order (`unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`) rather than alphabetical order. Dispositions MUST be strictly validated against the 7 recognized domain values.
- **FR-013**: Nullable sortable fields (`score`, `firstResponseAt`, `lastEventAt`, `effectiveTo`) MUST order null values last regardless of whether the sort order is ascending or descending (`nulls last`).
- **FR-014**: All ordering operations MUST automatically append a stable identifier tiebreaker (`id`) to guarantee deterministic pagination.
- **FR-015**: The query service MUST authenticate requests using the same token authorization scheme as the existing API, requiring the `roleradar.api` scope and validating the allowlisted owner subject.
- **FR-016**: Data access in the query engine MUST be scoped strictly to the authenticated `OwnerId` in the application layer; the query endpoint MUST NOT expose or connect directly to un-scoped database collections.
- **FR-017**: Multi-owner isolation MUST ensure that queries never return records belonging to an owner other than the authenticated user.
- **FR-018**: The query service MUST publish a formal service metadata contract (`$metadata`) describing all entity types, scalar types, and queryable attributes.
- **FR-019**: A snapshot of the service metadata contract MUST be committed to the repository, with automated contract drift tests verifying that the implementation matches the committed schema.
- **FR-020**: The web client MUST implement a strongly-typed query client or query builder verified against the metadata schema.
- **FR-021**: Web list views for Postings, Companies, Applications, Notes, Criteria, and Search Runs MUST utilize the query service for all data retrieval, paging, sorting, and filtering.
- **FR-022**: Existing web list filter controls MUST map directly onto `$filter` expressions:
  - `disposition`: `disposition eq '<value>'`
  - `companyId`: `companyId eq <guid>`
  - `resurfacePolicy`: `resurfacePolicy eq '<value>'`
  - `minScore`: `score ge <minScore>`
  - Text search `q`: combined case-insensitive text match across searchable fields (e.g., for postings: `(contains(tolower(title), '<q>') or contains(tolower(companyName), '<q>'))`)
  - Criteria `effectiveDate`: `(effectiveFrom le <date> and (effectiveTo eq null or effectiveTo ge <date>))`
- **FR-023**: Clicking table column headers on web list views MUST update the URL query state with an allowlisted sort expression and trigger a server-side query across all matching records.
- **FR-024**: The web list views MUST remove any disclaimer or note indicating that sorting applies only to the current page.
- **FR-025**: Following web client migration to the query service, the legacy list endpoints (`GET /api/v1/postings`, `GET /api/v1/companies`, `GET /api/v1/applications`, `GET /api/v1/notes`, `GET /api/v1/criteria`, `GET /api/v1/search-runs`) MUST be removed from the API and OpenAPI contract. Single-entity detail endpoints (`GET /api/v1/{entity}/{id}`), mutation endpoints (`POST`, `PATCH`), the CLI, and the agent MCP server MUST remain intact and unaffected.
- **FR-026**: Backing data store indexes MUST exist for all orderable and filterable properties, with the owner identifier positioned as the leading index field.
- **FR-027**: The system documentation MUST include operational instructions for querying `/odata` endpoints with standard HTTP clients (such as `curl`) using bearer token authorization. Integration with external spreadsheet and BI reporting tools is explicitly documented as a prospective follow-up, not a committed feature deliverable.

### Key Entities

- **PostingReadModel**: Flattened representation of a prospective job posting. Attributes: `id` (identifier), `companyId` (identifier), `companyName` (text), `title` (text), `requisitionId` (optional text), `location` (optional text), `disposition` (pipeline status: `unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`), `resurfacePolicy` (resurface policy: `never`, `if_reopened`), `score` (optional integer score), `createdAt` (UTC timestamp), `updatedAt` (UTC timestamp).
- **CompanyReadModel**: Representation of an employer or organization. Attributes: `id` (identifier), `name` (text), `status` (company status), `ownershipType` (ownership classification), `headquarters` (optional text), `description` (optional text), `categoryTags` (list of text tags).
- **ApplicationReadModel**: Enriched representation of a job application record. Attributes: `id` (identifier), `postingId` (identifier), `companyId` (identifier), `postingTitle` (text), `companyName` (text), `status` (application status: `applied`, `rejected`, `closed`), `appliedAt` (UTC timestamp), `hasInterview` (boolean indicator), `firstResponseAt` (optional UTC timestamp), `lastEventAt` (optional UTC timestamp, null only when the application has no events), `ageInDays` (integer count).
- **NoteReadModel**: Representation of a research or interview note. Attributes: `id` (identifier), `heading` (text), `tags` (list of text tags), `linkedEntityType` (entity discriminator), `linkedEntityId` (identifier), `createdAt` (UTC timestamp), `updatedAt` (UTC timestamp), `content` (selectable-only narrative text).
- **CriterionReadModel**: Representation of a scoring criterion. Attributes: `id` (identifier), `name` (text), `category` (classification category), `ruleText` (rule description), `effectiveFrom` (UTC timestamp), `effectiveTo` (optional UTC timestamp), `isActive` (boolean indicator).
- **SearchRunReadModel**: Representation of an automated search execution log. Attributes: `id` (identifier), `runDate` (UTC timestamp), `companiesSearched` (integer count), `postingsFound` (integer count), `newPostings` (integer count), `errorsCount` (integer count), `summary` (optional text).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Web list views sort and filter across the complete dataset (including catalogs of 240+ postings) on the server, returning initial page responses in under 500ms under standard local conditions.
- **SC-002**: 100% of pagination requests across all entity sets yield non-overlapping, deterministic results across consecutive pages when sorted by any allowlisted field.
- **SC-003**: 100% of non-GET requests to `/odata` endpoints are rejected with HTTP 405 Method Not Allowed and an `Allow: GET` header.
- **SC-004**: 100% of invalid query requests (including page sizes exceeding 100 items, unallowlisted field filters, disallowed query operators or functions, and deep expressions) are rejected with HTTP 400 Bad Request and structured problem details.
- **SC-005**: Multi-owner isolation achieves 100% test verification: queries executed under a simulated secondary owner token never return or expose any data belonging to another owner, and the internal owner identifier is never present in response payloads.
- **SC-006**: Web list views accurately display the server-computed total matching count and update column-header sorting across all pages, with zero instances of "sorting applies to this page only" remaining in the UI.
- **SC-007**: Server-side query execution handles a benchmark volume of 5,000 records in under 1 second for standard filtered and sorted queries.
- **SC-008**: Automated contract drift detection prevents build or merge completion if the running schema departs from the committed contract snapshot.
- **SC-009**: Following web client migration, all legacy list endpoints are retired from the API and OpenAPI contract with zero broken links across detail views, mutations, CLI, and agent tools.

## Assumptions

- **Read-Only Scope**: The query service is strictly for read and query operations; all data mutations, state transitions, and audit logging remain on the existing REST command endpoints.
- **Authentication Alignment**: The query service shares the same Identity Provider token verification, signing keys, and token lifetime policies as the existing REST API.
- **Client Query Generation**: A typed client or query builder evaluation will precede final web client integration, ensuring clean alignment with web route loader functions.
- **Contacts Isolation**: Contacts data remains CLI-only for user privacy and is explicitly excluded from the query service and web views.
- **Single Owner per Request**: Each query runs under a single authenticated owner context, maintaining the single-owner security architecture of the system.
- **External BI Follow-Up**: Desktop reporting integration with tools like Excel or Power BI is an exploratory follow-up capability; bearer-token HTTP access satisfies current requirements.
