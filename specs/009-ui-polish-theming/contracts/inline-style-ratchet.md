# Contract: inline-style ratchet

**Test**: `web/tests/architecture/inline-style-ratchet.test.ts`. **Data**: `web/tests/architecture/inline-style-ceiling.json`.

## Rules

1. The test scans `web/app/**/*.tsx` and `*.ts`, excluding `web/app/ds/**`, for the JSX attribute `style` (any value: object literal, identifier or expression). Counting is by syntax tree, not by text.
2. The JSON file holds one integer per file path (forward slashes) and a `total`. Initial total is **203** across the 17 files in spec US1.
3. The test fails when a file's real count is **above** its ceiling (a new inline style) or when the ceiling is **above** the real count (a phase landed without lowering it).
4. A file not in the JSON has a ceiling of 0.
5. The kit is out of this test. A separate Phase 6 test (`kit-style-emitters.test.ts`) counts `style=` in `web/app/ds/components/src/**` and is the precondition for the CSP flip (today 150 in 27 files).
6. At total 0 the ceiling file and test are removed and replaced by an ESLint `no-restricted-syntax` rule on `JSXAttribute[name.name='style']` for `web/app/**` outside `ds`.

## Ceiling schedule

| After | Total ceiling |
| --- | --- |
| Phase 1 | 203 |
| Phase 3 slice 1 (small routes) | 182 |
| Phase 3 slice 2 (list routes) | 150 |
| Phase 3 boundary A (`postings.$id`, `TransitionPostingDialog`) | 96 |
| Phase 3 boundary B (`companies.$id`) | 56 |
| Phase 3 boundary C (`applications.$id`, `CreateApplicationDialog`) | 0 |

## Dynamic exceptions

None expected. If a computed value is found, it is set through a CSS custom property or a data attribute, listed in the spec's exceptions section with the reason, and it does not use a JSX `style` attribute, so the ratchet needs no allowlist.
