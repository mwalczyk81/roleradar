# Contract: CSP policies

## Web (`web/server.ts`, `applySecurityHeaders`)

| Phase | Policy change |
| --- | --- |
| 1 to 5 | None. `style-src-attr 'unsafe-inline'` stays. The style-attribute census in the visual spec (server-rendered HTML scanned with JavaScript disabled) replaces any Report-Only header; no production code changes. |
| 6 (**done**: `kit-style-emitters.test.ts` reports zero and the census is 0) | Removed the `style-src-attr 'unsafe-inline';` clause. Remaining policy is unchanged: `default-src 'self'; script-src 'self' 'nonce-...'; style-src 'self' 'nonce-...'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self' https://identity:5001 https://localhost:5001; frame-ancestors 'none';` |
| 6, otherwise | No change. Blocked state recorded in `kit-style-emitters.md`. |

`server.test.ts` and `security-invariants.test.ts` assert that the header has no `style-src-attr` and no `unsafe-inline`; `csp-style-attribute.spec.ts` asserts the same on the live response and checks the EmptyState is styled from the server HTML with JavaScript disabled. Done in phase 6.

## Guard

Every Playwright test run through `support/csp-guard.ts` fails on any `securitypolicyviolation` event or console message containing "Content Security Policy" or "violates the following", on every route and in both themes.
