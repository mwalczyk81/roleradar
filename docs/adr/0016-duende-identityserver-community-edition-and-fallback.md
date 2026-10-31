# ADR 0016: Duende IdentityServer Community Edition Licensing and IdP Fallback Strategy

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

Feature 005a introduces an OpenID Connect (OIDC) identity host (`RoleRadar.Identity`) to authenticate the single system owner and issue cryptographically verifiable access tokens for web UI sessions and automated jobs.

Duende IdentityServer is the standard OpenID Connect and OAuth 2.0 framework for .NET. Duende Software provides a **Community Edition** license tier for eligible organizations and personal projects alongside commercial licensing terms.

Because RoleRadar is hosted in a public portfolio repository under the MIT license, we must:

1. Formally record our eligibility under Duende's official licensing terms based on published criteria.
2. Define the exact boundary constraints governing key requirements, non-production usage, and production usage.
3. Address redistribution restrictions and establish hard packaging constraints for container images and packages.
4. Architect a clean decoupling seam so that the identity provider can be swapped for an alternative open-source IdP (such as OpenIddict or Keycloak) without modifying domain logic, API endpoints, or integration tests.

## Decision Drivers

- **Constitution Principle I (Privacy & Public Repository Posture)**: Zero credentials, private tokens, or license keys in git; safe for public MIT inspection.
- **Constitution Principle IV (Named Seams Only)**: Only the dedicated identity host project (`RoleRadar.Identity`) may reference Duende packages; all other projects remain generic OIDC resource servers.
- **Licensing Compliance & Transparency**: Explicitly adhere to Duende's official published terms.
- **Provider Neutrality & Minimal Switching Cost**: API resource servers and integration tests interact strictly via standard OpenID Connect discovery (`.well-known/openid-configuration`), standard JWT claims (`iss`, `sub`, `aud`, `exp`), and standard JWKS key sets.

## Official Licensing Terms and Eligibility Analysis

Official Duende terms retrieved on **October 25, 2026** from:

- [Duende Software Community Edition](https://duendesoftware.com/products/communityedition)
- [Duende Software License Terms](https://duendesoftware.com/license)

### 1. Community Edition Eligibility Thresholds

Per Duende's published criteria, the Community Edition is available for:

- **Personal Projects**: Individual developers building personal, non-commercial software.
- **For-Profit Entities**: Businesses with projected annual gross revenue under **$1,000,000 USD** AND total capital access (outside investment, venture capital, or external debt facilities) under **$3,000,000 USD**.
- **Nonprofit Organizations & Charities**: Entities with a published annual operating budget under **$1,000,000 USD**.

**RoleRadar Context**: RoleRadar is a personal, single-owner job search automation tool developed by an individual maintainer with zero commercial revenue and zero outside capital. It qualifies under the personal projects and sub-$1M gross revenue thresholds.

### 2. License Key Requirements Across Environments

- **Evaluation, Development, Testing, and Personal Projects**: Duende does not require a license key for evaluation, development, automated test environments, or personal projects. When running without a key, a startup informational warning appears in the logs, but identity operations and protocol endpoints function normally without synthetic throttling or feature gating.
- **Production Use**: For production hosting, Duende requires obtaining a Community Edition license key via an application request on their website. If obtained, the key is supplied strictly via the `Duende__LicenseKey` environment variable at runtime and NEVER committed to source control or container images.
- **Hosting Review Note**: Prior to any public hosting or production cloud deployment, the maintainer must re-verify Duende's active license terms and confirm continued compliance.

### 3. Redistribution Restrictions & Hard Packaging Invariants

Duende Software's standard and Community Edition licenses explicitly exclude **Redistribution** ("software you ship to third parties"). Embedding or bundling Duende IdentityServer into a distributed product, client binary, or public container image distributed to third parties requires a separate commercial redistribution license agreement.

To strictly uphold this requirement in a public repository:

- **Hard Packaging Constraint**: The `roleradar-identity` container image and any NuGet package or artifact bundling Duende binaries **MUST NEVER be published to a public container registry (e.g. Docker Hub, GitHub Container Registry) or attached to public GitHub Release assets**.
- **Local & CI Build Scope**: Container images for `RoleRadar.Identity` are built locally by the operator or built ephemerally inside CI runners strictly for test execution and vulnerability scanning; images are discarded upon run completion.

## Fallback IdP Evaluation & Switching Cost

Should licensing terms change or an alternative be required, **OpenIddict** (Apache 2.0) is the designated fallback.

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

Chosen design: **Adopt Duende IdentityServer 8.0.10 under Community Edition with Strict Seam Isolation and Zero Public Distribution**.

### Enforced Constraints

1. **Architecture Test Enforcement**: An automated test in `RoleRadar.Tests.Architecture` verifies that:
   - Only `RoleRadar.Identity` references any package starting with `Duende.*`.
   - `RoleRadar.Api`, `RoleRadar.Application`, `RoleRadar.Domain`, and `RoleRadar.Infrastructure` have zero reference to Duende packages.
2. **Provider-Neutral OIDC Suite**: The end-to-end integration test suite executes strictly against standard discovery documents, standard token endpoints, and standard JWT claims.
3. **No Distribution of Identity Artifacts**: CI workflows and release scripts strictly prohibit pushing `roleradar-identity` images to external registries.
4. **Zero License Keys Committed**: The repository contains zero license key settings; runtime keys (if any) are injected via environment variables.

### Exact Package Versions

- `Duende.IdentityServer`: `8.0.10`
- `Duende.IdentityServer.AspNetIdentity`: `8.0.10`
- `Duende.IdentityServer.EntityFramework`: `8.0.10`

## Consequences

- **Good**: Leverages Duende 8.0.10's proven, automatic cryptographic key management, RFC 7636 PKCE validation, and ASP.NET Core Identity integration.
- **Good**: Zero vendor lock-in for the rest of the solution. The API and client application interact exclusively through standard OIDC protocols.
- **Good**: Fully compliant with Community Edition criteria for personal projects and transparently documents redistribution constraints.
