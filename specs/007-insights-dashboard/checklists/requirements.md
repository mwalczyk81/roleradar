# Specification Quality Checklist: Insights Dashboard

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-04
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Domain retention check resolved: score, current disposition, decided date and first-seen date are retained; disposition history exists only in audit entries. "Reviewed" is defined from current disposition plus application presence.
- Amended 2026-11-04 (Matt's decision): application rate uses one posting cohort (first-seen window, reviewed postings, has-application over all) and cannot exceed 100%. Adds a derived has-application posting fact (FR-009a) with backfill and list filter. Re-validated, all items still pass, no open markers.
- Amended 2026-11-04 (Matt): funnel split into two single-cohort funnels (postings first seen in window: leads, reviewed, applied-to; applications applied in window: applied, responded, interviewed, offers) so bars nest; FR-005, FR-007, FR-008 reworded; Funnel B Offers bar counts offers with an interview, offers without one shown as a note. Research R13 items 1 to 4 confirmed. Re-validated, all items still pass, no open markers.
- Judgment calls recorded in Assumptions for review: weeks start Monday UTC; offers and ghosted list filters may need adding (FR-017).
- Technology choices from the input (OData, stats endpoint, design kit, react-markdown plugins) were deliberately kept out of the spec body and belong in `/speckit-plan`.
