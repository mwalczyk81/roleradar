# ADR 0016: Duende IdentityServer Community Edition Licensing and IdP Fallback Strategy

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

Feature 005a introduces an OpenID Connect (OIDC) identity host to authenticate the single system owner and issue cryptographically verifiable access tokens for web UI sessions and automated jobs.

Duende IdentityServer is the premier OpenID Connect and OAuth 2.0 framework for .NET. However, Duende IdentityServer transitioned to the Reciprocal Public License (RPL) commercial model with version 5.0+, offering a **Community Edition** license for eligible organizations and personal projects.

Because RoleRadar is hosted in a public portfolio repository under the MIT license, we must:

1. Formally record our eligibility for Duende Community Edition.
2. Ensure that zero commercial license keys, credentials, or proprietary identifiers are placed in the repository, configuration files, or container images (Constitution Principle I).
3. Architect a clean decoupling seam so that the identity provider can be swapped for an alternative open-source IdP (such as OpenIddict or Keycloak) without modifying any domain logic, API endpoints, or integration tests.

## Decision Drivers

- **Constitution Principle I (Privacy & Public Repository Posture)**: Zero credentials, private tokens, or license keys in git; safe for public MIT inspection.
- **Constitution Principle IV (Named Seams Only)**: Only the dedicated identity host project (`RoleRadar.Identity`) may reference Duende packages; all other projects remain generic OIDC resource servers.
- **Provider Neutrality**: API resource servers and integration tests must interact strictly via standard OpenID Connect discovery (`.well-known/openid-configuration`), standard JWT claims (`iss`, `sub`, `aud`, `exp`), and standard JWKS key sets.
- **Switching Cost Minimization**: If Duende licensing terms change or an alternative is preferred, switching must require minimal effort.

## Evaluation of Duende Community Edition Eligibility

Per Duende Software's official licensing terms:

- **Community Edition Eligibility**: Available for individuals working on personal projects, non-profit organizations, and commercial entities with annual gross revenue under $1,000,000 USD.
- **Repository Context**: RoleRadar is a personal, single-owner job search automation tool developed by an individual developer. It qualifies fully for the free Community Edition.
- **Development & Production Key Usage**:
  - In local development and automated testing, Duende IdentityServer runs without a license key, emitting standard informational license notices.
  - If a Community Edition license key is ever obtained for hosted deployment, it is supplied strictly via the `Duende__LicenseKey` environment variable at runtime and NEVER committed to source control or container images.

## Fallback IdP Evaluation & Switching Cost

Should licensing terms change or open-source pure-play requirements dictate, **OpenIddict** (Apache 2.0) is the primary evaluated fallback.

### Switching Cost Analysis

| Component | Coupling to Duende | Impact If Swapping to OpenIddict or Keycloak |
| :--- | :--- | :--- |
| **`RoleRadar.Domain`** | None | 0 lines changed. |
| **`RoleRadar.Application`** | None | 0 lines changed. |
| **`RoleRadar.Mcp.Stdio`** | None | 0 lines changed. |
| **`RoleRadar.Api`** | None (uses `Microsoft.AspNetCore.Authentication.JwtBearer`) | 0 lines changed. Reads standard authority discovery metadata. |
| **End-to-End Tests** | None (uses standard OIDC protocol discovery and token endpoints) | 0 lines changed. |
| **`RoleRadar.Identity`** | High (Duende host implementation) | Rewritten or replaced with OpenIddict / Keycloak container. |

Because all API resource validation and integration testing use standard OpenID Connect discovery endpoints (`.well-known/openid-configuration`), the switching cost is strictly isolated to replacing the `RoleRadar.Identity` host project itself.

## Decision Outcome

Chosen design: **Adopt Duende IdentityServer 8.0.10 under Community Edition with Strict Seam Isolation**.

### Enforced Constraints

1. **Architecture Test Enforcement**: An automated test in `RoleRadar.Tests.Architecture` verifies that:
   - Only `RoleRadar.Identity` references any package starting with `Duende.*`.
   - `RoleRadar.Api`, `RoleRadar.Application`, `RoleRadar.Domain`, and `RoleRadar.Infrastructure` have zero reference to Duende packages.
2. **Standard OIDC Suite**: The end-to-end integration test suite executes strictly against standard discovery documents, standard token endpoints, and standard JWT claims.
3. **Zero License Keys Committed**: The repository contains zero license key settings; runtime keys (if any) are injected via environment variables.

### Exact Package Versions

- `Duende.IdentityServer`: `8.0.10`
- `Duende.IdentityServer.AspNetIdentity`: `8.0.10`
- `Duende.IdentityServer.EntityFramework`: `8.0.10`

## Consequences

- **Good**: Leverages Duende 8.0.10's proven, automatic cryptographic key management, RFC 7636 PKCE validation, and ASP.NET Core Identity integration.
- **Good**: Zero vendor lock-in for the rest of the solution. The API and client application interact exclusively through standard OIDC protocols.
- **Good**: Fully compliant with Community Edition criteria for personal projects.
