# Changelog

All notable changes to RoleRadar are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Feature numbers (001 to 012) refer to the specifications under [specs/](specs/); each one went from spec to plan to tasks to a reviewed pull request.

## [Unreleased]

### Changed

- **Breaking: API errors are RFC 9457 problem documents.** Every error under `/api/v1` and `/odata` is `application/problem+json` with `type`, `title`, `status`, `detail`, `errorCode` and `traceId` (and `errors` for field problems), replacing the private `{ ok, errorCode, message, details, correlationId }` envelope. The codes are listed in `docs/api/problems.md`. See ADR 0044.
- **Breaking: W3C Trace Context replaces `X-Correlation-ID`.** The id of a request is its W3C trace id (32 hex characters), carried by the `traceparent` header. The `X-Correlation-ID` header is no longer read, written or returned, the log field `correlationId` is now `traceId`, and the CLI run id and the MCP per-call id are trace ids too. The audit field keeps its name `request_correlation_id` and now holds the trace id for new entries; older entries keep their values. The web app now reads field errors and lower-case error codes correctly (RR-64).
- The one-time code at sign-in and at enrolment is checked by the ASP.NET Core Identity authenticator provider instead of hand-written RFC 6238 code. Existing enrolments and secrets keep working with no re-enrolment. The accepted window is the provider's: the code for the current 30-second step and two steps either side. The demo `totp` command and the enrolment URI use Otp.NET; the URI now also states the default `algorithm`, `digits` and `period` ([ADR 0042](docs/adr/0042-authenticator-provider-and-replay-guard.md)).

### Security

- A one-time code can be used only once. A code that was already accepted is rejected as a failed attempt (it counts toward the lockout and raises the one-time-code-failed event), including when two sign-ins submit it together and after a restart. The enrolment code therefore cannot sign in straight away: wait for the next code.

## [0.1.0] - TBD

The first release. Entries are grouped by capability; the specs and pull requests carry the detail.

### Added

- **Agent memory over MCP.** A stdio MCP server whose note tools create, read, append to, replace a section of, search and archive Markdown notes, with provenance markers and revision history. No tool deletes anything (001, #1).
- **Job-search tracker.** Companies, postings with three-tier deduplication, a disposition state machine, screening criteria revisions, applications with event timelines, private contacts, search runs and posting signals, all reachable from the same MCP server (002, #2).
- **Tracker import.** A dry-run-first import of an existing tracker's CSV and JSON exports, with schema validation, deduplication and a disposition histogram (004, #9).
- **REST API and sign-in.** A REST command API with one error envelope and a committed OpenAPI contract, and a Duende IdentityServer host that signs in the single owner with a password and a TOTP code (005a, #20).
- **Web app.** A server-rendered React Router v7 app for postings, companies, applications, criteria, notes and search runs, with CSRF checks on every form and a strict content security policy (005b, #28).
- **OData reads.** Every list page reads through one owner-scoped OData seam with server-side sorting, filtering and paging. Derived read-model fields are written at the repository choke point and can be backfilled with `reconcile-read-models` (006, #41).
- **Insights.** A dashboard with a postings funnel and an applications funnel, rate tiles and trend charts, where every count links to the list it counts. Note bodies render as locked-down Markdown (007, #51).
- **Rejection sync.** Rejecting an applied posting records the rejection on its application, idempotently, with warnings for the edge cases (008, #53).
- **Quality reporting.** Mutation testing of the web library code with a committed floor, a combined Allure report, coverage and trends published to a reports site, a pull-request summary comment and property tests (011, #61, #63, #66).
- **One-command demo and front door.** A separate Compose project over a fictional data set, with a throwaway sign-in generated at run time and a guard that keeps it away from real data; reviewed screenshots, a rewritten README, a documentation index, a code of conduct and this changelog (012, #68).

### Changed

- **UI polish and theming.** One visual identity across the web app and the sign-in pages, light and dark themes, no inline styles anywhere and a stricter content security policy (009, #54).
- **Contracts and test integrity.** Enum values in the web client are generated from the API contract, test classes are sorted into lanes, and flaky tests were diagnosed and fixed (010, #55).
- **Dependencies.** Nineteen grouped Dependabot updates for NuGet, npm, Docker images and GitHub Actions, for example #56 to #59.

### Removed

- The legacy list endpoints, replaced by the OData reads (006, #41).

### Fixed

- MCP tool schemas: explicit parameter types, server-side numeric tolerance, signal shorthand and update semantics that keep omitted fields (#16, #17, #18).
- Tracker import: company-scoped requisition ids, CSV schema checks, owner resolution and restore database remapping (#10, #12, #13, #14).
- The Identity image now ships its sign-in pages; the Docker ignore rules had dropped them (#21).

### Security

- **Local-first by design.** MongoDB binds to loopback only, the MCP server speaks stdio and opens no network listener, and every write records an append-only audit entry (001, 002).
- **Encrypted backups.** Backups stream through libsodium `secretstream` (XChaCha20-Poly1305) with an Argon2id key, restore verifies the whole archive before writing, and passphrases are never taken from the command line (001).
- **Container readiness.** Multi-architecture Linux images that run as a non-root user, and test guards that refuse to touch a developer's running stack (003, #3).
- **Scanned images.** The review gate scans every image it builds with a pinned Trivy and cannot pass on a skipped scan (010, #55).

[Unreleased]: https://github.com/mwalczyk81/roleradar/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/mwalczyk81/roleradar/releases/tag/v0.1.0
