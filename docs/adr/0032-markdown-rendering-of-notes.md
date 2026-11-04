# ADR 0032: Markdown Rendering of Note Bodies

- **Status**: Proposed
- **Date**: 2026-11-04
- **Feature**: `007-insights-dashboard` (Phase 5 of `tasks.md`, plan Phase 4; ships independently of the Insights work)

## Context and Problem Statement

Note bodies are plain text today (ADR 0026: untrusted text rendered as React text nodes). The owner wants formatted notes. Note content is untrusted (Constitution V), the CSP is strict with nonces and forbids inline style attributes (ADR 0026), and bodies can be up to 8 MiB.

## Decision Outcome

Use `react-markdown` with `remark-gfm` and `remark-breaks`, locked down.

Pinned exactly (checked against the npm registry on 2026-11-04; all `latest`, all MIT). They are in `dependencies` of `web/package.json` with no `^` or `~`:

| Package | Version | Licence |
| --- | --- | --- |
| react-markdown | 10.1.0 | MIT |
| remark-gfm | 4.0.1 | MIT |
| remark-breaks | 4.0.0 | MIT |

Rules:

- No `rehype-raw`, no `rehype-sanitize` and no other rehype plugin. `web/tests/security/markdown-invariants.test.ts` fails if any package named `rehype-*` appears in `package.json` or the lockfile (`remark-rehype`, which react-markdown itself depends on, is not a rehype plugin and is allowed).
- Raw HTML in a note is never parsed. It reaches the DOM as visible, escaped text. `skipHtml` stays off on purpose, so the author's content is shown and not silently dropped.
- `allowedElements` is the whole vocabulary: `p`, `h1` to `h6`, `ul`, `ol`, `li`, `blockquote`, `pre`, `code`, `em`, `strong`, `del`, `hr`, `br`, `a`, `table`, `thead`, `tbody`, `tr`, `th`, `td`, `input` (task-list checkbox) and `img` (listed only so its component can show the alt text; it never renders an `<img>`). `unwrapDisallowed` is set, so the text of any element outside the list is kept as plain text instead of being dropped with the element.
- Every allowed element is rendered by an explicit component that passes on only the props it needs. Nothing from the parsed document is spread onto the DOM, so a note can never produce an `id`, `name`, `style`, `class` or `on*` attribute (DOM clobbering and style attributes are impossible by construction, and a test scans every attribute in the output).
- Links go through one function, `safeMarkdownUrl` (`web/app/lib/markdown-url.ts`), used as react-markdown's `urlTransform` and checked again in the `a` component. Only an absolute `http:` or `https:` URL becomes an anchor. After trimming, case-insensitively, it rejects: any other scheme; control characters, whitespace, zero-width and bidi characters or a backslash anywhere in the URL (so `java<TAB>script:` and entity-obfuscated schemes fail); protocol-relative `//host`; relative paths, fragments and queries; a scheme without exactly two slashes before the host (`https:host`, `https:/host`, `https:///host`); and credentials in the URL (`https://user:pw@host`, a phishing shape). The anchor's `href` is the normalised `URL.href`. Anchors get `rel="noopener noreferrer"` and no `target`, so they open in the same tab. A rejected link renders as its text.
- Images: the `img` component never renders an element, only the alt text. This matters beyond the obvious: React 19's server render adds a `<link rel="preload" as="image">` for any `<img>`, which would fetch the URL. A test checks the server-rendered markup for it.
- Task-list checkboxes render `disabled` and `readOnly`, with an `aria-label` ("Completed task" or "Open task"), no `name`, and are not focusable.
- Tables are wrapped in a `role="region"` element with `tabindex="0"` and a unique `aria-label` ("Table 1", "Table 2", ...), set to scroll horizontally, so the page itself never scrolls sideways. Column alignment is kept as a class (`markdown-align-right`, `markdown-align-center`), never as a style attribute.
- Markdown headings are shifted down two levels so the page keeps one `h1` (the route's) and the note card keeps its `h2`: `#` becomes `h3`, `##` `h4`, `###` `h5`, `####` to `######` all `h6`. A body that starts at `##` therefore skips `h3` (an axe "heading-order" notice, moderate, not a failure).
- Headings carry no `id` and there are no anchors.
- Code, including fenced code, is not highlighted (no highlighter, no language class) and wraps instead of scrolling, so it needs no focusable region.
- No `dangerouslySetInnerHTML` is used by this feature. A scan test over `web/app` (excluding the vendored `ds`) allows exactly one occurrence in the repo: the existing nonce'd, static theme script in `root.tsx`. The task text said "no `dangerouslySetInnerHTML` under `web/app`", which the existing root could not satisfy, so the test pins that one file and its constant template.
- Styling is in `app.css` (`.note-content`, `.markdown-*`) using `--ds-*` tokens only, for light and dark, with no inline `style` attribute written by the markdown path.
- The note detail route still loads the body from `/api/v1/notes/{id}`; nothing about the API changes. The note heading, tags, dates and linked-entity link stay React text nodes.

### Decisions on autolinks and footnotes

- **`www.example.com`**: remark-gfm turns a bare `www.` address into a link with the URL `http://www.example.com` (a scheme the author never wrote, and not TLS). It renders as plain text. A small local remark transform (`web/app/lib/markdown-plugins.ts`) unwraps a link only when its own source text starts with `www.`, so a link the author wrote on purpose (`[www.example.com](https://www.example.com/)`) is kept.
- **Email addresses**: remark-gfm links them as `mailto:`. Only http and https may be anchors, so they render as plain text.
- **Footnotes**: remark-gfm emits a `section`, `sup`, `id="user-content-fn-1"` anchors, `data-footnote-*` attributes and a back arrow. They degrade to plain text: the reference shows as `[1]`, the definitions as an ordered list under a "Footnotes" heading (the heading react-markdown emits, shifted like any other), and the back arrow is dropped. No id, no fragment link.

## Behaviour verified against the installed versions (2026-11-04)

Run against react-markdown 10.1.0, remark-gfm 4.0.1 and remark-breaks 4.0.0 with a throwaway script (deleted), then pinned by tests:

| Question | Observed | Result |
| --- | --- | --- |
| Raw HTML with no rehype plugin | Block and inline HTML (`<script>`, `<img onerror>`, `<div onclick>`, comments) is escaped into text nodes | The earlier wording of this ADR was right. `skipHtml` would drop it, which is not wanted |
| `allowedElements` without `unwrapDisallowed` | The element and everything inside it is removed: a footnote reference and the whole footnote section disappeared | `unwrapDisallowed` is set; a test shows the footnote text surviving |
| `[x](javascript:...)` through the default transform | Renders `<a href="">x</a>` (still an anchor) | The `a` component renders the text when the URL is rejected |
| `//evil.example` and `/relative` through the default transform | Pass unchanged | `safeMarkdownUrl` rejects them |
| A tab or newline typed inside `(java<TAB>script:...)` | Not parsed as a link; stays literal text | No link either way; tests cover this and the entity form (`java&#x09;script:`) |
| `www.example.com` and an email | Linked as `http://www.example.com` and `mailto:` | See the decisions above |
| `![alt](https://...)` | Renders `<img>` and, under React 19 server render, `<link rel="preload" as="image">` | `img` never renders an element |
| Footnotes | `section`, `sup`, `id`, `data-footnote-*`, `href="#user-content-fn-1"`, `aria-describedby`, an `h2` with an id | Degrade to plain text, no ids |

## Build verification (T084)

Done before any component was written, then repeated with the component in place:

- `pnpm --filter web add --save-exact react-markdown@10.1.0 remark-gfm@4.0.1 remark-breaks@4.0.0` added 98 packages to the lockfile and removed none (the lockfile diff has no deleted lines). `pnpm install --frozen-lockfile` then reported the lockfile up to date.
- `pnpm --filter web run build` succeeded both times. **No Vite or React Router SSR setting was needed** for these ESM-only packages, and vitest (jsdom) imports them with no `deps.inline` setting either.
- The server bundle keeps the three packages as external imports (they resolve from `node_modules` at run time, they are not inlined). They are in `dependencies`, and `deploy/Dockerfile.web` builds its run-time image with `pnpm --filter web deploy --legacy --prod`, so they are installed there. Checked by running that same `pnpm deploy --prod` into a scratch directory, confirming the three packages are present, and importing the built `build/server/index.js` from that layout. `scripts/review-gate.ps1 -IncludeE2E` also builds the web image and smoke-tests it with these dependencies in place, and passed.

## Limits: a size cap alone does not bound the cost

The planned guard was 256 KiB of plain text fallback. Measuring showed that is not enough: parsing happens in micromark, on the single Node event loop, before any code of ours runs, and its cost is not linear. Measured on this build, within 256 KiB:

| Body | Before the complexity guard |
| --- | --- |
| 500 nested list levels (253 KB) | 2.3 s |
| `[` repeated 20,000 times (80 KB) | 1.6 s, quadratic |
| `*a` repeated 1,500 times with closers (6 KB) | 0.5 s, quadratic; 12 KB took 2 s |
| 35,000 `* item` lines (245 KB) | 5.3 s |
| 60,000 two-character lines in one paragraph with remark-breaks | `RangeError: Maximum call stack size exceeded` |
| 3,000 nested block quotes (6 KB) | `RangeError: Maximum call stack size exceeded` |

`web/app/lib/markdown-limits.ts` therefore adds a second, linear-time check that falls back to the same plain view (with a different notice). A body is shown as plain text when it has: more than 256 KiB of UTF-8; more than 10,000 lines (LF, CRLF or lone CR); a line starting with more than 128 spaces, tabs and `>` (deeply nested quotes or lists); or more than 2,000 of `*`, `_`, `~` and `[` in one blank-line-separated block. Real notes are far below all four. The body is always still shown in full, only unformatted.

Worst bodies that are still formatted, measured the same way (server render of `MarkdownBody`, including a warm-up):

| Body | Time |
| --- | --- |
| Typical note, 9 KB with headings, lists, a table, links | 139 ms |
| 9,990 `- item` lines (70 KB) | 829 ms |
| 9,990 table rows (100 KB) | 837 ms |
| 9,990 headings (40 KB) | 293 ms |
| 4,000 short paragraphs with emphasis and a link (132 KB) | 495 ms |
| 999 nested emphasis (4 KB) | 226 ms |
| 1,999 link openers, footnote references, or brackets | 72 ms, 145 ms, 9 ms |
| 63 nested list levels, 64 nested quotes | 31 ms, 5 ms |

The worst case that still formats is under a second and blocks the event loop for that long. That is a trade-off for the owner to accept or tighten: lowering the line limit or the byte limit reduces it roughly in proportion. Moving the parse to a worker thread with a time budget would bound it properly, and is not done here.

## Licence audit (T082)

`scripts/licence-audit.mjs` (next to the repo's other check scripts) walks the resolved tree on disk, from the three packages through every transitive `dependencies` and `optionalDependencies` entry, and exits 1 when any package has a licence outside MIT, ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0 or 0BSD. It runs as part of `pnpm --filter web run test`: `markdown-invariants.test.ts` runs it against the real tree and against fictional fixture trees (a GPL leaf, a missing licence, SPDX expressions, a cycle, an unresolved dependency).

How it treats awkward licence fields:

- A single SPDX id is compared case-insensitively against the allowed list.
- An SPDX expression is evaluated: `OR` passes if any alternative is allowed, `AND` needs every part allowed, parentheses are honoured, so `(MIT OR GPL-3.0)` passes and `(MIT AND GPL-3.0)` fails.
- `X WITH exception` fails, because a licence with an exception is a different licence from the allowed one.
- The old `{ "type": ... }` object form is read as a single id, and the old `licenses` array as `OR`.
- A missing or empty field, an unparseable expression, `SEE LICENSE IN ...` and `UNLICENSED` all fail. Nothing is assumed permissive.
- A required dependency that cannot be found on disk fails as "unresolved". An optional dependency that is not installed is skipped. Peer dependencies are not followed (react is supplied by the app).

Result on 2026-11-04: 102 packages audited, 0 failing; every licence is MIT except `@ungap/structured-clone` (ISC). The full output is in the appendix below.

## Consequences

- Good: formatted notes with a small, audited dependency set and an explicit allowlist. Nothing a note contains can execute, load a resource, or leave the app's origin.
- Bad: three new runtime dependencies in the web image, with the unified ecosystem under them (98 packages newly installed; the audit counts 102 because it also includes any that other dependencies already brought in). All MIT or ISC.
- Bad: bodies over the limits above lose formatting (they are shown in full as text), and a long `* item` list of more than 2,000 items counts as one block of delimiters, so it falls back too (the same list with `-` does not).
- Bad: a formatted note can still occupy the event loop for up to roughly a second (see Limits).
- Risk, closed: ESM-only packages under the React Router SSR build. Verified (see Build verification); no setting needed.

### Confirmation

- `web/tests/components/markdown-body.hostile.test.tsx`: hostile inputs (script, `onerror` images, raw HTML blocks, `javascript:`, `data:`, `vbscript:`, mixed-case, whitespace- and entity-obfuscated schemes, protocol-relative, relative, credentials, image syntax, footnotes) and the `safeMarkdownUrl` table. Asserts no `script`, `img`, `iframe`, `style` or `link` element, no `on*`, `id`, `name` or `style` attribute, and that only absolute http and https links become anchors.
- `web/tests/components/markdown-body.format.test.tsx`: headings, lists, tables in a focusable labelled region, task lists, strikethrough, autolink literal, line breaks, the size boundary (exactly 256 KiB formats; counted in bytes) and every complexity limit.
- `web/tests/security/markdown-invariants.test.ts`: exact pins, lockfile versions, no rehype plugin, no highlighter or HTML parser, the single `dangerouslySetInnerHTML`, and the licence audit.
- `web/tests/e2e/notes-markdown.spec.ts` with `web/tests/fixtures/notes-markdown-fictional.json`: a hostile fictional note in real Chromium against the loopback stub (the isolation guard still applies). Zero requests to any origin but the app's, zero console messages or CSP violations, nothing executed, axe clean in light and dark with the reduced-motion helper, no horizontal page scroll at 360 px, and the wide table scrolls inside its focusable region with the arrow keys.

## Appendix: licence audit output (`node scripts/licence-audit.mjs`, 2026-11-04)

```text
Licence audit from react-markdown, remark-gfm, remark-breaks (allowed: MIT, ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0, 0BSD)
@types/debug@4.1.13                                      MIT       PASS
@types/estree-jsx@1.0.5                                  MIT       PASS
@types/estree@1.0.10                                     MIT       PASS
@types/hast@3.0.5                                        MIT       PASS
@types/mdast@4.0.4                                       MIT       PASS
@types/ms@2.1.0                                          MIT       PASS
@types/unist@2.0.11                                      MIT       PASS
@types/unist@3.0.3                                       MIT       PASS
@ungap/structured-clone@1.4.0                            ISC       PASS
bail@2.0.2                                               MIT       PASS
ccount@2.0.1                                             MIT       PASS
character-entities-html4@2.1.0                           MIT       PASS
character-entities-legacy@3.0.0                          MIT       PASS
character-entities@2.0.2                                 MIT       PASS
character-reference-invalid@2.0.1                        MIT       PASS
comma-separated-tokens@2.0.3                             MIT       PASS
debug@4.4.3                                              MIT       PASS
decode-named-character-reference@1.3.0                   MIT       PASS
dequal@2.0.3                                             MIT       PASS
devlop@1.1.0                                             MIT       PASS
escape-string-regexp@5.0.0                               MIT       PASS
estree-util-is-identifier-name@3.0.0                     MIT       PASS
extend@3.0.2                                             MIT       PASS
hast-util-to-jsx-runtime@2.3.7                           MIT       PASS
hast-util-whitespace@3.0.0                               MIT       PASS
html-url-attributes@3.0.1                                MIT       PASS
inline-style-parser@0.2.9                                MIT       PASS
is-alphabetical@2.0.1                                    MIT       PASS
is-alphanumerical@2.0.1                                  MIT       PASS
is-decimal@2.0.1                                         MIT       PASS
is-hexadecimal@2.0.1                                     MIT       PASS
is-plain-obj@4.1.0                                       MIT       PASS
longest-streak@3.1.0                                     MIT       PASS
markdown-table@3.1.0                                     MIT       PASS
mdast-util-find-and-replace@3.0.4                        MIT       PASS
mdast-util-from-markdown@2.1.0                           MIT       PASS
mdast-util-gfm-autolink-literal@2.0.1                    MIT       PASS
mdast-util-gfm-footnote@2.1.0                            MIT       PASS
mdast-util-gfm-strikethrough@2.0.1                       MIT       PASS
mdast-util-gfm-table@2.0.1                               MIT       PASS
mdast-util-gfm-task-list-item@2.0.0                      MIT       PASS
mdast-util-gfm@3.1.0                                     MIT       PASS
mdast-util-mdx-expression@2.0.1                          MIT       PASS
mdast-util-mdx-jsx@3.2.1                                 MIT       PASS
mdast-util-mdxjs-esm@2.0.1                               MIT       PASS
mdast-util-newline-to-break@2.0.0                        MIT       PASS
mdast-util-phrasing@4.1.0                                MIT       PASS
mdast-util-to-hast@13.2.1                                MIT       PASS
mdast-util-to-markdown@2.2.2                             MIT       PASS
mdast-util-to-string@4.0.0                               MIT       PASS
micromark-core-commonmark@2.0.4                          MIT       PASS
micromark-extension-gfm-autolink-literal@2.1.0           MIT       PASS
micromark-extension-gfm-footnote@2.1.0                   MIT       PASS
micromark-extension-gfm-strikethrough@2.1.0              MIT       PASS
micromark-extension-gfm-table@2.1.2                      MIT       PASS
micromark-extension-gfm-tagfilter@2.0.0                  MIT       PASS
micromark-extension-gfm-task-list-item@2.1.0             MIT       PASS
micromark-extension-gfm@3.0.0                            MIT       PASS
micromark-factory-destination@2.0.1                      MIT       PASS
micromark-factory-label@2.0.1                            MIT       PASS
micromark-factory-space@2.1.0                            MIT       PASS
micromark-factory-title@2.0.1                            MIT       PASS
micromark-factory-whitespace@2.0.1                       MIT       PASS
micromark-util-character@2.1.1                           MIT       PASS
micromark-util-chunked@2.0.1                             MIT       PASS
micromark-util-classify-character@2.0.1                  MIT       PASS
micromark-util-combine-extensions@2.0.1                  MIT       PASS
micromark-util-decode-numeric-character-reference@2.0.2  MIT       PASS
micromark-util-decode-string@2.0.1                       MIT       PASS
micromark-util-edit-map@1.0.0                            MIT       PASS
micromark-util-encode@2.0.1                              MIT       PASS
micromark-util-html-tag-name@2.0.1                       MIT       PASS
micromark-util-normalize-identifier@2.0.1                MIT       PASS
micromark-util-resolve-all@2.0.1                         MIT       PASS
micromark-util-sanitize-uri@2.0.1                        MIT       PASS
micromark-util-subtokenize@2.1.0                         MIT       PASS
micromark-util-symbol@2.0.1                              MIT       PASS
micromark-util-types@2.0.3                               MIT       PASS
micromark@4.0.3                                          MIT       PASS
ms@2.1.3                                                 MIT       PASS
parse-entities@4.0.2                                     MIT       PASS
property-information@7.2.0                               MIT       PASS
react-markdown@10.1.0                                    MIT       PASS
remark-breaks@4.0.0                                      MIT       PASS
remark-gfm@4.0.1                                         MIT       PASS
remark-parse@11.0.0                                      MIT       PASS
remark-rehype@11.1.2                                     MIT       PASS
remark-stringify@11.0.0                                  MIT       PASS
space-separated-tokens@2.0.2                             MIT       PASS
stringify-entities@4.0.4                                 MIT       PASS
style-to-object@2.0.2                                    MIT       PASS
trim-lines@3.0.1                                         MIT       PASS
trough@2.2.0                                             MIT       PASS
unified@11.0.5                                           MIT       PASS
unist-util-is@6.0.1                                      MIT       PASS
unist-util-position@5.0.0                                MIT       PASS
unist-util-stringify-position@4.0.0                      MIT       PASS
unist-util-visit-parents@6.1.0                           MIT       PASS
unist-util-visit@5.2.0                                   MIT       PASS
vfile-message@4.0.3                                      MIT       PASS
vfile@6.0.3                                              MIT       PASS
zwitch@2.0.4                                             MIT       PASS
102 packages audited, 0 failing
PASS
```
