# Phase 0 Research: Technical Decisions and Registry Verification

**Feature**: `005b-management-ui`  
**Date**: 2026-10-31 (Amended)  
**Status**: Completed  

All dependencies, container base images, and runtime versions have been re-verified directly against the npm registry and Docker Hub at amend time. No remembered versions or floating tags are used.

---

## 1. Package & Image Registry Directory

### Production Runtime Dependencies

| Package / Image | Exact Version / Digest | Source / Registry | Purpose |
|-----------------|------------------------|-------------------|---------|
| `node` base image | `node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914` | Docker Hub (OCI Index) | Hardened, minimal Linux runtime (unprivileged `node:node` UID 1000) |
| `react` | `19.0.0` | npm registry | Core UI library |
| `react-dom` | `19.0.0` | npm registry | DOM renderer |
| `react-router` | `7.18.5` | npm registry | Full-stack web framework runtime (deliberate choice per spec; 8.x breaking changes out of scope) |
| `@react-router/node` | `7.18.5` | npm registry | Node.js adapter for React Router |
| `@react-router/serve` | `7.18.5` | npm registry | Production server execution entry |
| `openid-client` | `6.8.8` | npm registry | Certified OpenID Connect client (Authorization Code + PKCE, refresh, logout) |
| `openapi-fetch` | `0.17.0` | npm registry | Type-safe REST client pairing with committed OpenAPI types |
| `lucide-react` | `1.66.0` | npm registry | Accessible UI icons for application consumption (composites do not import icons directly) |
| `cookie` | `1.0.2` | npm registry | Standards-compliant HTTP cookie parsing and serialization |

### Monorepo & Build Tooling

| Tool / Package | Exact Version | Source / Registry | Purpose & Rationale |
|----------------|---------------|-------------------|---------------------|
| `nx` | `24.0.3` | npm registry | Monorepo orchestration CLI |
| `@nx/dotnet` | `24.0.3` | npm registry | First-party .NET 10 solution plugin |
| `@nx/js` | `24.0.3` | npm registry | JavaScript / TypeScript workspace orchestration |
| `pnpm` | `10.4.0` | Local & npm | Pinned package manager (`packageManager` in `package.json`) |
| `typescript` | `5.9.3` | npm registry | Newest stable TypeScript on the 5.x branch. 6.0.3 and 7.0.2 (`latest`) exist; `@react-router/dev@7.18.5` peers `typescript ^5.1.0 || ^6.0.0`, so 7.x is unsupported. Staying on 5.9.3 for toolchain-wide tested support; 6.0.x is a later dependency update. |
| `@react-router/dev` | `7.18.5` | npm registry | React Router Vite build plugin |
| `vite` | `6.4.1` | npm registry | Frontend bundler (compatible with `@react-router/dev` and `vitest@5`) |
| `openapi-typescript` | `7.13.0` | npm registry | TypeScript contract generation from OpenAPI schema |

### Testing & Verification Dependencies

| Package | Exact Version | Source / Registry | Compatibility Rationale |
|---------|---------------|-------------------|-------------------------|
| `vitest` | `5.0.5` | npm registry | Newest compatible major. Vitest 5.0.5 requires `vite: '^6.4.0 || ^7.0.0 || ^8.0.0'`, and `@react-router/dev@7.18.5` supports `vite: '^5.1.0 || ^6.0.0 || ^7.0.0 || ^8.0.0'`. Both align cleanly on Vite 6.4.x. |
| `@testing-library/react` | `16.3.3` | npm registry | React component DOM interaction testing |
| `@testing-library/dom` | `10.4.2` | npm registry | DOM query utilities |
| `axe-core` | `4.14.0` | npm registry | WCAG AA accessibility automated analysis |
| `@playwright/test` | `1.64.1` | npm registry | End-to-end browser automation smoke tests |

---

## 2. Research Findings by Architectural Domain

### Domain 1: Authentication & OIDC Integration

