# Quickstart & Verification Guide: Feature 002 (Job Search Tracking)

This guide walks through step-by-step verification of all Feature 002 capabilities: company profiles, tiered posting deduplication, state transitions, atomic reopen-and-apply override, criteria revisioning, CLI-only contact privacy and redaction, cross-collection referential integrity verification, and backup/restore round-trip.

---

## 1. Prerequisites & Services Setup

Ensure the MongoDB replica set is running and initialized:

```powershell
pwsh ./scripts/start-services.ps1
```

Confirm health via CLI status:

```powershell
dotnet run --project src/RoleRadar.Cli -- status
```

---

## 2. Test Flow Walkthrough

### Step 2.1: Record a Target Company with Career Sources

Record a company via MCP tool `company_record`:

```json
{
  "name": "Acme Software Corp",
  "description": "Enterprise cloud compliance and infrastructure software; VC-backed Series B; HQ in Austin.",
  "parentCompany": "Acme Holdings LLC",
  "ownershipType": "vc_backed",
  "headquarters": "Austin, TX",
  "status": "target",
  "categoryTags": ["cloud", "infrastructure", "security"],
  "careerSources": [
    {
      "atsType": "Greenhouse",
      "tenant": "acmesoftware",
      "slug": "acme-careers",
      "fetchMethod": "api",
      "notes": "Greenhouse public API boards.greenhouse.io/v1/boards/acmesoftware/jobs"
    }
  ]
}
```

Verify result: Company created with generated binary GUID `id`, status `target`, and 1 career source.

---

### Step 2.2: Record Postings & Verify Deduplication Tiers (A, B, C, D)

#### 1. Ingest Initial Posting (Tier A Baseline)
Record a posting with ATS tenant and requisition ID:

```json
{
  "companyId": "<acme_company_guid>",
  "title": "Staff Backend Engineer",
  "url": "https://boards.greenhouse.io/acmesoftware/jobs/4012948002?gh_src=linkedin",
  "sourceKind": "ats",
  "atsTenant": "acmesoftware",
  "requisitionId": "4012948002",
  "location": "Austin, TX (Hybrid)",
  "remoteType": "hybrid",
  "compensationMin": 175000,
  "compensationMax": 205000,
  "compensationCurrency": "USD",
  "compensationText": "$175,000 - $205,000 + equity",
  "resurfacePolicy": "if_reopened"
}
```

Verify result: Posting created in `lead` disposition. `url` is normalized to `https://boards.greenhouse.io/acmesoftware/jobs/4012948002`. Computed key `dedup_key_a = "acmesoftware|4012948002"` is indexed in partial unique index.

#### 2. Re-submit Same Role via Aggregator (Tier A Dedup Check)
Submit same role from an aggregator on a different UTC date with matching tenant and requisition ID:

```json
{
  "companyId": "<acme_company_guid>",
  "title": "Staff Engineer",
  "url": "https://www.linkedin.com/jobs/view/999888777",
  "sourceKind": "aggregator",
  "atsTenant": "acmesoftware",
  "requisitionId": "4012948002",
  "remoteType": "hybrid"
}
```

Verify result: Returns `status: "already_tracked"`, referencing the existing posting GUID. `last_verified_at` is updated as a real write, generating an audit entry with a one-field summary (`last_verified_at`). No duplicate document inserted. Submitting again on the same UTC day writes nothing and creates no audit entry.

#### 3. Test Ingest Dedup Match with Differing Fields (Differences List)
Submit the same role via aggregator but with differing compensation and remote type:

```json
{
  "companyId": "<acme_company_guid>",
  "title": "Staff Backend Engineer",
  "sourceKind": "aggregator",
  "atsTenant": "acmesoftware",
  "requisitionId": "4012948002",
  "remoteType": "remote",
  "compensationMin": 190000
}
```

Verify result: Returns `status: "already_tracked"`. Existing posting fields are **not modified**; instead, response includes `differences: [ { "field": "remote_type", "stored": "hybrid", "supplied": "remote" }, { "field": "compensation_min", "stored": 175000, "supplied": 190000 } ]`.

#### 4. Test Explicit Update Mode
Call `posting_record` providing `postingId` and current `versionToken`:

```json
{
  "postingId": "<posting_guid>",
  "versionToken": "<current_version_token>",
  "location": "Austin, TX (Remote Eligible)",
  "remoteType": "remote",
  "score": 9.0
}
```

Verify result: Returns `status: "updated"`. Modifies only supplied fields (`location`, `remote_type`, `score`). `version_token` rotates. Audit log entry summary records `fields: ["location", "remote_type", "score"]`. Passing an outdated `versionToken` returns `ErrorCode.Conflict` (`ConcurrencyConflict`).

