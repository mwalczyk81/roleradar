# RoleRadar Constitution

RoleRadar is a single-owner, local-first job search tracker that an AI agent reads and writes
through a Model Context Protocol (MCP) server. Because RoleRadar is hosted in a public portfolio
repository, it MUST never contain personal job-search data. The following principles govern all
specifications, plans, tasks, and implementations in strict priority order (I through IX).

## Core Principles

### I. Privacy First (NON-NEGOTIABLE)

- Real job postings, company opinions, application records, salary figures, or personal contact
  data MUST NEVER appear in the repository, tests, fixtures, documentation, logs, or CI output.
- All automated and manual tests MUST use strictly fictional data.
- Secrets, credentials, and connection strings MUST be supplied via environment variables or local
  user secrets and MUST NEVER be committed to source control.
- `gitleaks` and path/pattern rules MUST run in CI and in the local pre-commit guard, while the
  real-name check MUST run locally against a denylist stored outside the repository and never
  committed; CI MUST NOT need the denylist.

**Rationale**: RoleRadar is a public portfolio repository backing a private personal workflow; a
single leaked fixture, log line, or commit containing real job-search or third-party contact data
permanently compromises privacy and professional confidentiality. Keeping the real-name denylist
outside the repository prevents the denylist itself from leaking private company or contact names.

### II. Test-First Development (NON-NEGOTIABLE)

- Tests MUST be written and verified to fail before production implementation code is written.
- Integration tests MUST provision isolated ephemeral databases using Testcontainers for Postgres
  and MongoDB and MUST NEVER connect to or mutate a developer's running local containers.
- Every storage port MUST have a dedicated contract test suite verifying its behavioral invariants.
- A minimum code coverage gate MUST be enforced as a blocking check in CI.

**Rationale**: Writing tests first prevents untested implementation drift, while Testcontainers and
contract test suites guarantee deterministic, repeatable verification without risking local
job-search data.

### III. Test-Enforced Layering

- `Domain`, `Application`, `Infrastructure`, and host projects MUST obey a strict one-way inward
  dependency rule verified automatically by an architecture test in CI.
- Delivery adapters (MCP tools, REST endpoints, CLI commands) MUST invoke `Application` services
  directly in-process rather than calling across network boundaries to internal adapters.

**Rationale**: Architectural boundaries degrade when left to code-review convention alone;
automated architecture tests make dependency violations fail the build immediately.

### IV. Named Seams Only

- Only the following abstractions are permitted without an approved Architecture Decision Record
  (ADR):
  - `INoteStore` for narrative storage isolation.
  - The identity-provider boundary: only the dedicated identity host project may reference Duende
    packages, while the web application MUST use generic OpenID Connect (OIDC).
  - Storage ports required to isolate Postgres and MongoDB from the `Application` and `Domain`
    layers.
- Factories, plugin frameworks, mediator libraries, and generic repositories MUST NOT be introduced
  without a prior MADR ADR justifying the complexity.

**Rationale**: Constraining abstractions to explicit, high-value boundaries prevents speculative
over-engineering while preserving the ability to swap narrative storage or identity providers.

### V. Agent Safety & Auditability

- All job-posting text and external content MUST be treated as untrusted input across every prompt,
  parser, storage boundary, and UI surface.
- MCP tools MUST be few in number, narrow in scope, and idempotent in execution.
- Hard-delete MCP tools MUST NOT exist; agent-facing tools may only archive or transition state.
- Every write operation MUST record an append-only audit entry: no update or delete path exists in
  application code, and each entry MUST capture the actor identity and tool or command name.
- MCP tool descriptions MUST be accurate and concise to minimize context overhead and ambiguity.

**Rationale**: An AI agent processes untrusted external job descriptions that could contain prompt
injections; narrow idempotent tools, no destructive deletes, and mandatory append-only audit
logging bound the blast radius of any unintended agent action.

### VI. stdio Protocol Hygiene

- In the stdio MCP host, standard output (`stdout`) is reserved exclusively for MCP protocol
  frames.
- All diagnostic, startup, error, and application logging MUST be routed to standard error
  (`stderr`).
- The stdio host MUST NOT open or bind any network listener.

