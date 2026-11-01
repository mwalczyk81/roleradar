# Implementation Plan: 005b-management-ui

**Branch**: `005b-management-ui` | **Date**: 2026-10-31 (Amended) | **Spec**: [specs/005b-management-ui/spec.md](file:///C:/Users/mwalc/source/repos/roleradar/specs/005b-management-ui/spec.md)

**Input**: Feature specification for 005b-management-ui (single-owner server-rendered web app managing job search through existing 005a REST API, confidential OIDC client of 005a Identity host holding tokens server-side, no new domain behavior, MCP tools, or API endpoints).

---

## Summary

Feature 005b introduces the single-owner management web application for RoleRadar. Built with **React Router v7** (`7.18.5`) in framework mode running on **Node.js 22 LTS**, the application operates as a confidential OpenID Connect client of the 005a Duende Identity host using **`openid-client@6.8.8`** (Authorization Code grant with PKCE `S256`, silent refresh, and RP-initiated logout). The choice of React Router 7.x is deliberate per project specification (FR-001) even though 8.x exists on npm, as 7.x provides a stable, long-term framework-mode foundation without the breaking architectural churn of 8.x.

All access, refresh, and ID tokens are maintained exclusively on the server in an encrypted in-memory session store (**AES-256-GCM** using `SESSION_ENCRYPTION_KEY`). The browser receives only an opaque, cryptographically signed cookie (`__Host-roleradar_session`, `HttpOnly`, `Secure`, `SameSite=Lax`). Single-flight concurrency gating ensures overlapping user requests never trigger duplicate token rotation against Identity's one-time refresh token policy.

All backend communication is performed server-side by route loaders and actions using **`openapi-fetch@0.17.0`** paired with the committed contract **`contracts/client/api-types.ts`**. Untrusted job-posting and assessment text is rendered strictly as plain text nodes to prevent XSS. The UI is built using Matt's Design System tokens and components vendored under `web/app/ds`. The vendored copy strictly mirrors the upstream kit layout (`tokens.css`, `tokens.json`, `components/bundle.css`, `components/index.d.ts`, `components/src/<group>/*.jsx`, `components/<Name>/<Name>.d.ts`, `fonts/`), is copy-only, and is never edited by hand. TypeScript is configured with `allowJs: true` so strict typechecking applies to the consuming application code rather than vendored files. The monorepo is orchestrated by **Nx 24.0.3** and **`@nx/dotnet@24.0.3`** via root `package.json` and a pinned `pnpm-lock.yaml`.

---

## Technical Context

**Language/Version**: TypeScript 5.9.3 on Node.js 22.23.4 LTS (pinned in `.node-version` and `engines`).  
*TypeScript Selection Rationale*: TypeScript 5.9.3 is the newest stable release on the 5.x branch. TypeScript 6.0.3 and 7.0.2 (the registry `latest`) are published. `@react-router/dev@7.18.5` declares `typescript: ^5.1.0 || ^6.0.0`, so 7.x is outside its supported range. We stay on 5.9.3 for the widest tested support across the Vite, Vitest, React Router and lint toolchain; moving to 6.0.x is allowed in a later dependency update once the whole toolchain is verified against it.

**Primary Dependencies**:
- Web Framework: `react-router@7.18.5`, `@react-router/node@7.18.5`, `@react-router/serve@7.18.5` (deliberate 7.x pin per spec)
- Identity & OIDC: `openid-client@6.8.8`
- REST Client: `openapi-fetch@0.17.0` paired with `contracts/client/api-types.ts`
- Icons: `lucide-react@1.66.0` (used in application views; composite design system components do not import icon packages directly)
- Bundler & Tooling: `vite@6.4.1`, `@react-router/dev@7.18.5`, `pnpm@10.4.0`
- Monorepo Orchestration: `nx@24.0.3`, `@nx/dotnet@24.0.3`

**Storage**: Server-side in-memory `Map<string, EncryptedSessionRecord>` with AES-256-GCM token encryption at rest; container restart invalidation is accepted for single-owner operation. No new domain store.  
**Testing**:
- Web Unit, Component, & Route Testing: `vitest@5.0.5`, `@testing-library/react@16.3.3`, `@testing-library/dom@10.4.2`  
  *Vitest Major Selection Rationale*: Vitest 5.0.5 is the newest compatible major. Its peerDependency requires `vite: '^6.4.0 || ^7.0.0 || ^8.0.0'`, and `@react-router/dev@7.18.5` supports `vite: '^5.1.0 || ^6.0.0 || ^7.0.0 || ^8.0.0'`, ensuring zero peer dependency conflicts.
- Accessibility Gate: `axe-core@4.14.0` (zero violations on every route in both light and dark themes)
- End-to-End Automation: `@playwright/test@1.64.1` (throwaway containers, direct MongoDB `audit_log` assertion)
- Coverage Target: >= 80% line coverage and >= 75% branch coverage on `web/app/**` (excluding `web/app/ds/**`)

**Target Platform**: Docker container running `node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914` (unprivileged `node:node` UID 1000, read-only rootfs, tmpfs `/tmp`, loopback healthcheck, port 5003 bound to `127.0.0.1` only).  
**Project Type**: Full-stack server-rendered web application (confidential OIDC client and REST proxy).  
**Performance Goals**:
- < 200ms page transitions across paged lists.
- Full end-to-end owner workflow (login, browse, filter, disposition transition, sign out) completed in under 3 minutes (SC-001).

**Constraints**:
- **Zero Browser Tokens**: No access tokens, refresh tokens, or ID tokens in cookies, local storage, or HTML responses (FR-003, FR-028).
- **Untrusted Plain-Text Rendering**: All text fields from API DTOs rendered as plain-text leaf nodes; `dangerouslySetInnerHTML` banned by ESLint (FR-012).
- **Strict Nonced CSP**: Per-response 128-bit cryptographic nonce; no `'unsafe-inline'` script execution.
- **Fail Closed on Secrets**: Server refuses to start outside Development if `SESSION_ENCRYPTION_KEY` or `ROLERADAR_WEB_CLIENT_SECRET` is missing.
- **Single-Flight Refresh**: A background Promise lock coordinates concurrent requests during token refresh to accommodate `RefreshTokenUsage=OneTimeOnly`.
- **Copy-Only Design System Vendoring**: `web/app/ds` is strictly copy-only and never edited by hand.
- **Fictional Data Only**: All test fixtures, component stories, and Playwright verification scripts use synthetic, fictional data (Principle I).

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-evaluated post-design.*

### Principle I: Privacy First (NON-NEGOTIABLE)
- **Status**: **PASS**
- **Verification**: Zero real data anywhere. Playwright tests and component fixtures use strictly fictional company names, job titles, and contact information. All secrets (`SESSION_ENCRYPTION_KEY`, `ROLERADAR_WEB_CLIENT_SECRET`) are injected via environment variables. `gitleaks` scanning continues to enforce secret hygiene.

### Principle II: Test-First Development (NON-NEGOTIABLE)
- **Status**: **PASS**
- **Verification**: Tests are authored before implementation code. Vitest tests cover route loaders and actions against stubbed API responses. Playwright tests spin up isolated throwaway containers and assert audit log creation directly in the test's MongoDB `audit_log` collection. The developer's running database is never touched.

### Principle III: Test-Enforced Layering
- **Status**: **PASS**
- **Verification**: The web application communicates exclusively with the REST API using HTTP over TLS and the generated OpenAPI client; it has zero direct references to `RoleRadar.Domain`, `RoleRadar.Infrastructure`, or internal handlers. Existing .NET architecture tests remain green.

### Principle IV: Named Seams Only
- **Status**: **PASS**
- **Verification**: Principle IV explicitly specifies: *"the web application MUST use generic OpenID Connect (OIDC)"*. The web application uses `openid-client` and does not import Duende IdentityServer packages. An automated architecture check (FR-026) enforces this boundary.

### Principle V: Agent Safety & Auditability
- **Status**: **PASS**
- **Verification**: All job-posting text, assessment notes, and external strings are treated as untrusted input. Rendered purely as escaped plain-text React leaf nodes. Zero hard deletes. All state transitions update append-only audit entries on the backend.

### Principle VI: stdio Protocol Hygiene
- **Status**: **PASS**
- **Verification**: Applies strictly to the MCP host; the web application is an independent HTTP host.

### Principle VII: Data Durability & Safe Recovery
- **Status**: **PASS**
- **Verification**: The web container persists no domain data; domain persistence remains in MongoDB named volumes. In-memory session loss upon container restart is accepted per spec.

### Principle VIII: Recorded Decisions & Pinned Dependencies
- **Status**: **PASS**
- **Verification**: Six MADR ADRs (0021 through 0026) have been authored and indexed in `docs/adr/README.md`. Every npm package and container base image tag is pinned to an exact version or digest verified live on the registry.

### Principle IX: Baseline Technical Conventions
- **Status**: **PASS**
- **Verification**: Backend .NET code continues to obey .NET 10, nullable enable, warnings as errors, UTC timestamps, and UUIDv7.

### Minor Constitution Amendment (Frontend Conventions)

*Note: In accordance with Constitution Governance, the proposed frontend conventions are recorded here for ratification in the implementation pull request:*

> **Proposed Amendment (Frontend Quality & Safety Conventions)**:
> 1. All web application code MUST target TypeScript with `strict: true`, `noImplicitAny: true`, and zero compilation errors under `TreatWarningsAsErrors` equivalents.
> 2. All user interface components MUST consume semantic `--ds-*` CSS tokens exclusively; hardcoded hex codes and raw pixel dimensions outside `web/app/ds` are forbidden and enforced via automated linter. `web/app/ds` is explicitly excluded from these lint rules.
> 3. Automated component testing MUST include `axe-core` accessibility checks achieving zero serious and zero critical violations across all routes in both light and dark themes.
> 4. All untrusted external posting and narrative strings MUST be rendered as escaped plain text; `dangerouslySetInnerHTML` is prohibited across all UI views.
> 5. Node.js runtime (v22 LTS) and package manager (`pnpm`) MUST be pinned; frontend dependencies MUST be tracked in Dependabot and lockfiles committed.

---

## Project Structure

### Documentation (this feature)

```text
specs/005b-management-ui/
├── spec.md                     # Feature specification with clarifications
├── plan.md                     # This implementation plan
├── research.md                 # Phase 0 research findings & live registry pins
├── data-model.md               # Phase 1 data models, session entity, and safety rules
├── quickstart.md               # Phase 1 local run & verification guide
└── contracts/                  # Phase 1 interface contracts
    ├── routes.md               # Route loaders, server actions, and error boundaries
    └── design-system-components.md # Design system composites and token rules
```

### Source Code Layout (Monorepo Root)

```text
roleradar/
├── .node-version               # Node 22.23.4 (LTS)
├── nx.json                     # Nx monorepo configuration
├── package.json                # Root package.json orchestrating .NET and web
├── pnpm-lock.yaml              # Pinned monorepo lockfile
├── RoleRadar.sln               # .NET 10 solution
├── Directory.Build.props       # Global MSBuild properties (IsTestingPlatformApplication=false)
├── Directory.Packages.props    # Central Package Management (CPM)
├── contracts/                  # OpenAPI contract & generated client types
│   ├── openapi/roleradar-api.json
│   └── client/api-types.ts
├── deploy/                     # Docker Compose and deployment manifests
│   ├── docker-compose.apps.yml # Includes web service on port 5003
│   ├── Dockerfile.api
│   ├── Dockerfile.identity
│   ├── Dockerfile.web          # Multi-stage hardened Node 22 Alpine build
│   └── certs/                  # Shared dev certificates
├── src/                        # .NET backend projects
│   ├── RoleRadar.Domain/
│   ├── RoleRadar.Application/
│   ├── RoleRadar.Infrastructure/
│   ├── RoleRadar.Identity/
│   ├── RoleRadar.Api/
│   ├── RoleRadar.Mcp.Stdio/
│   └── RoleRadar.Cli/
├── tests/                      # .NET test projects
│   ├── RoleRadar.Tests.Architecture/
│   ├── RoleRadar.Tests.Unit/
│   ├── RoleRadar.Tests.Contract/
│   └── RoleRadar.Tests.Integration/
└── web/                        # React Router v7 Web Management UI (Nx Project)
    ├── package.json
    ├── project.json            # Nx project configuration
    ├── tsconfig.json           # strict: true, allowJs: true, checkJs: false
    ├── vite.config.ts          # Vite configuration with React Router plugin
    ├── vitest.config.ts        # Vitest 5 test runner configuration
    ├── server.ts               # In-process HTTPS Node server entry
    └── app/
        ├── root.tsx            # HTML shell, CSP nonce injector, zero-FOUC theme script
        ├── routes.ts           # Route definitions
        ├── ds/                 # Exact mirrored copy of Matt's Design System (COPY-ONLY)
        │   ├── SYNC.md         # Upstream SHA, license, copy-only expectations
        │   ├── README.md       # Brand book & documentation
        │   ├── tokens.css      # Light/dark CSS semantic custom properties
        │   ├── tokens.json     # Token definitions
        │   ├── fonts/          # Inter and JetBrains Mono woff2 fonts
        │   └── components/
        │       ├── bundle.css  # Focus rings, reduced motion, structural styles (NO base.css)
        │       ├── index.d.ts  # Exported component prop types
        │       ├── <Name>/     # Component types and READMEs (e.g. Button.d.ts, DataTable.d.ts)
        │       └── src/        # Primitives & Composites together (.jsx)
        │           ├── data/       # DataTable.jsx, Toolbar.jsx
        │           ├── feedback/   # Alert.jsx, Avatar.jsx, Badge.jsx, Card.jsx, EmptyState.jsx, Skeleton.jsx, Toast.jsx, ToastRegion.jsx
        │           ├── forms/      # Button.jsx, Checkbox.jsx, Input.jsx, Radio.jsx, Select.jsx, Switch.jsx, Textarea.jsx
        │           ├── layout/     # AppShell.jsx
        │           ├── navigation/ # Tabs.jsx
        │           └── overlay/    # Menu.jsx, Modal.jsx, Tooltip.jsx
        ├── lib/                # Core web application server utilities
        │   ├── oidc.server.ts  # openid-client discovery, PKCE, token refresh
        │   ├── session.server.ts # Encrypted in-memory session store & AES-256-GCM
        │   ├── api-client.server.ts # openapi-fetch client with ErrorEnvelope mapper
        │   └── csrf.server.ts  # Synchronizer CSRF tokens
        └── routes/             # Route modules (loaders, actions, views)
            ├── _index.tsx      # Redirects to /postings
            ├── postings._index.tsx # Postings list with DataTable and Toolbar
            ├── postings.$id.tsx    # Posting detail view & transition dialog
            ├── companies._index.tsx # Companies paged list
            ├── companies.$id.tsx   # Company detail & edit form
            ├── applications._index.tsx # Applications list with resolved names
            ├── applications.$id.tsx # Application detail & event timeline
            ├── criteria.tsx    # Screening criteria read-only view
            ├── notes._index.tsx # Narrative notes search view
            ├── notes.$id.tsx   # Note detail read-only view
            ├── search-runs.tsx # Search runs history view
            ├── auth.callback.tsx # OIDC authorization code exchange
            ├── auth.logout.tsx # RP-initiated sign-out action
            ├── not-authorized.tsx # 403 Forbidden error view
            └── service-unavailable.tsx # 503 Offline error view
```

### Structure & Compilation Decisions

1. **Exact Design System Mirroring**: `web/app/ds` mirrors the kit structure verbatim. There is no `base.css` (global focus ring and reduced motion rules reside in `components/bundle.css`), and there are no artificial `primitives/` or `composites/` directories. Primitives and Part C composites live side-by-side in `components/src/<group>/*.jsx`.
2. **Copy-Only Vendoring Invariant**: As defined in `SYNC.md`, `web/app/ds` is strictly copy-only and never modified locally. All improvements and additions originate in the upstream kit and arrive via clean re-export.
3. **TypeScript & Linter Boundary**: In `web/tsconfig.json`, `allowJs: true` and `checkJs: false` allow the application to import `.jsx` components seamlessly through their `.d.ts` declarations. All application code under `web/app/**` outside `web/app/ds/**` is authored in strictly typed TypeScript. Stylelint/ESLint rules prohibiting hex colors and raw pixel sizes exclude `web/app/ds/**`.
4. **Composite Decoupling**: Composites do not import `lucide-react` (icons arrive as props/children). `AppShell` accepts `navItems`, `linkComponent`, `title`, `theme`, `onThemeToggle`, `userLabel`, `signOut`, and `headerActions` props (no `userSub`) rather than hardcoding routes. Interactive composites (`Toast`, `Menu`) compose caller handlers before internal handlers.
5. **Monorepo Orchestration**: Nx 24 with `@nx/dotnet` coordinates all build, lint, and test targets from the repository root. Caching is explicitly disabled for `RoleRadar.Tests.Contract` and `RoleRadar.Tests.Integration` to ensure Testcontainers and Docker state remain fresh.

---

## Complexity Tracking

| Principle / Invariant | Status | Justification / Architectural Seam |
|-----------------------|--------|------------------------------------|
| **Principle IV (Named Seams Only)** | **PASS** | Generic OIDC client (`openid-client`) is utilized for the web application; zero Duende packages imported outside `RoleRadar.Identity`. |
| **Principle I (Privacy First)** | **PASS** | Tokens never reach the browser; tokens encrypted at rest in server memory; synthetic test data only. |
| **Principle V (Untrusted Text)** | **PASS** | All API strings rendered as escaped text nodes; `dangerouslySetInnerHTML` banned by ESLint. |

*Zero unjustified constitutional violations exist in this plan.*
