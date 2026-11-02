# Operational Guide: Reconciling Read Models

## Overview

Read models in RoleRadar maintain denormalized, queryable, and pre-indexed fields in MongoDB collections to support fast server-side OData queries (e.g. `disposition_rank`, `company_name`, `has_score` on postings; `posting_title`, `company_name`, `has_interview`, `first_response_at`, `last_event_at`, `has_first_response`, `has_last_event` on applications).

When schema versions change, migrations occur, or data is restored from legacy snapshots, read models must be reconciled to backfill or re-sync these computed fields.

## Semantics & Boundaries

- **Derived Fields Only**: Reconciliation only writes derived/denormalized fields (`disposition_rank`, `company_name`, `has_score` for postings). It does **not** modify `updated_at` timestamps on documents.
- **No Audit Entries**: Reconciliation is an operational backfill/maintenance procedure, not a domain action; it does **not** record audit trail entries.
- **Global Scope (Not Owner-Scoped)**: Reconciliation runs across all documents across all owners in the database to align read models globally.
- **Rename-Cascade Gap (Phase 3 Note)**: Until Phase 4 is implemented, renaming a company does not yet automatically cascade to update denormalized `company_name` on existing postings or applications. Running `reconcile-read-models` reconciles these denormalized company names until domain event cascading lands in Phase 4.

## Operational Rules & Safety

> [!IMPORTANT]
> **Operator Rule**: Matt runs `reconcile-read-models` on the real database **manually** following the exact run order below. Antigravity (agy), autonomous subagents, and automated CI/CD jobs **never** run this command against live production databases.

### Recommended Run Order

1. **Step 1: Deploy API and Web**
   Deploy the updated API and web application so the new OData query endpoints and models are active.

2. **Step 2: Create an Encrypted Backup**
   Before running any reconciliation on a live environment, create an encrypted backup using the CLI:

   ```bash
   roleradar backup
   ```

3. **Step 3: Execute Dry Run**
   Run the reconcile command without `--confirm` (dry-run mode). No documents are modified; the CLI scans the collection and reports the number of records examined and how many require updates:

   ```bash
   roleradar reconcile-read-models
   ```

   Inspect the summary output (`Postings Examined`, `Postings Updated`).

4. **Step 4: Execute Confirmed Reconciliation**
   Once the dry-run summary is verified, apply the updates by passing the explicit confirmation token:

   ```bash
   roleradar reconcile-read-models --confirm RECONCILE-READ-MODELS
   ```

5. **Step 5: Verify Idempotency (Dry Run Again)**
   Re-running the dry-run command immediately after confirmation must report `Postings Updated: 0`:

   ```bash
   roleradar reconcile-read-models
   ```
