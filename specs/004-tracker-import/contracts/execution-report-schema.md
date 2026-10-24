# Contract: Execution Report Schema (`--report <path>`)

## Overview

The `--report <path>` option produces an external UTF-8 plain-text report.
To uphold Constitution Principle I (Privacy First), the report **must not** be written to any path residing within a git working tree.

## Sections in the Report

1. **Header & Execution Mode**:
   - Timestamp (UTC)
   - Mode (`DRY_RUN` or `APPLIED`)
   - Source Bundle Path
   - Default Date Anchor (if supplied)

2. **Aggregated Counts Summary**:
   - Companies (created, updated, skipped)
   - Postings (created, updated, skipped)
   - Applications (created, skipped)
   - Notes created (job-search area)
   - Criteria registered
   - Dates (defaulted, inferred with rollback, flagged)
   - Culture levels unmapped
   - Text fields truncated
   - Unclaimed applied postings remaining in `scored`

3. **Companies Requiring Alias Review**:
   - List of company names auto-created with status `neutral` because they did not exist and were not in `watchlist-companies.csv` or `company-aliases.csv`.
   - Purpose: Allows the user to inspect near-miss names (e.g. "Initech" vs "Initech Systems") and add them to `company-aliases.csv` before executing `--apply`.

4. **Unclaimed "Applied" Postings**:
   - List of slugs and titles from `postings.json` marked with status `applied` that had no matching submission in `applications.csv` and consequently remain in disposition `scored`.

5. **Contacts Detected for Manual Entry**:
   - List of row numbers and names from `applications.csv` (`people_detected`, `contains_people`).
   - Instructions to invoke `roleradar contact add` manually.
   - **Zero** contacts are auto-created.

6. **Row Validation Errors & Warnings**:
   - Detailed list of row numbers, filenames, error codes, and field messages for all invalid or skipped rows.
