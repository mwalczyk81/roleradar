# Data Model & Schema Mappings: Feature 004 Job-Search Tracker Import

**Feature**: `004-tracker-import`  
**Date**: 2026-10-24  
**Status**: Draft

## 1. Import Bundle File Schemas

### 1.1 `postings.json`

JSON object keyed by unique slug.

| Field | Type | Required | Target Entity & Field | Mapping Rules & Constraints |
| :--- | :--- | :--- | :--- | :--- |
| `added` | string | No | `Posting.FirstSeenAt` | ISO-8601 date; fallback to `--default-date` |
| `comp` | string | No | `Posting.CompensationText` | Stored as-is; max 100 chars |
| `compHigh` | bool | No | `Posting.CompHighPriority` | Direct boolean mapping |
| `company` | string | Yes | `Company.Name` | Canonicalized via `company-aliases.csv`; auto-creates neutral company if unlisted |
| `culture` | string | No | `Posting.CultureSummary` | Safely truncated to 500 chars; truncation count reported |
| `cultureLevel` | string | No | `Posting.CultureLevel` | Mapped to `CultureLevel` enum (`stable`, `moderate`, `high_risk`, `unknown`); unmapped values fall back to `unknown` with count reported |
| `first` | string | No | `Posting.FirstSeenAt` | Used if `added` is absent |
| `ghost` | bool | No | `PostingSignal` | If `true`, creates `SignalType.Phantom` signal |
| `href` | string | No | `Posting.NormalizedUrl` | Normalized URL via `UrlNormalizer` |
| `loc` | string | No | `Posting.LocationText` | Stored as-is; max 150 chars |
| `note` | string | No | `Posting.ScreeningNotes` | Max 2000 chars |
| `order` | int | No | None | Stored for sorting/reference; not in domain entity |
| `parent` | string | No | `Company.Description` | Organization background text appended to company description |
| `posted` | string | No | `Posting.PostedAt` | Parsed ISO date |
| `score` | double | No | `Posting.Score` | Clamped 0.0 .. 10.0 |
| `status` | string | Yes | `Posting.Disposition` | Mapped to `PostingDisposition`. Postings with status `applied` are imported as `scored` and reach `applied` only via `applications.csv` import |
| `tier` | string | No | None | **Dropped** per specification |
| `title` | string | Yes | `Posting.Title` | Stored as-is; normalized title generated via `TitleNormalizer` |

### 1.2 `appendix-rows.csv`

Comma-separated values with header row (~150 rows).

| Column | Type | Target Entity & Field | Rules & Behavior |
| :--- | :--- | :--- | :--- |
| `source` | string | `Posting.SourceKind` | `aggregator`, `referral`, `direct`, `portal` |
| `proposed_disposition` | string | `Posting.Disposition` | `screened_out`, `applied`, `dismissed` (maps to `screened_out`, policy `never`), `duplicate` (links `duplicate_of_id` if target exists, else `screened_out` reason "duplicate") |
| `company` | string | `Company.Name` | Canonical name resolution |
| `title` | string | `Posting.Title` | Used with company for Tier D title matching when req/URL missing |
| `req` | string | `Posting.RequisitionId` | ATS requisition identifier |
| `comp_detected` | decimal? | `Posting.DetectedSalary` | Numeric compensation figure |
| `comp_is_estimate` | bool | `Posting.CompIsEstimate` | Estimation indicator flag |
| `remote_mentioned` | bool | `Posting.RemoteType` | Maps to `Remote` if true |
| `details_raw` | string | `Posting.RawDetails` | Truncated to 1000 chars |
| `added_md` | string | `Posting.FirstSeenAt` | Parsed with rollback rule; fallback to `--default-date` |
| `reason_primary` | string | `Criterion.Key` | Auto-registered as `Criterion` (provenance `inferred`) on first use |
| `reason_all` | string | `Posting.ScreeningNotes` | Semicolon-delimited reasons appended to notes |
| `proposed_resurface` | string | `Posting.ResurfacePolicy` | `never` or `if_reopened` |
| `company_also_in_db`| bool | None | Validation advisory |
| `review_flags` | string | None | Logged in detail report |
| `reason_text` | string | `Posting.ScreeningNotes` | Replaced by `corrected_notes` if present |
| `decision` | string | None | Blank = accept; `"skip"` = skip; other = validation error naming row |
| `corrected_notes` | string | `Posting.ScreeningNotes` | Overrides `reason_text` |

