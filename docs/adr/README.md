# Architecture Decision Records (MADR Index)

All significant architectural and library decisions for RoleRadar are recorded here in Markdown Architectural Decision Record (MADR) format per Constitution Principle VIII.

| ADR | Title | Status | Feature |
| ----- | ------- | -------- | --------- |
| [ADR-0001](./0001-mongo-only-for-feature-001.md) | MongoDB Replica Set Only for Feature 001 (Multi-Document Atomicity) | Accepted | `001-memory-replacement` |
| [ADR-0002](./0002-inote-repository-single-write-port.md) | `INoteRepository` as the Aggregate-Level Transactional Write Port | Accepted | `001-memory-replacement` |
| [ADR-0003](./0003-stdio-first-mcp.md) | Stdio-First MCP Host Without Network Listeners | Accepted | `001-memory-replacement` |
| [ADR-0004](./0004-verified-mcp-sdk-usage.md) | Live-Verified NuGet Package & Docker Image Pins and API Signatures | Accepted | `001-memory-replacement` |
| [ADR-0005](./0005-revision-history-policy.md) | Revision History Policy — Full Snapshots Only for Destructive Edits | Accepted | `001-memory-replacement` |
| [ADR-0006](./0006-handlers-with-decorators-no-mediator.md) | Plain Command & Query Handlers with Explicit Decorators Instead of a Mediator Library | Accepted | `001-memory-replacement` |
| [ADR-0007](./0007-backup-streaming-aead-encryption.md) | Streamed Backup Authenticated Encryption (`Geralt` libsodium `secretstream` + `Argon2id`) and Two-Pass Restore | Accepted | `001-memory-replacement` |
| [ADR-0008](./0008-mongodb-structured-job-search-storage.md) | MongoDB Replica Set Only for Structured Job-Search Storage | Accepted | `002-job-search-tracking` |
| [ADR-0009](./0009-pattern-reuse-and-typed-results-for-feature-002.md) | Architectural Pattern Reuse and Typed Operational Failures for Feature 002 | Accepted | `002-job-search-tracking` |
| [ADR-0010](./0010-immutable-auditability-and-zero-hard-deletes.md) | Immutable Auditability, Zero Hard Deletes, and Contact Privacy Isolation | Accepted | `002-job-search-tracking` |
| [ADR-0011](./0011-hosted-mongodb-selection-and-compatibility.md) | Hosted MongoDB Database Evaluation & Compatibility Checklist | Proposed | `003-linux-container-readiness` |
| [ADR-0012](./0012-azure-container-runtime-target.md) | Azure Container Runtime Target — Container Apps vs. AKS | Proposed | `003-linux-container-readiness` |
| [ADR-0013](./0013-tracker-import-dry-run-scratch-database.md) | Tracker Import Dry-Run Mechanism — Ephemeral Scratch Database vs. Outer Rolled-Back Transaction | Accepted | `004-tracker-import` |
| [ADR-0014](./0014-csv-parsing-strategy.md) | CSV Parsing Strategy — Hand-Rolled RFC 4180 Parser vs. External Library | Accepted | `004-tracker-import` |
| [ADR-0015](./0015-identity-operational-store-selection.md) | Identity Operational Store Selection — SQLite File Volume vs. PostgreSQL Container | Accepted | `005a-api-and-login` |
| [ADR-0016](./0016-duende-identityserver-community-edition-and-fallback.md) | Duende IdentityServer Community Edition Licensing and IdP Fallback Strategy | Accepted | `005a-api-and-login` |
| [ADR-0017](./0017-owner-credential-and-security-controls.md) | Single-Owner Credential Handling and Authentication Security Controls | Accepted | `005a-api-and-login` |
| [ADR-0018](./0018-rest-api-style-and-owner-mapping.md) | REST API Style, Error Envelope, Paging, and Owner Mapping | Accepted; superseded in part by ADR-0028 and ADR-0044 | `005a-api-and-login` |
| [ADR-0019](./0019-openapi-generation-and-typed-client.md) | OpenAPI Contract Generation, Committed Snapshot Verification, and Typed Client Generation | Accepted | `005a-api-and-login` |
| [ADR-0020](./0020-https-transport-and-local-compose-certificates.md) | Transport Security, Local Compose Certificates, and Self-Contained Health Probing | Accepted | `005a-api-and-login` |
| [ADR-0021](./0021-oidc-client-library-selection.md) | OIDC Client Library Selection (`openid-client` v6) | Accepted | `005b-management-ui` |
| [ADR-0022](./0022-encrypted-in-memory-session-store.md) | Encrypted In-Memory Session Store and Single-Flight Token Refresh | Accepted | `005b-management-ui` |
| [ADR-0023](./0023-openapi-fetch-typed-api-consumption.md) | Strongly-Typed API Consumption via `openapi-fetch` and Committed OpenAPI Types | Accepted; superseded in part by ADR-0028 | `005b-management-ui` |
| [ADR-0024](./0024-nx-and-nx-dotnet-monorepo-orchestration.md) | Monorepo Orchestration with Nx and `@nx/dotnet` | Accepted | `005b-management-ui` |
| [ADR-0025](./0025-web-in-process-tls-and-dev-cert-trust.md) | Web Host In-Process TLS Termination, Dev Certificate Trust, and Port 5003 Migration | Accepted | `005b-management-ui` |
| [ADR-0026](./0026-strict-csp-nonce-and-theme-initialization.md) | Strict CSP with Nonce, Zero-FOUC Theme Initialization, and Untrusted Plain-Text Rendering | Accepted | `005b-management-ui` |
| [ADR-0027](./0027-web-test-pyramid-and-e2e-strategy.md) | Web Test Pyramid and Verification Strategy | Accepted | `005b-management-ui` |
| [ADR-0028](./0028-odata-read-seam-and-query-execution.md) | OData Read Seam and Query Execution Architecture | Accepted | `006-odata-reads` |
| [ADR-0029](./0029-insights-counts-via-shared-lens-module.md) | Insights Counts via OData `$count` and One Shared Lens Module | Accepted | `007-insights-dashboard` |
| [ADR-0030](./0030-insights-stats-read-in-memory.md) | Insights Stats Read Computed In Memory Through `IOwnerReadSeam` | Accepted | `007-insights-dashboard` |
| [ADR-0031](./0031-insights-derived-read-model-flags.md) | Derived Read-Model Flags for Insights, Written Only at the Repository Choke Point | Accepted | `007-insights-dashboard` |
| [ADR-0032](./0032-markdown-rendering-of-notes.md) | Markdown Rendering of Note Bodies | Accepted | `007-insights-dashboard` |
| [ADR-0033](./0033-posting-rejection-syncs-to-application.md) | Rejecting an Applied Posting Records the Rejection on Its Application | Accepted | `008-posting-application-rejection-sync` |
| [ADR-0034](./0034-identity-serves-kit-assets-and-csp.md) | Identity Serves the Kit Assets and a CSP for Its Sign-In Pages | Accepted | `009-ui-polish-theming` |
| [ADR-0035](./0035-generated-enum-contract.md) | Enum Values in the API Contract Are Generated, Not Snapshotted | Accepted | `010-contracts-test-integrity` |
| [ADR-0036](./0036-test-lanes-and-gate-scan.md) | Test Lanes by Trait, and a Gate That Scans Its Own Images | Accepted | `010-contracts-test-integrity` |
| [ADR-0037](./0037-mutation-testing.md) | Nightly Mutation Testing for the Web Library, .NET Deferred | Accepted | `011-test-quality-reporting` |
| [ADR-0038](./0038-reporting-stack.md) | Reporting From Pipeline Numbers, With One Combined Allure Report | Accepted | `011-test-quality-reporting` |
| [ADR-0039](./0039-demo-stack-and-guard.md) | A Separate Demo Stack With a Guard and Generated Credentials | Accepted | `012-front-door` |
| [ADR-0040](./0040-mongodb-9-and-one-image-source.md) | MongoDB 9.0, With One Source for the Image Tag | Accepted | `chore/mongo-9-upgrade` |
| [ADR-0041](./0041-operability-logging-correlation-health.md) | Structured Logs, One Correlation Id, and Health That Checks Its Store | Accepted; superseded in part by ADR-0044 | `013-operability` |
| [ADR-0042](./0042-authenticator-provider-and-replay-guard.md) | One-Time Codes Through the Platform Authenticator Provider, With a Temporary Replay Guard | Accepted | `014-standards-first` |
| [ADR-0043](./0043-backup-archive-format.md) | The Backup Archive Format Stays (`.rrbak`, libsodium secretstream and Argon2id) | Accepted | `014-standards-first` |
| [ADR-0044](./0044-problem-details-and-trace-context.md) | RFC 9457 Problem Documents and W3C Trace Context | Accepted | `014-standards-first` |
| [ADR-0045](./0045-web-retry-and-limiter-libraries.md) | Web Retry, Timeout and Concurrency Limit From ky and p-limit | Accepted | `014-standards-first` |
| [ADR-0046](./0046-log-redaction.md) | Log Redaction Stays With LogScrubber | Accepted | `014-standards-first` |
| [ADR-0047](./0047-console-formatter-and-validators.md) | The Console Formatter and the Command Validators Stay Hand-Written | Accepted | `014-standards-first` |
| [ADR-0048](./0048-scripts-and-existing-tools.md) | The Action-Pin Script Is Retired for zizmor With a Token; the Licence Audit Stays | Accepted | `014-standards-first` |
| [ADR-0049](./0049-local-observability-apphost-and-dashboard.md) | Local Observability Orchestration — Aspire AppHost and Dashboard | Accepted | `015-observability` |
| [ADR-0050](./0050-span-attribute-allowlist-processor.md) | Span Attribute Allowlist, Drop-List, and Exception-Event Sanitization Processor | Accepted | `015-observability` |
| [ADR-0051](./0051-otel-logs-and-runtime-scrubbing.md) | OTLP Structured Logs, Runtime Scrubbing Processor, and Producer Census | Accepted | `015-observability` |
| [ADR-0052](./0052-store-health-and-operation-metrics-meter.md) | Datastore Readiness, Command Health Metrics, and Metric View Tag Filtering | Accepted | `015-observability` |
| [ADR-0053](./0053-aspire-13-6-5-security-fix-release-age-waiver.md) | Aspire 13.6.5 Security Fix and the Release-Age Waiver | Accepted | `015-observability` |

