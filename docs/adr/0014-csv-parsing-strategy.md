# ADR 0014: CSV Parsing Strategy — Hand-Rolled RFC 4180 Parser vs. External Library

- **Status**: Accepted
- **Date**: 2026-10-24
- **Feature**: `004-tracker-import`

## Context and Problem Statement

Feature 004 requires reading up to four CSV files from the import bundle:

- `appendix-rows.csv` (~150 rows, 18 columns, containing quoted text, commas in fields, and newlines in notes).
- `applications.csv` (16 columns, optional dates, review flags, quotes).
- `watchlist-companies.csv` (5 columns, multiline text, quoted descriptions).
- `company-aliases.csv` (2 columns, simple pairs).

In accordance with Constitution Principle IV ("Named Seams Only", no unnecessary external frameworks) and Principle VIII ("Pinned Dependencies"), we evaluated whether to introduce an external CSV library (such as `CsvHelper` or `Sylvan.Data.Csv`) or implement an internal, hand-rolled RFC 4180 CSV parser.

## Considered Options

1. **Option A: Hand-Rolled RFC 4180 CSV Parser (`Rfc4180CsvReader`)**
   A dedicated parser in `RoleRadar.Domain.Tracking` (~120 lines of C#) implementing standard RFC 4180 rules (commas, double quotes, escaped quotes `""`, multiline quoted fields, header row mapping, line numbering).
2. **Option B: `CsvHelper` (v33.1.0)**
   The standard .NET CSV library from NuGet.
3. **Option C: `Sylvan.Data.Csv` (v1.4.4)**
   A high-performance lightweight CSV library from NuGet.

## Comparison & Decision Drivers

| Factor | Option A: Hand-Rolled Parser | Option B: `CsvHelper` (33.1.0) | Option C: `Sylvan.Data.Csv` (1.4.4) |
| :--- | :--- | :--- | :--- |
| **External Dependencies** | **Zero**: Standard BCL (`System.IO`, `System.Text`, `ReadOnlySpan<char>`). | **High**: Heavyweight dependency with reflection/expression tree builders and class mapping abstractions. | **Low**: Single focused NuGet package. |
| **Licensing & Supply Chain** | **Zero Risk**: 100% first-party code adhering to project MIT license. | Dual MS-PL / Apache 2.0 license, transitive dependencies. | MIT license, external package dependency. |
| **Schema Alignment** | **Exact**: Directly tailored to RoleRadar's requirements: strict row numbering for error isolation, untrusted text length limiting, header validation, and streaming records. | Requires extensive configuration class mappings (`ClassMap<T>`). | Requires DataReader or manual ordinal indexing. |
| **Complexity & LOC** | **~120 lines**: Small, easily verified by exhaustive unit tests including fuzzing and malformed CSV edge cases. | Thousands of lines of external library code. | Moderate external library codebase. |
| **Maintenance Burden** | Negligible; RFC 4180 is an immutable standard. | Regular package upgrades, API deprecations across major versions. | Pinned dependency maintenance. |

## Decision Outcome

Chosen option: **Option A: Hand-Rolled RFC 4180 CSV Parser (`Rfc4180CsvReader`)**.

### Rationale

1. **Adherence to Constitution Principles**:
   - Constitution Principle IV explicitly cautions against introducing external dependencies where plain abstractions suffice.
   - Similar to Feature 001 where `MarkdownSectionParser` was hand-rolled in first-party code with 100% test coverage, a dedicated RFC 4180 parser avoids external supply-chain surface while providing exact control over character parsing and error reporting.
2. **Deterministic Row Number Tracking**:
   - The specification mandates that all validation errors and skips report exact source file row numbers (e.g. `appendix-rows.csv:42`). A tailored parser tracks line/record numbers natively across multiline fields.
3. **Untrusted Data Boundaries**:
   - Per Constitution Principle V, imported text is untrusted. The hand-rolled parser enforces max field length limits and clean string allocations directly during streaming tokenization.
