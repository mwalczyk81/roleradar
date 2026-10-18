# Contract: CLI Commands (`RoleRadar.Cli`) — Feature 002

**Feature**: `002-job-search-tracking`  
**Host**: `RoleRadar.Cli` (`System.CommandLine` `2.0.13` GA).  
**Rules**: Commands use injected handlers directly (no mediator, no generic repository). Standard exit codes: `0` (success), `1` (unhandled error/datastore offline), `2` (invalid arguments/validation failure), `3` (integrity issues found). Contacts management and referential integrity verification are strictly CLI-only.

---

## 1. `roleradar contact` (Confidential Contacts Management)

Provides CLI-only management of private third-party professional contacts (recruiters, referrals, hiring managers, peers). Completely unreachable through MCP tools. Contact personal data is strictly isolated from logs, errors, status output, and general search queries.

### 1.1 `roleradar contact add`

Creates a new confidential contact record associated with a company or application.

#### Synopsis
```powershell
dotnet run --project src/RoleRadar.Cli -- contact add --name <name> [--role <role>] [--company <companyName>] [--company-id <guid>] [--relationship <rel>] [--notes <notes>] [--json]
```

#### Options & Arguments
- `--name` (`Option<string>`, required, 1..150 chars): Full name of the contact.
- `--role` (`Option<string>`, optional, max 150 chars): Current organizational title.
- `--company` (`Option<string>`, optional, max 150 chars): Company affiliation name.
- `--company-id` (`Option<Guid?>`, optional): Company binary GUID reference.
- `--relationship` (`Option<string>`, optional, default `"peer"`): `"recruiter"`, `"referral"`, `"hiring_manager"`, or `"peer"`.
- `--notes` (`Option<string>`, optional, max 500 chars): Context and discussion notes.
- `--json` (`Option<bool>`): Output JSON format.

#### Behavioral Contract & Privacy Guard (`FR-028`, `FR-031`)
- Inserts document into `contacts` collection with binary GUID `_id` and initial `version_token`.
- Writes an append-only `audit_log` entry inside the transaction where `AuditContentSummary` records **only** the contact ID and changed field names (`fields: ["name", "role", "company_affiliation", "relationship", "notes"]`). **Zero personal text or names are persisted to `audit_log`**.

---

### 1.2 `roleradar contact get`

Retrieves a single contact record strictly by its binary GUID identifier.

#### Synopsis
```powershell
dotnet run --project src/RoleRadar.Cli -- contact get --id <guid> [--json]
```

#### Options & Arguments
- `--id` (`Option<Guid>`, required): Unique binary GUID identifier of the contact.
- `--json` (`Option<bool>`): Output JSON format.

#### Behavioral Contract (`FR-033`)
- Retrieves contact directly by primary key.
- If not found or if already redacted (`is_redacted: true`), returns typed `NotFound` failure or redacted placeholder.
- There is **no listing or fuzzy searching** command for contacts on CLI or MCP (`FR-029`, `FR-033`).

---

### 1.3 `roleradar contact redact`

Blanks all personal fields while retaining the stable document ID and recording a compliant audit entry without violating the zero hard-delete constraint.

#### Synopsis
```powershell
dotnet run --project src/RoleRadar.Cli -- contact redact --id <guid> [--version-token <guid>] [--force] [--json]
```

#### Options & Arguments
- `--id` (`Option<Guid>`, required): Identifier of contact to redact.
- `--version-token` (`Option<Guid?>`, optional): Optimistic concurrency token.
- `--force` (`Option<bool>`, optional): Bypass interactive confirmation prompt.
- `--json` (`Option<bool>`): Output JSON format.

#### Behavioral Contract (`FR-032`)
- Updates document: sets `name = "[REDACTED]"`, `role = null`, `company_affiliation = null`, `company_id = null`, `relationship = "redacted"`, `notes = null`, and `is_redacted = true`.
- Rotates `version_token`.
- Appends `audit_log` entry recording operation `"contact_redact"` with summary containing only field names (`["name", "role", "company_affiliation", "company_id", "relationship", "notes", "is_redacted"]`) and record ID.