## P4 outcomes

Feature 014 story P4 evaluates four hand-written mechanisms against a standard. Each ends in a swap with the existing tests unchanged, or an ADR naming the standard it lost to. The mechanisms and the standards compared are in the "Standards considered" table of [specs/014-standards-first/plan.md](../../specs/014-standards-first/plan.md#standards-considered).

| Item | Jira card | Decision and ADR |
| ------ | ----------- | ------------------ |
| Log redaction | RR-80 | Keep `LogScrubber`; `Microsoft.Extensions.Compliance.Redaction` closes no gap and removes no code. [ADR-0046](./0046-log-redaction.md) |
| Console formatter | RR-81 | Keep `RoleRadarConsoleFormatter`: no built-in or Serilog formatter has a hook for scrubbing final text (required), and a custom `ConsoleFormatter` is the platform's extension point. Failing the 17 cases is not the reason (10 test the incidental 013 shape). A custom Serilog `ITextFormatter` passes all 17 and is about 24 percent smaller (owner may overrule). Revisit when feature 015's OpenTelemetry log exporter lands; the OTLP data model may be a better record shape and a log processor may be where scrubbing moves (notes in the ADR, not decisions). [ADR-0047](./0047-console-formatter-and-validators.md) |
| Command validators | RR-81 | Keep the hand-written validators; FluentValidation reaches parity only through a workaround, and saves about 5 percent of characters. [ADR-0047](./0047-console-formatter-and-validators.md) |
| Backup archive format | RR-82 | Keep the `.rrbak` format; `age` cannot restore archives made by the current format (a conversion works only through the current decrypt code), needs a plugin binary for scripted passphrases, and releases plaintext before it finds a bad chunk (a straight pipe into `mongorestore` left 44,916 of 50,000 postings). It is better only on having a public specification. `restic` is a repository, not an archive. Any move to `age` is a separate decision for the owner. [ADR-0043](./0043-backup-archive-format.md) |
| Scripts covered by existing tools | RR-83 | Retire `verify-action-pins.ps1`: zizmor with a token (its own CI step; the other hooks never see the token) fails tag-form pins, a nonexistent SHA and a wrong version comment. The missing-comment check is intentionally dropped (the owner's call: the SHA is the pin, the comment has no security property). The PR's own CI log must show the online audits ran (no "offline mode" warning). Keep `licence-audit.mjs`: no tool fails on a missing or empty licence field (Trivy omits it, `pnpm licenses` only lists, Dependency Review documents it will not fail). [ADR-0048](./0048-scripts-and-existing-tools.md) |
