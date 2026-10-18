# ADR 0010: Immutable Auditability, Zero Hard Deletes, and Contact Privacy Isolation

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `002-job-search-tracking`

## Context and Problem Statement

Constitution Principle I (Privacy First) and Principle V (Agent Safety & Auditability) mandate that:

1. Real job postings, companies, and third-party personal contact data must never leak into logs, status output, test fixtures, or general search results.
2. AI agents reading and executing MCP tools must never have access to destructive hard-delete operations.
3. Every write operation must produce an append-only audit log entry inside the same database transaction.

In Feature 002, contacts (recruiters, hiring managers, referrals) represent sensitive third-party personal data. At the same time, users may need to remove or deactivate contacts, close postings, or archive screening rules without violating the zero hard-delete invariant or leaking names in audit before/after diffs.

How should entity deactivation, contact privacy isolation, and audit logging be architected to satisfy these strict non-negotiable principles?

## Decision Drivers

- **Zero Hard Deletes (Principle V & FR-043)**: No delete or drop operations exist in application repositories, commands, or MCP tools.
- **Strict Contact Privacy (Principle I & FR-028–FR-033)**: Third-party personal contact details must never appear in general search results, logs, errors, status outputs, or audit summaries. Contacts must be completely excluded from MCP tools.
- **Audit Completeness**: 100% of write operations must produce an immutable audit entry in `audit_log` inside the transaction.

## Decision Outcome

Chosen design: **Deactivation State Machine + CLI-Only Contact Management & Redaction + Privacy-Preserving Audit Summaries**.

### 1. Zero Hard Deletes Across All Repositories and Tools

- Deactivation of any entity is represented strictly through explicit domain states:
  - Postings: `closed`, `screened_out`, `rejected`, or `duplicate_of_id`.
  - Criteria: marked superseded by populating `effective_to` and setting `is_active: false`.
  - Companies: status set to `avoid` or `neutral`.
  - Contacts: explicit CLI `redact` operation.
- Architecture tests assert that no repository interface or implementation contains methods named `Delete*`, `Remove*`, or `Drop*`.
- Architecture tests assert that no MCP tool name contains `delete`, `remove`, or `drop`.

### 2. Contact Privacy Isolation & CLI-Only Access

- **CLI-Only Access (`roleradar contact add|get|redact`)**: Contacts are managed exclusively through the CLI by direct primary key. No contact data or contact query endpoints exist on the MCP server surface. Applications reference contacts solely through an optional binary GUID (`referral_contact_id`).
- **Audit Privacy Summary**: For write operations on contacts, the `AuditContentSummary` is generated using a restricted contact summary builder that records ONLY the changed field names and the contact record ID (e.g. `fields: ["name", "role", "company_affiliation", "relationship", "notes"]`). Contact names, roles, notes, or employer names are strictly excluded from the audit log payload.
- **Redaction Operation**: When a user requests removal of a contact via `roleradar contact redact`, the system executes an explicit `RedactContactCommand`. This operation blanks personal fields (`name = "[REDACTED]"`, `role = null`, `company_affiliation = null`, `relationship = "redacted"`, `notes = null`) and sets `is_redacted: true` while preserving the stable binary GUID record ID and recording an audited `contact_redact` event.

### 3. Automated Leak & Architecture Verification

Automated test suites will:

1. Scan logs, CLI status outputs, error messages, and general search results to assert zero contact names or details appear.
2. Inspect the raw BSON `audit_log` entries for contact creations, updates, and redactions to assert that only field names and IDs are present.
3. Architecture test in `RoleRadar.Tests.Architecture` inspects all classes in `RoleRadar.Mcp.Stdio` to assert that no tool name, parameter name (other than `referralContactId`), or description contains or references contact data.

### Consequences

- **Good**: 100% compliance with repository privacy principles; zero hard-delete blast radius for agent interactions.
- **Good**: Full audit traceability without leaking personal details in audit history.
- **Good**: Test-enforced architecture ensures future tool additions cannot introduce delete mechanisms or expose contact data over MCP.
