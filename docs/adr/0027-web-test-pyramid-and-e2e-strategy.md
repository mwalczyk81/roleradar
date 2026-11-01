# ADR 0027: Web Test Pyramid and Verification Strategy

- **Status**: Accepted
- **Date**: 2026-11-01
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Feature 005b introduces the web management user interface (React Router v7 SSR, Node 22, in-process TLS, OIDC authentication with Duende IdentityServer, encrypted session store, and typed API consumption).

Testing a secure, accessible, local-first management UI presents multiple distinct verification requirements:

1. Fast local feedback loops for business logic, cryptographic invariants, and route loaders/actions without spinning up heavy dependencies.
2. Comprehensive accessibility (WCAG 2.1 AA) and Content Security Policy (CSP) compliance inside an authentic browser engine.
3. Realistic end-to-end integration confirming that the multi-container system (Identity with TOTP, API with MongoDB, and Web with session encryption) operates seamlessly as a unified whole.
4. Production container hardening verification ensuring that images run securely under restricted runtime environments (read-only root filesystem, dropped Linux capabilities, non-root user).

A clear multi-tiered test pyramid is needed to delineate test responsibilities, prevent test duplication, and maintain rapid CI and local execution speeds.

## Decision Drivers

- **Constitution Principle II (Isolated Testing)**: Zero shared state, throwaway test networks, and isolated ephemeral data stores; never touch developer containers.
- **Constitution Principle V (Untrusted Posting Hygiene)**: Verify plain text rendering and strict CSP across all routes.
- **Fast Developer Feedback**: Fast test loop under 30 seconds for unit, architecture, and route suites.
- **Deterministic Browser Verification**: Deterministic headless Chromium execution with reduced motion for axe-core scans.
- **Full Traceability & Auditability**: End-to-end assertion confirming user actions are written to immutable MongoDB `audit_log` records.

## Decision Outcome

We establish a five-tier test pyramid for the web application:

```text
               /\
              /  \     Container Smoke Tests (.NET Integration)
             /----\    - Read-only rootfs, cap-drop, TLS, non-root, fail-closed
            /      \
           /--------\  Real-Stack E2E (Playwright + Throwaway Docker Stack)
          /          \ - Full OIDC + TOTP, real Mongo replica set, API, Web, audit log
         /------------\
        /              \ Chromium Browser Tests with Stubbed API (Playwright)
       /----------------\ - Axe a11y, CSP nonce, theme toggling, UI interactions
      /                  \
     /--------------------\ Route Loader & Action Tests (Vitest + JSDOM)
    /                      \ - SSR loaders, CSRF, HTTP statuses (400/401/403/404/409)
   /------------------------\
  /                          \ Unit & Architecture Tests (Vitest + Node)
 /----------------------------\ - Pure functions, AES-GCM, single-flight refresh, token leakage
```

### 1. Tier 1: Unit & Architecture Tests (`web/tests/unit`, `web/tests/architecture`)

- **Scope & Role**: Pure functions, cryptographic algorithms (AES-256-GCM session serialization/deserialization, fail-closed key validation), single-flight OIDC token refresh deduplication, contract enum snapshot drift checks, navigation links, and architectural dependency rules (e.g. ensuring `web/` never references or imports Duende IdentityServer packages).
- **Environment**: Vitest with Node.js / JSDOM. Fast, in-memory, no external processes.
- **Run Target**: Fast loop (`pnpm --filter web run test`, `pwsh ./scripts/test-fast.ps1`).

### 2. Tier 2: Route Loader & Action Tests (`web/tests/routes`, `web/tests/components`)

- **Scope & Role**: Verifies React Router loader and action functions. Checks CSRF token validation (`validateCsrfToken`), HTTP error mappings (401 unauthenticated redirect, 403 `/not-authorized`, 404, 409 conflict toasts), optimistic concurrency conflict handling, sorting/paging clamp guards, and untrusted text rendering.
- **Environment**: Vitest with JSDOM and mock API client.
- **Run Target**: Fast loop (`pnpm --filter web run test`).

### 3. Tier 3: Chromium Browser Tests with Stubbed API (`web/tests/e2e/csp-style-attribute.spec.ts`)

- **Scope & Role**: Real browser rendering and interaction verification using headless Chromium. Runs axe-core accessibility checks across light and dark modes with reduced motion, validates strict CSP nonce application on script and style tags, verifies zero FOUC during theme switching, and tests client-side component interactions (modals, dialogs, error toasts) against an in-process mock server.
- **Environment**: Playwright + real Chromium browser + in-process Node HTTPS server.
- **Run Target**: Web CI (`pnpm --filter web run test:e2e`).

### 4. Tier 4: Real-Stack End-to-End Test (`web/tests/e2e/smoke.spec.ts` / Real-Stack Runner)

- **Scope & Role**: Full cross-service integration test running against production container images (`roleradar-identity`, `roleradar-api`, `roleradar-web`) and a real MongoDB replica set on an isolated throwaway Docker bridge network. Verifies:
  1. Unauthenticated challenge and redirect to Identity container.
  2. Owner credential and TOTP authenticator authentication.
  3. Authorization code exchange with PKCE and encrypted session establishment.
  4. Postings browsing, filtering, and detail navigation.
  5. Posting disposition transition mutation.
  6. Application creation on a separate posting.
  7. Company profile editing.
  8. Sign-out flow and unauthenticated re-challenge on protected routes.
  9. Direct database query assertion against MongoDB `audit_log` confirming all mutations recorded correct actors and operations.
- **Environment**: Docker daemon + throwaway network + real container images + Playwright Chromium.
- **Run Target**: CI E2E job and opt-in local review gate (`pwsh ./scripts/review-gate.ps1 -IncludeE2E`).

### 5. Tier 5: Container Smoke Tests (`WebContainerSmokeTests.cs` in `RoleRadar.Tests.Integration`)

- **Scope & Role**: Evaluates the web image packaging (`deploy/Dockerfile.web`) under production orchestrator constraints matching `deploy/docker-compose.apps.yml`. Ensures container runs cleanly with read-only root filesystem, dropped capabilities (`cap-drop ALL`), `no-new-privileges`, `node` non-root UID, in-process TLS on port 5003, and fails closed when mandatory secrets (`SESSION_ENCRYPTION_KEY`, certs) are omitted.
- **Environment**: .NET xUnit / Testcontainers / Docker CLI.
- **Run Target**: Full integration suite and review gate (`pwsh ./scripts/review-gate.ps1`).

## Consequences

- **Speed & Isolation**: The local fast loop remains sub-minute because heavy multi-container spin-ups are isolated to Tier 4 and Tier 5.
- **Layer Clarity**: Developers know exactly where to place tests based on what layer of the stack is being tested.
- **Zero Leakage**: Token leakage and security invariants are enforced both at the unit/architecture level (Tier 1) and in the real browser execution (Tier 3 and 4).
