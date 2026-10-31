# ADR 0026: Strict CSP with Nonce, Zero-FOUC Theme Initialization, and Untrusted Plain-Text Rendering

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Core Principle V (Agent Safety & Untrusted Posting Hygiene) mandates that all external job-posting content and narrative texts be treated as untrusted input across every prompt, parser, and UI surface.

In Feature 005b:

1. Job postings, compensation text, assessment notes, company descriptions, and timeline notes may contain hostile payloads (e.g. `<script>`, `<img onerror=...>`, CSS injection, or prompt-injection attempts).
2. The web application requires strict Content Security Policy (CSP) headers without `'unsafe-inline'` in `script-src` to prevent cross-site scripting (XSS).
3. The UI supports light and dark themes using CSS variables and `data-theme="light|dark"` on `<html>`. To avoid a Flash of Unstyled Content (FOUC), a minimal theme script must execute synchronously in the document head before the browser paints.
4. How should the nonce be generated, delivered to the theme script, and integrated into the React Router framework shell?
5. How do we enforce that untrusted API data is never rendered as rich text or HTML?

## Decision Drivers

- **Zero Inline Script Execution Without Nonce**: Eliminate `'unsafe-inline'` from script execution policies.
- **Zero FOUC (Flash of Unstyled Content)**: Set the `data-theme` attribute before CSS paints the first frame.
- **Strict Untrusted Text Isolation**: Render all API-supplied strings as plain text React leaf nodes; ban `dangerouslySetInnerHTML`.
- **Design Token Discipline**: Disallow ad-hoc hex codes and raw pixel sizes outside the vendored design system.

## Decision Outcome

Chosen design: **Cryptographic Per-Response Nonce + Head Script + React Safe Interpolation + Strict ESLint Constraints**.

### 1. Per-Response Cryptographic Nonce Generation

On every HTML document request, the server middleware generates a 128-bit random base64 nonce:

```typescript
const nonce = crypto.randomBytes(16).toString('base64');
```

The server sets the response header:

```http
Content-Security-Policy: default-src 'self'; script-src 'self' 'nonce-{NONCE}'; style-src 'self' 'nonce-{NONCE}'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self' https://identity:5001; frame-ancestors 'none';
```

Additional security headers:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`

### 2. Zero-FOUC Theme Initialization Script

The server passes `nonce` through loader context into the root document template (`root.tsx`). In the `<head>`, an inline script tagged with the matching `nonce` reads the cookie or system color scheme:

```tsx
export function Document({ children, nonce, theme }: { children: React.ReactNode; nonce: string; theme?: string }) {
  const themeScript = `
    (function() {
      try {
        var theme = ${JSON.stringify(theme)};
        if (!theme) {
          var match = document.cookie.match(/roleradar_theme=(light|dark)/);
          theme = match ? match[1] : (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        }
        document.documentElement.setAttribute('data-theme', theme);
      } catch (e) {}
    })();
  `;

  return (
    <html lang="en" data-theme={theme ?? 'light'}>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <script nonce={nonce} dangerouslySetInnerHTML={{ __html: themeScript }} />
        {/* ...styles and meta... */}
      </head>
      <body>{children}</body>
    </html>
  );
}
```

Because the script executes before the body is rendered and before CSS parses the DOM, `document.documentElement` immediately reflects the user's preferred theme without flickering.

### 3. Safe Plain-Text Rendering Rule

Untrusted strings from API DTOs (e.g., `posting.title`, `compensation.rawText`, `assessmentNote.summary`, `posting.signals`, `company.description`, `note.content`, `event.notes`) are rendered strictly as standard React text nodes:

```tsx
// SAFE: React automatically escapes HTML entities into plain text DOM nodes
<span>{posting.title}</span>
<div>{compensation.rawText}</div>
```

### 4. Automated Codebase Rules

Two strict lint and analysis rules are enforced in CI:

1. **Ban `dangerouslySetInnerHTML`**: React ESLint rule `react/no-danger` is enabled as an error across all application files (with an explicit, single-line exception permitted only for the nonced theme script in `root.tsx`).
2. **Design Token Enforcement**: An ESLint / Stylelint rule forbids hardcoded hex colors (`#[0-9a-fA-F]{3,6}`) and explicit pixel widths (`px`) outside `web/app/ds`. All UI components must use semantic `--ds-*` CSS tokens.

## Consequences

### Positive

- Robust defense against XSS even if malicious text payloads are stored in posting or note fields.
- Immediate theme resolution on page load without visual flash or delay.
- Nonce ensures all executed scripts are explicitly authorized by the web server.

### Negative / Trade-offs

- Static caching of HTML responses is prevented because the nonce is uniquely generated per response (appropriate for authenticated, server-rendered application routes).