### 1.3 `applications.csv`

Application history and interaction milestones.

| Column | Type | Target Entity & Field | Rules & Behavior |
| :--- | :--- | :--- | :--- |
| `kind` | string | Discriminator | `application` (creates Application) or `standalone_note` (creates Note in `job-search`) |
| `company` | string | `Company.Name` | Company link |
| `title` | string | `Posting.Title` | Role link |
| `req` | string | `Posting.RequisitionId`| Role link |
| `applied_on` | string | `Application.AppliedOn`| Stated application date (supports `~` and parentheticals) |
| `rejected` | bool | `ApplicationEvent` | If true, appends `rejected` event and marks posting `rejected` |
| `rejected_date_md` | string | `ApplicationEvent.EventDate` | Parsed with rollback rule |
| `desired_salary` | decimal?| `Application.DesiredSalary` | Numeric target salary |
| `referral` | bool | `Application.IsReferral` | Referral indicator |
| `fit` | string | `Application.FitRating` | Self-assessed fit |
| `resume_variant` | string | `Application.ResumeVariant` | `tailored`, `as_is`, `ic_framed` |
| `people_detected` | string | Report Action List | Surfaced for manual `roleradar contact add`; **NO** auto-creation |
| `review_flags` | string | `Application.ReviewNotes` | Flag `date_is_stated_not_submitted` recorded in notes |
| `raw` | string | `Application.Notes` | Contextual notes |
| `decision` | string | None | Blank = accept; `"skip"` = skip; other = validation error |
| `corrected_notes` | string | `Application.Notes` | Overrides `raw` |

### 1.4 `watchlist-companies.csv`

Employer strategic status preferences.

| Column | Type | Target Entity & Field | Rules & Behavior |
| :--- | :--- | :--- | :--- |
| `company` | string | `Company.Name` | Canonical company name |
| `proposed_status` | string | `Company.Status` | `watch` or `avoid` |
| `text` | string | `Company.Description` | Appended to company description |
| `decision` | string | None | Blank = accept; `"skip"` = skip; other = validation error |
| `corrected_notes` | string | `Company.Description` | Overrides `text` |

### 1.5 `company-aliases.csv` (Optional)

Exact company name mapping pairs (`alias,canonical`).

---

## 2. DTOs & Models in Application Layer

```csharp
namespace RoleRadar.Application.Tracking.Import;

public sealed record TrackerImportCommand(
    string BundlePath,
    DateOnly? DefaultDate,
    bool Apply,
    string? ReportPath,
    string Actor = "cli-import-tracker"
) : ICommand<TrackerImportSummaryDto>;

public sealed record TrackerImportSummaryDto(
    bool IsDryRun,
    int CompaniesCreated,
    int CompaniesUpdated,
    int CompaniesSkipped,
    int PostingsCreated,
    int PostingsUpdated,
    int PostingsSkipped,
    int ApplicationsCreated,
    int ApplicationsSkipped,
    int NotesCreated,
    int CriteriaCreated,
    int DatesDefaulted,
    int DatesInferred,
    int DatesFlagged,
    int CultureLevelsUnmapped,
    int FieldsTruncated,
    int UnclaimedAppliedPostingsRemainingScored,
    IReadOnlyList<RowValidationErrorDto> Errors,
    IReadOnlyList<string> CompaniesNeedingAliasReview,
    IReadOnlyList<string> DetectedPeopleForManualContactAdd,
    IReadOnlyList<string> UnclaimedAppliedPostings
);

public sealed record RowValidationErrorDto(
    string FileName,
    int RowNumber,
    string ErrorMessage
);
```
