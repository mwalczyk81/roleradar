# ADR 0032: Markdown Rendering of Note Bodies

- **Status**: Proposed
- **Date**: 2026-11-04
- **Feature**: `007-insights-dashboard` (Phase 4, ships independently)

## Context and Problem Statement

Note bodies are plain text today (ADR 0026: untrusted text rendered as React text nodes). The owner wants formatted notes. Note content is untrusted (Constitution V), the CSP is strict with nonces (ADR 0026), and bodies can be up to 8 MiB.

## Decision Outcome

Use `react-markdown` with `remark-gfm` and `remark-breaks`, locked down.

Pinned exactly (checked against the npm registry on 2026-11-04; all `latest`, all MIT):

| Package | Version | Licence |
| --- | --- | --- |
| react-markdown | 10.1.0 | MIT |
| remark-gfm | 4.0.1 | MIT |
| remark-breaks | 4.0.0 | MIT |

Rules:

- No `rehype-raw` and no other rehype plugin that parses HTML. A test reads `package.json` and the lockfile and fails if `rehype-raw` appears.
- Raw HTML in a note is not interpreted: it renders as inert text (react-markdown default), never as elements.
- `allowedElements` is an allowlist (paragraphs, headings, lists, blockquote, pre/code, emphasis, strong, del, hr, br, links, tables, task-list checkbox). Everything else is disallowed.
- Links: a custom `urlTransform` and `a` component accept only absolute `http:` and `https:` URLs (after trimming, case-insensitive, control characters rejected). Anything else (`javascript:`, `data:`, `vbscript:`, relative, protocol-relative) renders as plain text. Allowed links get `rel="noopener noreferrer"`.
- Images: the `img` component never renders an `<img>`; it renders the alt text as plain text. No remote fetch can occur from note content.
- Task-list checkboxes render `disabled`.
- Tables are wrapped in a keyboard-focusable, labelled, horizontally scrolling region so the page itself does not scroll on phones.
- No `dangerouslySetInnerHTML` anywhere (a scan test over `web/app`).
- Styling via CSS classes in `app.css` using `--ds-*` tokens, no inline `style` attributes, to stay inside the CSP.
- Size guard: bodies over 256 KiB render as plain preformatted text with a notice, so a huge note cannot stall SSR.
- The note detail route still loads the body from `/api/v1/notes/{id}`; nothing about the API changes.

### Consequences

- Good: formatted notes with a small, audited dependency set and an explicit allowlist.
- Bad: three new runtime dependencies in the web bundle (plus their `unified` ecosystem dependencies). A licence audit over the resolved tree is a Phase 4 task.
- Risk: ESM-only packages under the React Router SSR build. Verified by the first Phase 4 task (`pnpm --filter web run build`).

### Confirmation

Vitest component tests with hostile fixtures (script tags, `onerror` images, `javascript:`, `data:`, `vbscript:`, mixed-case and whitespace-obfuscated schemes, protocol-relative links, image syntax, raw HTML). A Chromium e2e test renders a hostile fictional note and records zero requests to any other origin and zero CSP violations. Axe scan with the reduced-motion helper.
