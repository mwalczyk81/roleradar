# Research & Technical Decisions: Structured Job-Search Tracking

**Feature**: `002-job-search-tracking`  
**Date**: 2026-10-17  
**Status**: Completed (Phase 0 Output — Revised)

---

## 1. Storage Decision: Unified MongoDB Replica Set

### Decision
Store all structured job-search entities in the existing MongoDB replica set (`rs0`) and database (`roleradar`) established in Feature 001. No secondary database (e.g. PostgreSQL) is introduced for Feature 002.

The structured entities are organized into dedicated MongoDB collections:
- `companies`
- `career_sources`
- `postings`
- `posting_signals`
- `criteria`
- `applications`
- `application_events`
- `contacts`
- `search_runs`
- (existing: `notes`, `note_revisions`, `audit_log`)

Every document in every collection MUST carry `owner_id`. All `_id` and foreign reference fields are binary subtype 4 GUIDs (`GuidRepresentation.Standard`).

### Rationale
1. **Multi-Document ACID Transactions**: In Feature 002, writes frequently span multiple aggregates atomically:
   - Creating an application updates posting disposition to `applied` and inserts an `audit_log` entry.
   - Reopen-and-apply override transitions a closed posting, inserts an application, and writes an audit log entry.
   - Ingesting a posting with signals inserts the posting, signals, and audit entry.
   MongoDB replica sets support multi-document, multi-collection ACID transactions via `IClientSessionHandle.WithTransactionAsync`. A single datastore guarantees all-or-nothing transactions without distributed two-phase commit (2PC) or outbox workers.
2. **Unified Backup and Restore**: Feature 001 built an audited, authenticated, streaming AES-256-GCM / libsodium crypto pipeline around `mongodump --archive --gzip`. Keeping all entities in MongoDB means existing encrypted backup, non-empty restore guards, and wipe operations automatically protect the entire system of record with zero operational drift.
3. **Data Volume Characteristics**: The dataset is small and bounded: ~100 companies, ~150 legacy postings/appendix rows, scaling to ~5,000 postings over several years of searching. MongoDB easily supports sub-10ms queries for this volume when backed by appropriate compound and partial unique indexes.
4. **Architectural Simplicity (Constitution Principle IV)**: Introducing PostgreSQL alongside MongoDB for a dataset of a few thousand records introduces dual connection management, dual readiness probes, cross-database backup orchestration, and distributed transaction complexity without user benefit.

---

## 2. Legacy Data Compatibility & Schema Fit Check

An inspection of the real legacy tracker data was performed to guarantee that all 78 existing postings and all 149 appendix rows map directly to the Feature 002 data model **without requiring placeholder values, fake URLs, dummy requisition IDs, or synthetic identifiers**:

| Legacy Data Subset | Characteristics | Schema Fit in Feature 002 Data Model | Zero-Placeholder Validation |
| :--- | :--- | :--- | :--- |
| **78 Tracked Postings** | Full or partial ATS identifiers; mixed compensation formats (some exact, some ranges, some narrative bands). | Mapped directly to `postings`. Exact/range comp mapped to `compensation.min`/`compensation.max`; narrative bands mapped directly to `compensation.text` (up to 300 chars). URLs normalized; ATS tenant and requisition ID populate `dedup_key_a`. | **PASS**: No dummy compensation numbers or synthetic ATS IDs required. |
| **125 of 149 Appendix Rows** | Zero requisition IDs; zero URLs; company name and job title only. | Represented in `postings` with `original_url: null`, `normalized_url: null`, `ats_tenant: null`, `requisition_id: null`. `dedup_key_a`, `dedup_key_b`, and `dedup_key_c` remain `null` and are safely skipped by partial unique indexes. Tier D advisory index on `{ owner_id, company_id, normalized_title }` indexes these rows for fuzzy matching. | **PASS**: `url` and `requisition_id` are optional nullable fields; no dummy `https://none` or placeholder IDs needed. |
| **24 of 149 Appendix Rows** | Has company, title, and normalized URL, but no ATS requisition ID. | Represented in `postings` with `dedup_key_a: null`, `dedup_key_c: null`, and `dedup_key_b: normalized_url`. Enforces Tier B partial unique index. | **PASS**: Normalizes URL and matches cleanly without synthetic requisition IDs. |
| **~100 Legacy Companies** | Company name, ownership notes, and headquarters context embedded in a legacy "parent" field. | Legacy "parent" string mapped directly to bounded `description` (up to 300 characters). Structured `parent_company` populated only when an actual corporate parent exists. `ownership_type` mapped to enum (`public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`). | **PASS**: Eliminates circular-parent edge cases while preserving 100% of contextual business notes. |