- **Decision**: Adopt `openid-client@6.8.8` paired with React Router v7 (`7.18.5`) framework mode (ADR-0021).
- **Rationale**:
  - OpenID Foundation certified. Zero external dependencies; built natively on Web Standards (`oauth4webapi`, `fetch`, Web Cryptography).
  - Implements RFC 7636 (PKCE with S256), RFC 6749 (Authorization Code grant), `refresh_token` grant, and RP-Initiated Logout.
  - Keeps tokens 100% on the server; zero token leaks to client bundles.
  - Decoupled from Duende IdentityServer packages, satisfying Core Principle IV.
  - **Framework Version Selection**: The choice of React Router 7.x (`7.18.5`) is deliberate per project specification (FR-001) even though React Router 8.x exists on npm. React Router 7 is the stable, proven framework-mode foundation for server-rendered web applications with long-term ecosystem alignment, whereas 8.x introduces breaking changes that are out of scope.
- **Alternatives Evaluated**:
  - *`openid-client` v5*: Previous generation relying on Node callback APIs and third-party HTTP libraries; now in maintenance mode. Rejected.
  - *`next-auth` / `@auth/core`*: Over-opinionated framework designed for multi-provider consumer SaaS; couples session storage to database schemas. Rejected.
  - *`arctic`*: Lightweight OAuth2 library lacking automated OIDC discovery, JWKS rotation, and certified ID token validation. Rejected.

### Domain 2: Session Store & Token Security

- **Decision**: Encrypted In-Memory Map Store with AES-256-GCM + Single-Flight Refresh Lock (ADR-0022).
- **Rationale**:
  - The browser receives only an opaque random session identifier via a signed cookie (`__Host-roleradar_session` or `roleradar_session`, `HttpOnly`, `Secure`, `SameSite=Lax`).
  - Tokens in server memory are encrypted at rest using AES-256-GCM with a 32-byte key (`SESSION_ENCRYPTION_KEY`). Missing keys outside Development fail closed at startup.
  - Duende IdentityServer uses `RefreshTokenUsage = OneTimeOnly`. When 10 concurrent requests arrive with an expired access token, a single-flight Promise registry (`inFlightRefreshes.get(sessionId)`) ensures exactly 1 refresh call executes; the other 9 requests await the result without duplicate rotations.
  - Container restart session invalidation is acceptable for local single-owner operation; avoids external database dependencies.
  - Synchronizer CSRF tokens are enforced on all non-GET actions.

### Domain 3: REST API Consumption

- **Decision**: Strongly-Typed `openapi-fetch@0.17.0` paired with `contracts/client/api-types.ts` (ADR-0023).
- **Rationale**:
  - `contracts/client/api-types.ts` is generated from the committed `contracts/openapi/roleradar-api.json`.
  - Compile-time typechecking ensures zero untyped query parameters, paths, or payloads.
  - All calls originate server-side from React Router loaders and actions; direct browser calls fail immediately (API has CORS disabled).
  - API errors follow `ErrorEnvelope` (`{ ok: false, errorCode, message, details: ErrorFieldDetail[] }`). A centralized `mapApiError` adapter converts these into typed UI errors for inline form validation and 409 conflict handling.
  - Application list context resolution: The API has no batch lookup endpoint. The web server extracts unique `postingId`s and `companyId`s on the page (maximum 25 each), executes deduplicated parallel `GET`s via `Promise.all`, and merges the titles/names into the view model with request caching.

### Domain 4: Design System Integration & TypeScript Architecture

- **Decision**: Exact Upstream Layout Mirroring (`web/app/ds`) + TypeScript `allowJs` + Composite Invariants.
- **Layout Mirroring**:
  - The directory `web/app/ds` mirrors the upstream design kit artifact exactly:
    - `tokens.css`, `tokens.json`, `fonts/`
    - `components/bundle.css` (contains global focus rings, reduced motion, and structural styles; **there is no `base.css`**)
    - `components/index.d.ts` and `components/<Name>/<Name>.d.ts` (type definitions)
    - `components/src/<group>/*.jsx` (component implementations; **there are no `primitives/` or `composites/` folders**)
