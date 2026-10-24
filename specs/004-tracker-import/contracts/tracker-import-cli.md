# CLI Contract: `roleradar tracker-import`

## Synopsis

```bash
roleradar tracker-import <bundle-directory> [options]
```

## Description

Loads a historical job-search tracker bundle into RoleRadar's structured collections.
Defaults to dry-run mode (zero persistent writes, zero audit log entries). The `--apply` option is required to commit changes.

## Arguments

- `<bundle-directory>` *(required)*: Absolute or relative path to the directory containing import bundle files (`postings.json`, `appendix-rows.csv`, `applications.csv`, `watchlist-companies.csv`, and optional `company-aliases.csv`).

## Options

- `--default-date <YYYY-MM-DD>` *(conditional)*: Explicit default date used for undated records, partial dates needing a year anchor, or flagged dates. Required if any record in the bundle lacks a usable date.
- `--apply` *(optional, flag)*: Commit writes to the persistent database. Default is false (dry-run mode).
- `--report <path>` *(optional)*: Path to write a detailed UTF-8 report of row mappings, warnings, and manual contact recommendations. Refuses execution if `<path>` resolves inside a git work tree.
- `--verbose, -v` *(optional, flag)*: Display row error details on stderr.
- `--help, -h` *(optional, flag)*: Show help and usage information.

## Exit Codes

- `0`: Success (dry-run or apply completed without fatal errors).
- `1`: Validation error (missing required `--default-date`, report path inside git work tree, missing bundle files, unparsable bundle syntax).
- `2`: MongoDB connectivity or database error.

## Terminal Output Protocol (Privacy First)

- **stdout**: Strictly outcome counts and row numbers. **Zero** company names, job titles, salary numbers, or free text.
- **stderr**: Diagnostic logging and unhandled fatal errors.

### Sample Terminal Output

```text
RoleRadar Tracker Import (Dry-Run Preview)
Bundle: /path/to/bundle

Summary of Planned Changes:
  Companies:     12 created, 4 updated, 1 skipped
  Postings:      85 created, 2 updated, 3 skipped
  Applications:  18 created, 0 skipped
  Notes:          4 created
  Criteria:       7 created (2 seed, 5 inferred)

Date & Data Hygiene:
  Dates Defaulted:       6
  Dates Inferred (Rollback): 14
  Dates Flagged:         2
  Culture Unmapped:      1
  Fields Truncated:      3
  Unclaimed Applied:     2 (remain in 'scored')

Validation Warnings:
  appendix-rows.csv:42 - Unsupported transition 'closed' -> 'applied' skipped
  applications.csv:19 - Decision 'invalid_code' rejected

To apply these changes, rerun with --apply.
Detailed report written to /path/to/report.txt
```