#### 5. Test Tier D Advisory Match
Submit a role with no requisition ID and a matching normalized title for the same company:

```json
{
  "companyId": "<acme_company_guid>",
  "title": "Staff Backend Engineer",
  "sourceKind": "aggregator",
  "remoteType": "hybrid"
}
```

Verify result: Returns `status: "possible_duplicate"`, listing candidate GUIDs and dispositions. Record is **not** auto-created or auto-merged.

---

### Step 2.3: Disposition Transitions & Decided Handling

#### 1. Screen Out Posting
Transition posting to `screened_out` referencing a criterion (`resurfacePolicy` defaults to `"never"` if omitted, but can be set to `"if_reopened"`):

```json
{
  "postingId": "<posting_guid>",
  "targetDisposition": "screened_out",
  "reason": "Required relocation to Dublin, Ireland; conflicting with remote criteria.",
  "resurfacePolicy": "if_reopened",
  "versionToken": "<current_token>"
}
```

Verify result: Posting disposition updated to `screened_out` and `decided_at` is set to the current UTC timestamp. `audit_log` records state transition. Attempting to transition directly to `applied` via `posting_transition` is rejected with `ErrorCode.InvalidTransition`. Stored `resurface_policy` governs future transitions: returning to `lead` is rejected if stored policy is `"never"`.

#### 2. Re-encounter Decided Posting
Submit posting again via `posting_record`:

Verify result: Returns `status: "already_decided"`, `priorReason: "Required relocation...", resurfacePolicy: "if_reopened", canReopen: true`. Data store is untouched (no `last_verified_at` update, zero audit entries).

---

### Step 2.4: Application Management & Events

#### 1. Atomic Reopen-and-Apply with Override
Attempt to apply to the screened-out posting without override:
- Call `application_record` with `action: "create"`, `overrideDecided: false`.
- Verify result: Rejected with `ErrorCode.InvalidTransition`.

Apply with explicit override:
- Call `application_record` with:
  ```json
  {
    "postingId": "<posting_guid>",
    "action": "create",
    "submittedDate": "2026-10-17",
    "desiredSalaryMin": 185000,
    "desiredSalaryMax": 200000,
    "desiredSalaryCurrency": "USD",
    "resumeVariant": "tailored",
    "resumePointer": "local://resumes/acme-staff-2026.pdf",
    "overrideDecided": true,
    "overrideReason": "Hiring manager reached out: role opened for US remote."
  }
  ```
- Verify result: Succeeded in a single transaction. Posting disposition moved to `applied`, application created, and `audit_log` captures previous disposition (`screened_out`), previous policy (`if_reopened`), and override justification.

#### 2. Append Event to Decided / Terminal Posting
Append an event to an application whose posting has reached a terminal or decided disposition (`rejected` or `closed`):
- Call `application_record` with:
  ```json
  {
    "action": "append_event",
    "applicationId": "<application_guid>",
    "eventType": "rejected",
    "eventDate": "2026-10-25",
    "note": "Automated status notification received after final interview."
  }
  ```
- Verify result: The event is appended to `application_events` and audited. If the posting is already in `rejected` or `closed`, the posting disposition remains unchanged (no invalid transition error, no re-mutation of decided state).

---

### Step 2.5: Point-in-Time Criteria Revisioning & Search Runs via `tracker_log`

#### 1. Insert and Revise Screening Criteria
1. Insert screening criterion via `tracker_log` effective `2026-01-01`:
   ```json
   {
     "action": "criterion_revision",
     "key": "min_base_salary",
     "category": "hard_skip",
     "value": "180000",
     "provenance": "stated",
     "effectiveFrom": "2026-01-01T00:00:00Z"
   }
   ```
2. Attempt revision without `versionToken`:
   - Call `tracker_log` with `action: "criterion_revision"`, `key: "min_base_salary"`, `value: "200000"`, `effectiveFrom: "2026-07-01T00:00:00Z"`, omitting `versionToken`.
   - Verify result: Rejected with `ErrorCode.Conflict` (`ConcurrencyConflict`).
