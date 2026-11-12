# Changelog

All notable changes to RoleRadar are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Feature numbers (001 to 015) refer to the specifications under [specs/](specs/); each one went from spec to plan to tasks to a reviewed pull request.

## [Unreleased]

## [0.1.0] - 2026-11-12

The first release. Entries are grouped by capability; the specs and pull requests carry the detail.

### Added

- **Agent memory over MCP.** A stdio MCP server whose note tools create, read, append to, replace a section of, search and archive Markdown notes, with provenance markers and revision history. No tool deletes anything (001, #1).
- **Job-search tracker.** Companies, postings with three-tier deduplication, a disposition state machine, screening criteria revisions, applications with event timelines, private contacts, search runs and posting signals, all reachable from the same MCP server (002, #2).
- **Tracker import.** A dry-run-first import of an existing tracker's CSV and JSON exports, with schema validation, deduplication and a disposition histogram (004, #9).
- **REST API and sign-in.** A REST command API with RFC 9457 problem documents for every error and a committed OpenAPI contract, and a Duende IdentityServer host that signs in the single owner with a password and a TOTP code (005a, #20).
- **Web app.** A server-rendered React Router v7 app for postings, companies, applications, criteria, notes and search runs, with CSRF checks on every form and a strict content security policy (005b, #28).
- **OData reads.** Every list page reads through one owner-scoped OData seam with server-side sorting, filtering and paging. Derived read-model fields are written at the repository choke point and can be backfilled with `reconcile-read-models` (006, #41).
- **Insights.** A dashboard with a postings funnel and an applications funnel, rate tiles and trend charts, where every count links to the list it counts. Note bodies render as locked-down Markdown (007, #51).
- **Rejection sync.** Rejecting an applied posting records the rejection on its application, idempotently, with warnings for the edge cases (008, #53).
- **Quality reporting.** Mutation testing of the web library code with a committed floor, a combined Allure report, coverage and trends published to a reports site, a pull-request summary comment and property tests (011, #61, #63, #66).
- **One-command demo and front door.** A separate Compose project over a fictional data set, with a throwaway sign-in generated at run time and a guard that keeps it away from real data; reviewed screenshots, a rewritten README, a documentation index, a code of conduct and this changelog (012, #68).
- **Operability.** Structured, secret-scrubbed logs in text or JSON with a trace id on every line and an opt-in local log viewer, real audit actors with a read-only `roleradar audit list`, and truthful health: `/health/live` and `/health/ready`, with a bounded 503 when the database is down (013, #76).
- **Standards over hand-written code.** Errors are RFC 9457 problem documents (`docs/api/problems.md`, ADR 0044), and a request's id is its W3C trace id, carried by `traceparent` (the audit field keeps its name `request_correlation_id` and holds the trace id). The one-time code at sign-in is checked by the ASP.NET Core Identity authenticator provider, and the web's retry, timeout and concurrency limit come from `ky` and `p-limit`. Where a standard or library did not fit, the decision to keep our own code is recorded: the log scrubber, the console formatter and validators, and the backup format (ADRs 0043, 0046, 0047) (014, #83 to #90).
- **Observability.** OpenTelemetry traces, logs and metrics across the web app, API and Identity (and, opt in, the CLI and MCP server), exported over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. A privacy gate keeps secrets, query strings, statement text and personal identifiers out of every signal, metric labels are limited to an allowlist, and the log scrubber stays as the backstop. One command, `dotnet run --project src/RoleRadar.AppHost`, starts the stack from source with the Aspire Dashboard on loopback ports of its own, isolated from a running stack (015, #91 to #96).

### Changed

- **UI polish and theming.** One visual identity across the web app and the sign-in pages, light and dark themes, no inline styles anywhere and a stricter content security policy (009, #54).
- **Contracts and test integrity.** Enum values in the web client are generated from the API contract, test classes are sorted into lanes, and flaky tests were diagnosed and fixed (010, #55).
- **MongoDB 9.0.** The data store runs MongoDB 9.0.4 with the feature compatibility version held at 8.0, from one source for the image tag (#74).
- **A real 503 is not retried by the web.** The web honours `Retry-After` on a 503: a wait of up to 2 seconds is waited out and retried, a longer one means no retry. The API always sends `Retry-After: 5`, so a real 503 from the API is returned at once (a network error, a timeout, 502, 504 and a 503 without the header are still retried; reads at most 3 attempts, writes once). The bound is one constant, `RETRY_AFTER_BOUND_MS` (ADR 0045).
- **Action pins.** CI checks workflow action pins with zizmor alone, in its own step with the workflow token, so a pin to a commit that does not exist is caught; a missing `# vX.Y.Z` comment is no longer an error (ADR 0048).
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
- **One-time codes work once.** A code that was already accepted is rejected as a failed attempt (it counts toward the lockout and raises the one-time-code-failed event), including when two sign-ins submit it together and after a restart. The enrolment code therefore cannot sign in straight away: wait for the next code (ADR 0042).
- **Telemetry is off by default and scrubbed.** With no OTLP endpoint no exporter is registered and no connection is opened. A secret that appeared in an exported log record during the manual check (a PKCE code verifier in an object-valued attribute) is now scrubbed at the shared choke point (ADR 0051).
- **Patched Aspire.** The local dev loop pins Aspire 13.6.5, the vendor fix for a High advisory, under a recorded release-age exception (ADR 0053).

[Unreleased]: https://github.com/mwalczyk81/roleradar/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/mwalczyk81/roleradar/releases/tag/v0.1.0
