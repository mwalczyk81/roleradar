# Research & Technical Decisions: Structured Job-Search Tracking

**Feature**: `002-job-search-tracking`  
**Date**: 2026-10-17  
**Status**: Completed (Phase 0 Output)

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

Every document in every collection MUST carry `owner_id`.

### Rationale
1. **Multi-Document ACID Transactions**: In Feature 002, writes frequently span multiple aggregates atomically:
   - Creating an application updates posting disposition to `applied` and inserts an `audit_log` entry.
   - Reopen-and-apply override transitions a closed posting, inserts an application, and writes an audit log entry.
   - Ingesting a posting with signals inserts the posting, signals, and audit entry.
   MongoDB replica sets support multi-document, multi-collection ACID transactions via `IClientSessionHandle.WithTransactionAsync`. A single datastore guarantees all-or-nothing transactions without distributed two-phase commit (2PC) or outbox workers.
2. **Unified Backup and Restore**: Feature 001 built an audited, authenticated, streaming AES-256-GCM / libsodium crypto pipeline around `mongodump --archive --gzip`. Keeping all entities in MongoDB means existing encrypted backup, non-empty restore guards, and wipe operations automatically protect the entire system of record with zero operational drift.
3. **Data Volume Characteristics**: The dataset is small and bounded: ~100 companies, ~150 legacy postings/appendix rows, scaling to ~5,000 postings over several years of searching. MongoDB easily supports sub-10ms queries for this volume when backed by appropriate compound and partial unique indexes.
4. **Architectural Simplicity (Constitution Principle IV)**: Introducing PostgreSQL alongside MongoDB for a dataset of a few thousand records introduces dual connection management, dual readiness probes, cross-database backup orchestration, and distributed transaction complexity without user benefit.

### Alternatives Considered
- **Dual Store (PostgreSQL for structured entities + MongoDB for notes)**: Rejected. Splitting stores prevents atomic cross-entity writes (e.g., synchronizing posting state, application state, and audit log), requires dual backup tools (`pg_dump` + `mongodump`), doubles Docker compose memory/container footprint, and adds significant failure surface.
- **Relational Only (Migrate notes to PostgreSQL)**: Rejected. Feature 001 is already completed, tested, and validated with MongoDB. Rewriting Feature 001 violates scope discipline.

---

## 2. URL Normalization Specification & Tracking Parameter Denylist

### Normalization Pipeline
Job board aggregators (LinkedIn, Indeed, Google Jobs) and ATS portals (Workday, Greenhouse, Ashby, Lever) append varying tracking parameters, session tokens, and trailing slashes to identical job requisitions. The URL normalizer is a deterministic, pure domain function: `Normalize(string rawUrl) -> string`.

1. **Protocol & Host Canonicalization**:
   - Standardize protocol to lowercase (default `https` if missing).
   - Lowercase host name.
   - Strip default ports (`:80` for HTTP, `:443` for HTTPS).
2. **Path Normalization**:
   - Resolve dot segments (`.` and `..`).
   - Remove trailing slashes (e.g. `/jobs/123/` -> `/jobs/123`), preserving single `/` for root.
   - Decode unnecessary percent-encoded unreserved characters (`ALPHA / DIGIT / "-" / "." / "_" / "~"`).
3. **Query Parameter Filtering & Sorting**:
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
- **Property-Based Testing**: Validated using FsCheck with arbitrary URLs, arbitrary parameter interleavings, and casing variations.

---

## 3. Dynamic Ghost-Likelihood Calculation (On-Read Only)

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

## 4. MCP Tool Surface Design & Grouping Alternatives

### Constraints
- Feature 001 defines 6 tools (`note_create`, `note_get`, `note_append`, `note_replace_section`, `note_search`, `note_archive`).
- Architecture ceiling: At most 12 total tools exposed across the entire MCP server (at most 6 new tools for Feature 002).
- Zero delete/remove tools.
- Strict input length boundaries to prevent agent memory overflow.
- Narrow tools with neutral, declarative descriptions free of imperative instructions (prompt injection prevention).

### Alternatives Evaluated

#### Alternative 1: Entity-Per-Tool (18+ Tools)
Exposing separate `create`, `update`, and `get` tools for all 9 entities (`posting_create`, `posting_update`, `company_create`, `company_update`, `application_create`, etc.).
- **Verdict**: REJECTED. Violates the mandatory 12-tool ceiling and increases LLM context overhead.

#### Alternative 2: Single Mega-Tool (`tracker_dispatch`)
A single tool accepting an action discriminator (`action: "record_posting" | "query" | ...`) with a polymorphic payload.
- **Verdict**: REJECTED. Obscures tool parameter schemas from the Model Context Protocol, degrades agent parameter validation, increases tool hallucination, and makes safety boundaries difficult to enforce.

#### Alternative 3: Aggregate Write Tools + Unified Paged Query Tool (Chosen)
Define 5 narrow write tools mapped to core aggregate lifecycles, plus 1 unified paged query tool with typed discriminators:

1. `posting_record`: Records or updates a posting with tiered deduplication (Tiers A-D), compensation, culture, disposition, resurface policy, and duplicate link.
2. `posting_transition`: Explicitly transitions posting disposition (`screened_out`, `closed`, `reopened`) or updates verification status.
3. `company_record`: Records or updates a company profile (with bounded 300-char description and ownership type) and associates career sources.
4. `application_record`: Records a job application (variant pointer, referral), appends timeline events, and supports atomic reopen-and-apply override for decided roles.
5. `contact_record`: Manages private professional contacts (create, update, or `redact: true` to blank personal details while preserving ID).
6. `tracker_query`: Paged, bounded read tool with a discriminator (`postings`, `decided_postings`, `companies`, `criteria`, `applications`, `contact`, `integrity_check`).

Total new tools: **6**. Total server tools: **12**. Exactly satisfies all constraints.

---

## 5. Deduplication Invariants & Concurrency Resolution

### Tiered Priority
1. **Tier A**: `ats_tenant` + `requisition_id` (when both non-null).
2. **Tier B**: `normalized_url` (when ATS identifiers are missing).
3. **Tier C**: `company_id` + `requisition_id` (when tenant is missing).
4. **Tier D (Advisory Only)**: `company_id` + `normalized_title` (fuzzy match). Returns `PossibleDuplicate` candidate list with current dispositions and reasons; **never** alters or auto-merges data.

### Concurrent Submission Resolution
When two workers or search agents submit identical postings concurrently:
1. Both attempt insertion into `postings`.
2. One succeeds. The second receives a MongoDB unique index duplicate key error (`E11000`) on either `(ats_tenant, requisition_id)`, `normalized_url`, or `(company_id, requisition_id)`.
3. The repository catches `MongoWriteException` with category `DuplicateKey`, re-fetches the winning document inside the transaction, and executes standard re-encounter logic:
   - If decided with `resurface_policy: never` -> returns typed `AlreadyDecided`.
   - If decided with `resurface_policy: if_reopened` -> returns typed `AlreadyDecided` with `can_reopen: true`.
   - If active (`lead`, `scored`, `applied`, `unscored`) -> updates `last_verified` and returns `AlreadyTracked`.
4. This ensures concurrent submissions never crash and never produce duplicate records. Verified with an explicit parallel test.