---

## 2. `roleradar verify` (Referential Integrity Verification)

Executes cross-collection referential integrity verification across all notes, postings, applications, companies, criteria, and contacts.

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- verify [--json] [--fix-advisory]
```

### Options & Arguments

- `--json` (`Option<bool>`): Emit structured JSON report on stdout instead of human-readable text.
- `--fix-advisory` (`Option<bool>`): Optional flag that removes broken dangling duplicate pointers (sets `duplicate_of_id = null`) with audit logging. Never mutates notes or deletes documents.

### Behavioral Contract (`FR-036`, `FR-037`)

1. **Checks Performed**:
   - **Notes Linkage**: Notes referencing `company_id`, `posting_id`, or `application_id` verify that the referenced document exists in `companies`, `postings`, or `applications`.
   - **Posting Company Reference**: `postings.company_id` exists in `companies`.
   - **Duplicate Pointers**: `postings.duplicate_of_id` exists, does not equal self, and does not point to another duplicate posting (chain depth <= 1).
   - **Application Posting Reference**: `applications.posting_id` exists in `postings`.
   - **Application Referral Contact Reference**: `applications.referral_contact_id` (if non-null) exists in `contacts`.
   - **Posting Signal References**: `posting_signals.posting_id` exists in `postings`.
   - **Application Event References**: `application_events.application_id` exists in `applications`.
2. **Exit Code & Output**:
   - Exit `0`: All referential integrity checks passed (`issueCount: 0`).
   - Exit `3`: Integrity issues found. Details printed to stdout (or structured JSON).

---

## 3. `roleradar status` (Extension for Feature 002)

Extends the existing `roleradar status` CLI command to include structured job search tracking metrics alongside notes and audit counts, **while strictly guaranteeing zero contact or personal data exposure**.

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- status [--json]
```

### Privacy & Metric Contract (`FR-030`)

1. **Metrics Reported**:
   - Active companies count, broken down by status (`target`, `watch`, `avoid`, `neutral`).
   - Postings count by disposition (`lead`, `scored`, `unscored`, `applied`, `screened_out`, `closed`, `rejected`).
   - Applications count by active status.
   - Criteria count (active vs total).
   - Contacts count (total count and redacted count).
2. **Zero Privacy Leakage**:
   - Status output **never** outputs contact names, companies, roles, or notes.
   - Outputs only numerical aggregates for contacts (`contacts_total`, `contacts_redacted`).

---

## 4. Automated Testing & Verification Requirements

The following automated tests are mandated to enforce CLI and contact invariants:

1. **Contact Redaction Test**:
   - Unit and integration tests verify that calling `contact redact` overwrites personal fields with `[REDACTED]`/`null`, sets `is_redacted: true`, and verifies that subsequent `contact get` returns the redacted state.
2. **Contact Audit Summary Privacy Test**:
   - Contract test inspects raw BSON documents in `audit_log` created during `contact add`, `contact update`, and `contact redact` calls, asserting that the `content_summary` contains strictly field names and IDs, with zero occurrences of the contact's name, role, company, or notes.
3. **No-Leak Privacy Scanner Test**:
   - Integration test runs `roleradar status`, logs, and general searches on a seeded database containing sensitive test contact names, asserting that zero contact names or personal details appear anywhere in standard outputs or error messages.
4. **Architecture Test (MCP Tool Surface Guard)**:
   - Reflection test in `RoleRadar.Tests.Architecture` inspects all classes implementing MCP tools in `RoleRadar.Mcp.Stdio`:
     - Asserts that no tool name contains `contact`.
     - Asserts that no tool parameter name contains `contact` (except `referralContactId` on `application_record`).
     - Asserts that no tool description contains contact personal details or references contact query endpoints.