**Rationale**: Any non-protocol byte written to `stdout` corrupts the JSON-RPC stream and breaks
MCP client communication, while forbidding network listeners eliminates local network attack
surfaces.

### VII. Data Durability & Safe Recovery

- Containerized datastores MUST persist data across container restarts and system reboots using
  explicitly named volumes.
- Destructive data reset or wipe operations MUST be isolated in a separate, explicitly named script
  or command that refuses to run without explicit confirmation.
- Database backups MUST be encrypted before leaving the local machine or landing in cloud-synced
  storage.

**Rationale**: As a local-first system of record, routine container lifecycle commands must never
destroy state, and off-machine backups require encryption at rest to uphold Principle I.

### VIII. Recorded Decisions & Pinned Dependencies

- Significant architectural and library decisions MUST be recorded as Markdown Architectural
  Decision Records (MADR) under `docs/adr/`.
- All NuGet package versions and container image tags MUST be explicitly pinned; floating versions
  and `:latest` container tags are forbidden.

**Rationale**: Recording decisions in MADR format preserves context across sessions, and pinning
every package and container tag guarantees reproducible builds and prevents supply-chain drift.

### IX. Baseline Technical Conventions

- All backend code MUST target .NET 10 and C# with `<Nullable>enable</Nullable>` and
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- All timestamps MUST be generated, stored, and serialized in UTC.
- Entity identifiers MUST be generated using `Guid.CreateVersion7()`, and MongoDB MUST be
  configured globally with `GuidRepresentation.Standard`.
- Every relational table and document collection MUST carry an `owner_id` field from initial
  creation.

**Rationale**: Consistent compiler strictness, time-ordered UUIDv7 identifiers, standard GUID
serialization, and pervasive `owner_id` scoping eliminate common persistence bugs and avoid costly
schema retrofits later.

## Operational & Scope Constraints

- **Priority Resolution**: When two principles come into tension, the lower-numbered principle
  always prevails (e.g., Principle I Privacy First overrides all other considerations).
- **Deferred Stack Decisions**: Concrete library, framework, and tooling selections beyond the
  invariants stated in Principles I–IX are decided during the `/speckit-plan` workflow step and
  recorded in feature plans or ADRs under `docs/adr/`, not in this constitution.
- **Public Repository Posture**: Every artifact committed to git—including sample configurations,
  seed scripts, migration tests, and documentation examples—MUST be safe for public inspection.

## Quality Gates & CI Enforcement

- **Pre-Commit & Secret Scanning**: CI MUST run `gitleaks` and path/pattern rules on every push and
  pull request without needing any private denylist; the local pre-commit guard MUST additionally
  run the real-name check against the external denylist stored outside the repository.
- **Compiler & Static Analysis Gate**: Builds MUST compile with zero warnings under
  `TreatWarningsAsErrors`.
- **Architecture & Contract Gate**: The automated architecture dependency test suite and all
  storage port contract tests MUST pass on every CI run.
- **Integration & Coverage Gate**: All Testcontainers-backed integration tests MUST pass in CI and
  meet the configured coverage threshold before any feature branch is merged.

## Governance

- **Authority**: This constitution supersedes all other repository conventions, templates, and
  ad-hoc implementation choices.
- **Amendment Procedure**: Any change to this constitution requires an explicit pull request or
  specification step documenting the rationale, an updated Sync Impact Report during review, and
  synchronized updates to affected `.specify/templates/` files or ADRs.
- **Versioning Policy**: The constitution version follows Semantic Versioning (`MAJOR.MINOR.PATCH`):
  - **MAJOR**: Removal, re-ordering, or backward-incompatible redefinition of an existing principle.
  - **MINOR**: Addition of a new principle or section, or material expansion of governance rules.
  - **PATCH**: Wording clarifications, formatting fixes, or non-semantic refinements.
- **Compliance Review**: Every `/speckit-plan`, `/speckit-tasks`, and `/speckit-analyze` artifact
  and every pull request review MUST explicitly verify compliance with Principles I through IX. Any
  proposed deviation from Principle IV (Named Seams Only) or Principle VIII requires an approved
  ADR under `docs/adr/` prior to implementation.

**Version**: 1.0.1 | **Ratified**: 2026-10-17 | **Last Amended**: 2026-10-17