---

## 3. URL Normalization Specification & Tracking Parameter Denylist

### Normalization Pipeline
Job board aggregators and ATS portals append varying tracking parameters, session tokens, and trailing slashes to identical job requisitions. The URL normalizer is a deterministic, pure domain function: `Normalize(string? rawUrl) -> string?`.

1. **Null/Empty Handling**: If input is null or whitespace, returns `null` cleanly (supporting URL-less legacy rows).
2. **Protocol & Host Canonicalization**:
   - Standardize protocol to lowercase (default `https` if missing).
   - Lowercase host name.
   - Strip default ports (`:80` for HTTP, `:443` for HTTPS).
3. **Path Normalization**:
   - Resolve dot segments (`.` and `..`).
   - Remove trailing slashes (e.g. `/jobs/123/` -> `/jobs/123`), preserving single `/` for root.
   - Decode unnecessary percent-encoded unreserved characters (`ALPHA / DIGIT / "-" / "." / "_" / "~"`).
4. **Query Parameter Filtering & Sorting**:
   - Strip URL fragments (`#...`).
   - Parse query string parameters into key-value pairs (case-insensitive key comparison against tracking denylist).
   - Strip all marketing, tracking, click-id, and session parameters.
   - Alphabetically sort remaining query parameters by key, then value.
   - Reconstruct query string with standard `?key=value&key2=value2`.

### Tracking Parameter Denylist
The following query parameter keys (matched case-insensitively, including prefix patterns) are stripped during normalization:
- `utm_*` (`utm_source`, `utm_medium`, `utm_campaign`, `utm_term`, `utm_content`, `utm_id`, `utm_reader`, `utm_referrer`)
- `ref`, `ref_*`, `source`, `gh_src`, `src`, `source_id`
- `fbclid`, `gclid`, `gclsrc`, `dclid`, `msclkid`, `twclid`
- `trk`, `trackingId`, `tracking_id`, `li_fat_id` (LinkedIn tracking)
- `mc_cid`, `mc_eid` (MailChimp)
- `_hsenc`, `_hsmi`, `hsCtaTracking` (HubSpot)
- `igshid` (Instagram)
- `yclid` (Yandex)
- `mkt_tok` (Marketo)

### Invariance & Testing
- **Idempotence**: `Normalize(Normalize(u)) == Normalize(u)` for all valid URLs.
- **Property-Based Testing**: Validated using `FsCheck.Xunit` with arbitrary URLs, parameter interleavings, and casing variations.

---

## 4. Dynamic Ghost-Likelihood Calculation (On-Read Only)

### Principle
Ghost-likelihood is a synthetic, risk-scoring heuristic that flags postings that may be evergreen reposts, phantom listings, or already abandoned roles. To avoid data staleness, scoring schema lock-in, and database migration overhead, **ghost-likelihood is strictly computed dynamically on read and is NEVER persisted in any MongoDB document, field, or backup**.

### Scoring Rules
Inputs are derived from observed `PostingSignal` records and posting temporal metadata:

| Signal / Condition | Weight | Rule Description |
|:---|:---:|:---|
| `gone` | +50% | Job posting URL returns 404/410 or job removed from career board |
| `phantom` | +40% | Known phantom listing indicator or verified third-party report |
| `deadline` passed | +30% | Explicit deadline has passed with no update or disposition change |
| `repost` | +25% | Posting reappeared as a fresh requisition after prior closure/removal |
| `parallel_posting` | +20% | Identical role posted concurrently under multiple geographic requisition IDs |
| `no_salary` | +15% | No compensation details disclosed in jurisdictions or boards where expected |
| Age > 60 days without verification | +20% | Stale listing age without recent confirmation |
| Age > 90 days without verification | +35% | Highly stale listing age |
| Verification fresh (< 7 days) | -15% | Active verification dampens age-based ghost signals |

### Calculation Formula
```text
RawScore = Sum(ActiveSignalWeights) + AgeAdjustment - FreshnessDampener
ClampedScore = Max(0, Min(100, RawScore))

Likelihood Category:
  0%  - 24%  -> Low
  25% - 49%  -> Moderate
  50% - 74%  -> High
  75% - 100% -> Critical
```