3. Attempt revision with non-sequenced `effectiveFrom`:
   - Call `tracker_log` with `effectiveFrom: "2025-12-01T00:00:00Z"` and valid `versionToken`.
   - Verify result: Rejected with `ErrorCode.Validation` (`effectiveFrom` must be strictly later than current active version's `effective_from`).
4. Successfully revise criterion:
   - Call `tracker_log` with valid `versionToken` and `effectiveFrom: "2026-07-01T00:00:00Z"`.
   - Verify result: Previous version's `effective_to` is set to `2026-07-01T00:00:00Z` and `is_active` set to `false`. New version created with `is_active: true`.
5. Deactivate criterion without replacement:
   - Call `tracker_log` with `action: "criterion_deactivate"`, `key: "min_base_salary"`, `versionToken: "<v2_token>"`, `effectiveFrom: "2026-09-01T00:00:00Z"`.
   - Verify result: Active version's `is_active` set to `false` and `effective_to` set to `2026-09-01T00:00:00Z`. Fully audited.

#### 2. Query Criteria Point-in-Time
- Query `tracker_query` with `entity: "criteria"`, `effectiveDate: "2026-03-15"`: Returns version 1 ($180k).
- Query `tracker_query` with `effectiveDate: "2026-07-15"`: Returns version 2 ($200k).
- Query `tracker_query` with `effectiveDate: "2026-10-01"`: Returns no active criterion for that key.

#### 3. Search Run Logging & Deduplication
1. Log search run via `tracker_log`:
   ```json
   {
     "action": "search_run",
     "startedAt": "2026-10-17T14:00:00Z",
     "completedAt": "2026-10-17T14:15:00Z",
     "companiesChecked": 12,
     "postingsFound": 3,
     "notes": "Weekly sweep"
   }
   ```
   - Verify result: Created in `search_runs` collection and logged in `audit_log`.
2. Resubmit search run with identical `startedAt`:
   - Verify result: Unique index on `(owner_id, started_at)` catches collision; returns `status: "already_logged"` and existing run details without duplicate insertion or audit write.

### Step 2.6: Confidential Contact Privacy & CLI Redaction

1. Add contact via CLI:
   ```powershell
   dotnet run --project src/RoleRadar.Cli -- contact add --name "Jane Doe" --role "Lead Technical Recruiter" --company "Acme Software Corp" --company-id "<acme_company_guid>" --relationship "recruiter" --notes "Met at tech conference Oct 2026."
   ```
2. Inspect `audit_log` in MongoDB:
   - Confirm summary contains **only** `fieldNames: ["name", "role", "company_affiliation", "company_id", "relationship", "notes"]` and contact ID. Zero personal names or roles in audit payload.
3. Verify MCP contact exclusion:
   - Query `tracker_query` with `entity: "contacts"`.
   - Returns validation error ("Contacts are accessible exclusively via CLI").
4. Retrieve contact via CLI:
   ```powershell
   dotnet run --project src/RoleRadar.Cli -- contact get --id "<contact_guid>"
   ```
   - Displays contact details.
5. Redact contact:
   ```powershell
   dotnet run --project src/RoleRadar.Cli -- contact redact --id "<contact_guid>" --force
   ```
6. Verify redacted state:
   - `name` is `"[REDACTED]"`, role/company/notes are `null`, `is_redacted` is `true`.
7. Run `roleradar status`:
   - Reports `Contacts: 1 stored (1 redacted)`. Zero personal information displayed.

---

### Step 2.7: Referential Integrity Verification

Run the verification CLI command:

```powershell
dotnet run --project src/RoleRadar.Cli -- verify
```

- If all linkages between notes, companies, postings, applications, signals, and contacts are valid, exits with code `0`.
- Inject a dangling posting ID onto a test note and re-run:
  - Command exits with code `3` and outputs the dangling reference report without modifying the database.

---

### Step 2.8: Backup, Wipe, and Restore Round-Trip

1. Run encrypted streaming backup:
   ```powershell
   $env:RR_PASSPHRASE = "TestPassphrase123!"
   pwsh ./scripts/backup.ps1 -OutputDirectory ./backups -PassphraseEnv RR_PASSPHRASE
   ```
2. Wipe datastore:
   ```powershell
   pwsh ./scripts/wipe-data.ps1
   ```
3. Verify datastore is empty:
   ```powershell
   dotnet run --project src/RoleRadar.Cli -- status
   ```
4. Restore from backup archive:
   ```powershell
   pwsh ./scripts/restore.ps1 -ArchivePath (Get-ChildItem ./backups/*.rrbak | Select-Object -Last 1).FullName -PassphraseEnv RR_PASSPHRASE
   ```
5. Re-run integrity check and status:
   ```powershell
   dotnet run --project src/RoleRadar.Cli -- verify
   dotnet run --project src/RoleRadar.Cli -- status
   ```
   - Confirms 100% data fidelity across all 9 structured collections.