- **TypeScript Configuration**:
  - Configured with `allowJs: true` and `checkJs: false` in `web/tsconfig.json`. The application code under `web/app/**` outside `web/app/ds/**` is strictly typed TypeScript (`strict: true`, `noImplicitAny: true`) consuming components via their `.d.ts` declarations.
- **Linter Boundaries**:
  - ESLint and Stylelint rules prohibiting raw hex colors (`#[0-9a-fA-F]{3,6}`) and pixel dimensions (`px`) explicitly exclude `web/app/ds/**`.
- **Composite Component Contracts**:
  - `AppShell` receives navigation items as a `navItems` array and accepts a `linkComponent` prop (defaulting to `<a>`, or accepting React Router's `<NavLink>` or `<Link>`) instead of hardcoding application routes.
  - Composites do **not** import `lucide-react`. Any icons arrive as React node props or child slots.
  - Interactive composites (`Toast`, `Menu`) follow the kit's existing handler-composition rules (`...rest` placed before internal handlers or composed so caller handlers execute first).
- **Copy-Only Vendoring Invariant**:
  - As stated in `web/app/ds/SYNC.md`, the vendored copy is strictly copy-only and never edited by hand. All fixes and Part C composites are authored in the upstream kit and synchronized via re-export.

### Domain 5: In-Process TLS & Container Trust

- **Decision**: Node in-process HTTPS on port 5003 + `NODE_EXTRA_CA_CERTS` (ADR-0025).
- **Rationale**:
  - Node terminates TLS directly on `https://*:5003` using the existing `./deploy/certs/dev-cert.crt` and `dev-cert.key`.
  - Browsers enforce `Secure` and `__Host-` cookie flags on HTTPS.
  - Outbound backchannel calls to `https://identity:5001` and `https://api:5002` succeed because `NODE_EXTRA_CA_CERTS=/https/dev-cert.crt` instructs Node to append the self-signed certificate authority to its root trust store.
  - Port 5003 is registered in `IdentityServerConfig` for `roleradar-web` redirect (`/auth/callback`) and post-logout (`/auth/logout`) URIs.

### Domain 6: Content Security Policy & Theme Initialization

- **Decision**: Cryptographic Nonce + Zero-FOUC Head Script + React Leaf Nodes (ADR-0026).
- **Rationale**:
  - Every HTML response receives a random 128-bit cryptographic nonce in its CSP header; `'unsafe-inline'` is completely banned.
  - The `<head>` includes a nonced inline script that reads the `theme` cookie or `prefers-color-scheme` and sets `document.documentElement.setAttribute('data-theme', theme)` before paint, guaranteeing zero theme flicker.
  - All API data fields (`title`, `location`, `rawText`, `signals`, `assessmentNote.summary`, etc.) are rendered as React plain-text children (`{posting.title}`), escaping all HTML. `dangerouslySetInnerHTML` is banned across the codebase via ESLint.

---

## 3. Web Test Coverage Strategy

Per Success Criterion **SC-007**, the web application coverage targets are established as follows:

| Metric | Target | Scope | Rationale |
|--------|--------|-------|-----------|
| **Line Coverage** | **>= 80%** | `web/app/**` (excluding `web/app/ds/**`) | Ensures high confidence across route loaders, actions, API clients, session managers, and custom components without double-testing vendored design system files. |
| **Branch Coverage** | **>= 75%** | `web/app/**` (excluding `web/app/ds/**`) | Covers critical branching logic (session expiry, 409 conflict reload, 400 validation error mapping, 403 not authorized). |
| **Accessibility** | **100% Zero Violations** | All routes in both light and dark themes | Automated `axe-core` assertions on every rendered route view. |
