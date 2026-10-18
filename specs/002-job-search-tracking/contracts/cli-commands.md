# Contract: CLI Commands (`RoleRadar.Cli`) — Feature 002

**Feature**: `002-job-search-tracking`  
**Host**: `RoleRadar.Cli` (`System.CommandLine` `2.0.13` GA).  
**Rules**: Commands use injected handlers directly (no mediator, no generic repository). Standard exit codes: `0` (success), `1` (unhandled error/datastore offline), `2` (invalid arguments/validation failure), `3` (integrity issues found).

---

## 1. `roleradar verify`

Executes cross-collection referential integrity verification across all notes, postings, applications, companies, criteria, and contacts.

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- verify [--json] [--fix-advisory]
```

### Options & Arguments

- `--json` (`Option<bool>`): Emit structured JSON report on stdout instead of human-readable text.
- `--fix-advisory` (`Option<bool>`): Optional flag that removes broken dangling duplicate pointers (sets `duplicate_of = null`) with audit logging. Never mutates notes or deletes documents.

### Behavioral Contract

1. **Checks Performed**:
   - **Notes Linkage**: Notes referencing `company_id`, `posting_id`, or `application_id` verify that the referenced document exists in `companies`, `postings`, or `applications`.
   - **Posting Company Reference**: `postings.company_id` exists in `companies`.
   - **Duplicate Pointers**: `postings.duplicate_of` exists, does not point to itself, and does not point to another duplicate posting (chain depth <= 1).
   - **Application Posting Reference**: `applications.posting_id` exists in `postings`.
   - **Contact Company/Application Reference**: `contacts.company_id` exists in `companies`, and optional `application_id` exists in `applications`.
   - **Posting Signal References**: `posting_signals.posting_id` exists in `postings`.
   - **Application Event References**: `application_events.application_id` exists in `applications`.
2. **Exit Code & Output**:
   - Exit `0`: All referential integrity checks passed (`issueCount: 0`).
   - Exit `3`: Integrity issues found. Details printed to stdout (or structured JSON).

### JSON Output Schema (`--json`)

```json
{
  "verifiedAtUtc": "2026-10-17T20:30:00Z",
  "status": "clean | issues_detected",
  "issueCount": 0,
  "issues": [
    {
      "sourceCollection": "notes",
      "sourceId": "0199f2c0-0000-7000-8000-000000000001",
      "targetCollection": "postings",
      "targetId": "0199f2c0-0000-7000-8000-000000000999",
      "issueKind": "dangling_reference",
      "detail": "Note references posting_id that does not exist in postings collection."
    }
  ]
}
```

---

## 2. `roleradar status` (Extension for Feature 002)

Extends the existing `roleradar status` CLI command to include structured job search tracking metrics alongside notes and audit counts, **while strictly guaranteeing zero contact or personal data exposure**.

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- status [--json]
```

### Extensions & Privacy Contract

1. **Metrics Reported**:
   - Active companies count, broken down by status (`target`, `watch`, `avoid`, `neutral`).
   - Postings count by disposition (`lead`, `scored`, `unscored`, `applied`, `screened_out`, `closed`, `rejected`).
   - Applications count by active status.
   - Criteria count (active vs total).
   - Contacts count (total count and redacted count).
2. **Zero Privacy Leakage (`FR-024`)**:
   - Status output **never** outputs contact names, companies, roles, emails, or notes.
   - Outputs only numerical aggregates for contacts (`contacts_total`, `contacts_redacted`).

### Plain Text Sample Output

```text
RoleRadar System Status
-----------------------
Datastore:       Connected (rs0)
Notes:           18 active (0 archived)
Revisions:       2
Audit Entries:   24

Job Search Tracker:
  Companies:     12 (target: 5, watch: 4, avoid: 1, neutral: 2)
  Postings:      48 (applied: 6, scored: 12, lead: 20, decided: 10)
  Applications:  6 active
  Criteria:      8 rules (active: 8)
  Contacts:      15 stored (1 redacted)
```
