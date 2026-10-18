# Quickstart & Verification Guide: Feature 002 (Job Search Tracking)

This guide walks through step-by-step verification of all Feature 002 capabilities: company profiles, tiered posting deduplication, state transitions, atomic reopen-and-apply override, criteria revisioning, contact privacy and redaction, cross-collection referential integrity verification, and backup/restore round-trip.

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

Record a company via MCP tool `company_record` (or integration test harness):

```json
{
  "name": "Stripe",
  "description": "Global financial infrastructure and payments platform; privately held.",
  "ownershipType": "private",
  "headquarters": "San Francisco, CA",
  "status": "target",
  "categoryTags": ["fintech", "infrastructure", "payments"],
  "careerSources": [
    {
      "atsType": "greenhouse",
      "careersUrl": "https://stripe.com/jobs",
      "apiEndpoint": "https://boards-api.greenhouse.io/v1/boards/stripe/jobs",
      "checkIntervalDays": 3,
      "checkInstructions": "Filter by Department: Engineering, Location: Remote - US"
    }
  ]
}
```

Verify result: Company created with generated UUID `companyId`, status `target`, and 1 career source.

---

### Step 2.2: Record Postings & Verify Deduplication Tiers (A, B, C, D)

#### 1. Ingest Initial Posting (Tier A Baseline)
Record a posting with ATS tenant and requisition ID:

```json
{
  "companyId": "<stripe_uuid>",
  "title": "Staff Backend Engineer, Global Payments",
  "url": "https://stripe.com/jobs/listings/7182934?gh_jid=7182934&utm_source=linkedin",
  "sourceKind": "ats",
  "atsTenant": "stripe",
  "requisitionId": "7182934",
  "remoteType": "remote",
  "resurfacePolicy": "if_reopened"
}
```

Verify result: Posting created in `lead` disposition. `url` is normalized to `https://stripe.com/jobs/listings/7182934` (tracking params stripped).

#### 2. Re-submit Same Role via Aggregator (Tier A Dedup Check)
Submit same role from an aggregator with identical `atsTenant` and `requisitionId` but different URL:

```json
{
  "companyId": "<stripe_uuid>",
  "title": "Staff Engineer",
  "url": "https://www.linkedin.com/jobs/view/999888777",
  "sourceKind": "aggregator",
  "atsTenant": "stripe",
  "requisitionId": "7182934",
  "remoteType": "remote"
}
```

Verify result: Returns `status: "already_tracked"`, referencing the existing posting UUID. Last verified date updated, no duplicate document inserted.

#### 3. Test Tier D Advisory Match
Submit a role with no requisition ID and a matching normalized title for the same company:

```json
{
  "companyId": "<stripe_uuid>",
  "title": "Staff Backend Engineer, Global Payments",
  "url": "https://unknown-aggregator.com/postings/abc",
  "sourceKind": "aggregator",
  "remoteType": "remote"
}
```

Verify result: Returns `status: "possible_duplicate"`, listing candidate UUIDs and dispositions. Record is **not** auto-created or auto-merged.

---

### Step 2.3: Disposition Transitions & Decided Handling

#### 1. Screen Out Posting
Transition posting to `screened_out` referencing a criterion:

```json
{
  "postingId": "<posting_uuid>",
  "targetDisposition": "screened_out",
  "reason": "Required relocation to Dublin, Ireland; conflicting with remote criteria.",
  "versionToken": "<current_token>"
}
```

Verify result: Posting disposition updated to `screened_out`. `audit_log` records state transition.

#### 2. Re-encounter Decided Posting
Submit posting again with `resurfacePolicy: "if_reopened"`:

Verify result: Returns `status: "already_decided"`, `priorReason: "Required relocation...", canReopen: true`. Data store is untouched (no `last_verified` update, zero audit entries).

---

### Step 2.4: Atomic Reopen-and-Apply with Override

Attempt to apply to the screened-out posting without override:
- Submit `application_record` with `action: "create"`, `overrideDecided: false`.
- Verify result: Rejected with `ErrorCode.InvalidTransition`.

Apply with explicit override:
- Submit `application_record` with `action: "create"`, `overrideDecided: true`, `overrideReason: "Hiring manager reached out: role opened for US remote."`.
- Verify result: Succeeded in a single transaction. Posting disposition moved to `applied`, application created, and `audit_log` captures previous disposition (`screened_out`), previous policy (`if_reopened`), and override justification.

---

### Step 2.5: Point-in-Time Criteria Revisioning

1. Insert screening criterion effective `2026-01-01` to `2026-06-30` (min comp: $180k).
2. Insert updated criterion effective `2026-07-01` onwards (min comp: $200k).
3. Query `tracker_query` with `entity: "criteria"`, `effectiveDate: "2026-03-15"`:
   - Returns version 1 ($180k).
4. Query `tracker_query` with `effectiveDate: "2026-10-17"`:
   - Returns version 2 ($200k).

---

### Step 2.6: Confidential Contact Privacy & Redaction

1. Create contact via `contact_record`:
   ```json
   {
     "companyId": "<stripe_uuid>",
     "name": "Jane Doe",
     "role": "Director of Engineering",
     "email": "jane.doe@stripe.com",
     "notes": "Met at Distributed Systems conference 2025"
   }
   ```
2. Inspect `audit_log` in MongoDB:
   - Confirm summary contains **only** `fieldNames: ["name", "role", "email", "notes"]` and contact ID. Zero names, emails, or company names in audit payload.
3. Redact contact:
   - Call `contact_record` with `id: "<contact_uuid>"`, `redact: true`.
4. Inspect contact document:
   - `name` is `"[REDACTED]"`, email/phone/notes are `null`, `is_redacted` is `true`.
5. Run `roleradar status`:
   - Reports `Contacts: 1 stored (1 redacted)`. Zero personal information displayed.

---

### Step 2.7: Referential Integrity Verification

Run the verification CLI command:

```powershell
dotnet run --project src/RoleRadar.Cli -- verify
```

- If all linkages between notes, companies, postings, applications, signals, and contacts are valid, exits with code `0`.
- Inject a dangling posting ID onto a test note and re-run:
  - Command exits with code `3` and outputs the dangling reference report.

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
