# Phase 1: Data Model & Schema Specifications

**Feature**: `006-odata-reads`  
**Date**: 2026-11-01 (Revised)  
**Status**: Complete  

This document details the read models exposed over the `/odata` query service, their mapping to underlying MongoDB collections, indexing schemas, single-choke-point persistence rules, and transactional rename cascades.

---

## 1. OData Read Models

All read models strictly omit the internal `owner_id` field from client payloads. Tenant scoping is enforced within the Application read seam prior to returning data.

### 1.1 PostingReadModel
- **Entity Set**: `Postings` (`/odata/Postings`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `companyId` (`Guid`)
  - `companyName` (`string`, denormalized from Company)
  - `title` (`string`)
  - `requisitionId` (`string?`)
  - `location` (`string?`)
  - `disposition` (`string`: `unscored` | `lead` | `scored` | `applied` | `rejected` | `screened_out` | `closed`)
  - `resurfacePolicy` (`string`: `never` | `if_reopened`)
  - `score` (`int?`, nullable)
  - `createdAt` (`DateTimeOffset` in UTC)
  - `updatedAt` (`DateTimeOffset` in UTC)
- **Allowlisted Query Properties**:
  - **Filterable**: `companyId`, `companyName`, `title`, `disposition`, `resurfacePolicy`, `score`, `createdAt`, `updatedAt`
  - **Sortable**: `id`, `companyName`, `title`, `disposition` (mapped to `disposition_rank`), `score` (nulls last), `createdAt`, `updatedAt`
  - **Selectable**: All attributes
- **Underlying Persistence Fields & Choke Point**:
  - Maps to `postings` collection.
  - Stored field `disposition_rank` (`int`: 0..6) is computed during serialization in `MongoPostingRepository.SerializePosting` from `Posting.Disposition`:
    - `unscored` = 0, `lead` = 1, `scored` = 2, `applied` = 3, `rejected` = 4, `screened_out` = 5, `closed` = 6.
  - Stored field `company_name` is populated during posting creation/upsert and maintained through company rename cascades.

### 1.2 CompanyReadModel
- **Entity Set**: `Companies` (`/odata/Companies`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `name` (`string`)
  - `status` (`string`: `target` | `active` | `watchlist` | `archived`)
  - `ownershipType` (`string`: `public` | `private` | `subsidiary` | `unknown`)
  - `headquarters` (`string?`)
  - `description` (`string?`)
  - `categoryTags` (`List<string>`)
- **Allowlisted Query Properties**:
  - **Filterable**: `name`, `status`, `ownershipType`, `headquarters`
  - **Sortable**: `id`, `name`, `status`, `ownershipType`
  - **Selectable**: All attributes

### 1.3 ApplicationReadModel
- **Entity Set**: `Applications` (`/odata/Applications`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `postingId` (`Guid`)
  - `companyId` (`Guid`)
  - `postingTitle` (`string`, materialized)
  - `companyName` (`string`, materialized)
  - `status` (`string`: `applied` | `rejected` | `closed`)
  - `appliedAt` (`DateTimeOffset` in UTC)
  - `hasInterview` (`bool`, materialized from lifecycle events)
  - `firstResponseAt` (`DateTimeOffset?` in UTC, nullable)
  - `lastEventAt` (`DateTimeOffset?` in UTC; optional, null only when the application has no events)
  - `ageInDays` (`int`, computed at runtime from `appliedAt`)
- **Allowlisted Query Properties**:
  - **Filterable**: `postingId`, `companyId`, `companyName`, `postingTitle`, `status`, `appliedAt`, `hasInterview`, `firstResponseAt`, `lastEventAt`
  - **Sortable**: `id`, `postingTitle`, `companyName`, `status`, `appliedAt`, `hasInterview`, `firstResponseAt` (nulls last), `lastEventAt` (nulls last), `ageInDays` (maps to `appliedAt desc/asc`)
  - **Selectable**: All attributes
- **Underlying Persistence Fields & Choke Point**:
  - Maps to `applications` collection.
  - `postingTitle` and `companyName` are materialized during `CreateApplicationHandler` and updated via rename cascades.
  - `hasInterview`, `firstResponseAt`, `lastEventAt`, and `status` are materialized during `AppendApplicationEventHandler` inside the same transaction with audit logging.
  - Milestone semantics: `firstResponseAt` is the earliest event_date (UTC midnight) among interview, offer, offer_accepted, offer_declined and rejected events (follow_up and withdrawn do not count). `hasInterview` is true when any interview event exists. `lastEventAt` is the latest event timestamp across all event types. Event timestamps come from event_date, falling back to created_at only when event_date is missing or unparseable.
  - `ageInDays` is computed in memory as `(DateTime.UtcNow.Date - appliedAt.Date).Days` and not stored or indexed independently.

### 1.4 NoteReadModel
- **Entity Set**: `Notes` (`/odata/Notes`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `heading` (`string`)
  - `tags` (`List<string>`)
  - `linkedEntityType` (`string`)
  - `linkedEntityId` (`Guid`)
  - `createdAt` (`DateTimeOffset` in UTC)
  - `updatedAt` (`DateTimeOffset` in UTC)
  - `content` (`string`, narrative content)
- **Allowlisted Query Properties**:
  - **Filterable**: `heading`, `linkedEntityType`, `linkedEntityId`, `createdAt`, `updatedAt`
  - **Sortable**: `id`, `heading`, `createdAt`, `updatedAt`
  - **Selectable**: All attributes (including `content`). `content` is **strictly forbidden** in `$filter` and `$orderby`.

### 1.5 CriterionReadModel
- **Entity Set**: `Criteria` (`/odata/Criteria`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `name` (`string`)
  - `category` (`string`, mapped from existing `weight` field)
  - `ruleText` (`string`)
  - `effectiveFrom` (`DateTimeOffset` in UTC)
  - `effectiveTo` (`DateTimeOffset?` in UTC, nullable)
  - `isActive` (`bool`)
- **Allowlisted Query Properties**:
  - **Filterable**: `name`, `category`, `effectiveFrom`, `effectiveTo`, `isActive`
  - **Sortable**: `id`, `name`, `category`, `effectiveFrom`, `effectiveTo`, `isActive`
  - **Selectable**: All attributes

### 1.6 SearchRunReadModel
- **Entity Set**: `SearchRuns` (`/odata/SearchRuns`)
- **Key**: `id` (`Edm.Guid`)
- **Attributes**:
  - `id` (`Guid`, Primary Key)
  - `runDate` (`DateTimeOffset` in UTC)
  - `companiesSearched` (`int`)
  - `postingsFound` (`int`)
  - `newPostings` (`int`)
  - `errorsCount` (`int`)
  - `summary` (`string?`)
- **Allowlisted Query Properties**:
  - **Filterable**: `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`
  - **Sortable**: `id`, `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`
  - **Selectable**: All attributes

---

## 2. Transactional Rename Cascades

To maintain consistency across denormalized fields without multi-collection read joins:

1. **Company Rename**:
   - Trigger: `UpdateCompanyHandler` / `company_record` updating `name`.
   - Action: Inside the same MongoDB client session / transaction, executes:
     - Update company document in `companies`.
     - Update `company_name = newName` on all documents in `postings` where `company_id == company.Id` and `owner_id == ownerId`.
     - Update `company_name = newName` on all documents in `applications` where `company_id == company.Id` and `owner_id == ownerId`.
     - Records append-only audit log entry.
2. **Posting Title Rename**:
   - Trigger: Updating job posting `title`.
   - Action: Inside the same transaction, updates:
     - Posting document in `postings`.
     - Update `posting_title = newTitle` on all documents in `applications` where `posting_id == posting.Id` and `owner_id == ownerId`.
     - Records append-only audit log entry.

---

## 3. Database Indexes (MongoDB)

All compound indexes are initialized in `TrackingIndexInitializer` with `owner_id` as the leading field, enforcing tenant isolation directly in the index B-tree.

```text
Collection: postings
- { owner_id: 1, disposition_rank: 1, _id: 1 }
- { owner_id: 1, score: -1, _id: 1 }
- { owner_id: 1, created_at: -1, _id: 1 }
- { owner_id: 1, updated_at: -1, _id: 1 }
- { owner_id: 1, title: 1, _id: 1 }           [Collation: locale="en", strength=2]
- { owner_id: 1, company_name: 1, _id: 1 }    [Collation: locale="en", strength=2]

Collection: applications
- { owner_id: 1, applied_at: -1, _id: 1 }
- { owner_id: 1, last_event_at: -1, _id: 1 }
- { owner_id: 1, first_response_at: -1, _id: 1 }
- { owner_id: 1, has_interview: 1, _id: 1 }
- { owner_id: 1, status: 1, _id: 1 }

Collection: companies
- { owner_id: 1, normalized_name: 1, _id: 1 }  [Collation: locale="en", strength=2]
- { owner_id: 1, status: 1, _id: 1 }

Collection: notes
- { owner_id: 1, created_at: -1, _id: 1 }
- { owner_id: 1, heading: 1, _id: 1 }         [Collation: locale="en", strength=2]
- { owner_id: 1, linked_entity_type: 1, linked_entity_id: 1, _id: 1 }

Collection: criteria
- { owner_id: 1, is_active: 1, effective_from: 1, _id: 1 }

Collection: search_runs
- { owner_id: 1, run_date: -1, _id: 1 }
```