### Verification Test Requirement
An automated integration test asserts that querying the raw MongoDB collection and decrypting backup dumps yields zero BSON documents containing `ghost` or `ghost_likelihood` keys.

---

## 5. MCP Tool Surface Design & Separation of Concerns

### Constraints
- Feature 001 defines 6 tools (`note_create`, `note_get`, `note_append`, `note_replace_section`, `note_search`, `note_archive`).
- Architecture ceiling: At most 12 total tools exposed across the entire MCP server (at most 6 new tools for Feature 002).
- Zero delete/remove tools.
- Strict input length boundaries to prevent agent memory overflow.
- Narrow tools with neutral, declarative descriptions free of imperative instructions.
- Third-party confidential contacts and referential integrity verification are strictly CLI-only.

### 6 New Tools Exposed on MCP
1. `posting_record`: Ingests/updates posting with tiered deduplication (Tiers A-D), differences list detection on duplicate match, explicit update mode (`postingId` + `versionToken`), compensation, culture, disposition, resurface policy, and duplicate link.
2. `posting_transition`: Explicitly transitions posting disposition (`screened_out`, `closed`, `reopened`) or updates verification status. Target `applied` is disallowed.
3. `company_record`: Records or updates a company profile (with bounded 300-char description and ownership type) and associates career sources (upsert by id or composite key, with `is_active` retirement flag).
4. `application_record`: Records a job application (variant pointer, referral, numeric salary range), appends timeline events (8 types, accepting events on decided postings without mutating disposition), and supports atomic reopen-and-apply override for decided roles.
5. `tracker_log`: Appends immutable operational records for screening criteria revisions (strict `effectiveFrom` sequencing, closing previous version) and deactivations (`criterion_deactivate`), and search run execution batches linked to report notes (with unique `startedAt` deduplication returning `already_logged`).
6. `tracker_query`: Paged, bounded read tool (uniform page/pageSize, default 50, max 200 items) with a discriminator (`postings`, `decided_postings`, `posting_dedup_lookup`, `companies`, `criteria`, `applications`).

Total new tools: **6**. Total server tools: **12**. Satisfies the <=12 constitutional cap and preserves privacy boundaries.

---

## 6. Deduplication Invariants & Concurrency Resolution

### Computed Deduplication Keys & Partial Indexes
To guarantee performance, avoid duplicate index collisions on null values, and resolve concurrent submissions:
1. **Tier A**: `dedup_key_a = "{ats_tenant}|{requisition_id}"` when both exist; null otherwise. Unique index with partial filter `{ dedup_key_a: { $type: "string" } }`.
2. **Tier B**: `dedup_key_b = normalized_url` when present; null otherwise. Unique index with partial filter `{ dedup_key_b: { $type: "string" } }`.
3. **Tier C**: `dedup_key_c = "{company_id}|{requisition_id}"` when requisition ID exists without tenant; null otherwise. Unique index with partial filter `{ dedup_key_c: { $type: "string" } }`.
4. **Tier D (Advisory Only)**: Non-unique compound index on `{ owner_id, company_id, normalized_title }`. Queries advisory matches and returns `PossibleDuplicate` candidate list with current dispositions and reasons; **never** alters or auto-merges data.

### Concurrent Submission Resolution
When two workers or search agents submit identical postings concurrently:
1. Both attempt insertion into `postings`.
2. One succeeds. The second receives a MongoDB unique index duplicate key error (`E11000`) on `dedup_key_a`, `dedup_key_b`, or `dedup_key_c`.
3. The repository catches `MongoWriteException` with category `DuplicateKey`, re-fetches the winning document inside the transaction, and executes standard re-encounter logic:
   - If decided with `resurface_policy: never` -> returns typed `AlreadyDecided` (`can_reopen: false`).
   - If decided with `resurface_policy: if_reopened` -> returns typed `AlreadyDecided` with `can_reopen: true`.
   - If active (`lead`, `scored`, `applied`, `unscored`):
     - If re-encountered on a different UTC date: updates `last_verified_at` as a real write, generating an audit entry with a one-field summary (`last_verified_at`), and returns `AlreadyTracked`.
     - If on the same UTC day with identical state: returns `AlreadyTracked` with zero writes and zero audit entries.
4. Concurrent submissions never crash and never produce duplicate records.
