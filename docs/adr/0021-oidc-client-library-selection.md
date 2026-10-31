# ADR 0021: OIDC Client Library Selection (openid-client v6)

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Feature 005b introduces the management web application for RoleRadar—a confidential OpenID Connect (OIDC) client communicating with the Duende Identity host established in Feature 005a.

To satisfy Core Principle I (Privacy First) and Principle IV (Named Seams Only):

1. The browser client must NEVER receive or store access tokens, refresh tokens, or ID tokens. Tokens must be held and managed exclusively server-side.
2. The web application must communicate with the Identity host using generic OpenID Connect standards without referencing Duende-specific packages.
3. The client must implement Authorization Code flow with PKCE (`S256`), handle silent token refreshes via the `refresh_token` grant, validate ID tokens and claims, and execute standards-compliant end-session logout.

Which JavaScript/TypeScript OpenID Connect client library should be adopted for the server runtime?

## Decision Drivers

- **Standards Compliance & Certification**: Must implement standard OpenID Connect Core 1.0, RFC 7636 (PKCE), RFC 6749 (OAuth 2.0), and OpenID Connect RP-Initiated Logout.
- **Pure Server-Side Execution**: Must execute exclusively in the Node.js server runtime without client-side browser bundle leakage.
- **Active Maintenance & Modern Runtime Support**: Must support Node.js 22 LTS, modern Fetch API, Web Cryptography standards, and pure ECMAScript Modules (ESM).
- **Zero Inappropriate Abstractions**: Principle IV bans unnecessary frameworks or third-party identity dependencies. The library must not dictate database models or tie the application to multi-tenant SaaS providers.
- **Independent Identity Seam**: The library must interact with the IdP through standard OIDC discovery (`/.well-known/openid-configuration`) and JWKS endpoints without importing Duende libraries.

## Considered Options

1. **`openid-client` (v6.8.8) by Filip Skokan (panva)**: Certified OpenID Connect client library rewritten from the ground up for modern JavaScript runtimes using native Web Cryptography and Fetch APIs (`oauth4webapi` foundation).
2. **`openid-client` (v5.7.1)**: Previous major generation of `openid-client`. Uses legacy Node.js-specific `http`, `crypto`, and `got` dependencies. Now in maintenance mode.
3. **`@auth/core` / `next-auth`**: High-level authentication framework designed primarily for Next.js and multi-provider social logins. Imposes database adapter schemas and opinionated session management.
4. **`arctic`**: Minimalist OAuth 2.0 client library. Lacks certified OpenID Connect discovery auto-configuration, automatic JWKS rotation, and certified ID token validation.
5. **Hand-Rolled Fetch Wrappers**: Writing manual HTTP requests to `/connect/token` and `/connect/authorize`. Error-prone with cryptographic state generation, timing attacks, and JWKS caching.

## Decision Outcome

Chosen option: **`openid-client` version `6.8.8`** paired with **React Router v7 framework mode (`7.18.5`)**.

### Rationale

`openid-client` is the de facto standard and certified OpenID Connect client for Node.js. Version 6 represents a modern, functional, zero-external-dependency implementation built on Web Standards:

- It natively leverages `fetch`, `CryptoKey`, and `URL`, avoiding legacy Node.js stream or HTTP callback wrappers.
- It seamlessly integrates with Node 22 LTS in React Router v7 framework mode.
- **Framework Version Selection**: The choice of React Router 7.x (`7.18.5`) is deliberate per the project specification (FR-001) even though React Router 8.x exists on npm. React Router 7 is the stable, proven framework-mode foundation for server-rendered web applications with long-term ecosystem alignment, whereas 8.x introduces breaking paradigm shifts that are out of scope for Feature 005b.
- It supports standard PKCE (`S256`), RFC 7009 token revocation, RFC 7662 token introspection, and RP-Initiated Logout.
- It leaves session and cookie management entirely under RoleRadar's control, enabling an encrypted server-side session store with zero browser token exposure.

### Key API Signatures & Integration Pattern

```typescript
import * as client from 'openid-client';

// 1. Discovery from Identity Host
const serverUrl = new URL(process.env.ROLERADAR_IDENTITY_ISSUER_URI ?? 'https://identity:5001');
const clientId = 'roleradar-web';
const clientSecret = process.env.ROLERADAR_WEB_CLIENT_SECRET;

export const oidcConfig = await client.discovery(
  serverUrl,
  clientId,
  clientSecret
);

// 2. PKCE Authorization URL Generation
export function generateAuthorizationParams() {
  const codeVerifier = client.randomPKCECodeVerifier();
  const codeChallenge = await client.calculatePKCECodeChallenge(codeVerifier);
  const state = client.randomState();

  const authUrl = client.buildAuthorizationUrl(oidcConfig, {
    redirect_uri: process.env.ROLERADAR_WEB_REDIRECT_URI ?? 'https://localhost:5003/auth/callback',
    scope: 'openid profile roleradar.api offline_access',
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
    state,
  });

  return { authUrl, codeVerifier, state };
}

// 3. Authorization Code Exchange
export async function exchangeCodeForTokens(
  currentUrl: URL,
  codeVerifier: string,
  expectedState: string
) {
  const tokenSet = await client.authorizationCodeGrant(
    oidcConfig,
    currentUrl,
    {
      pkceCodeVerifier: codeVerifier,
      expectedState,
    }
  );
  return tokenSet;
}

// 4. Token Refresh
export async function refreshTokens(refreshToken: string) {
  return await client.refreshTokenGrant(oidcConfig, refreshToken);
}

// 5. End Session URL Generation
export function getEndSessionUrl(idTokenHint?: string) {
  return client.buildEndSessionUrl(oidcConfig, {
    post_logout_redirect_uri: process.env.ROLERADAR_WEB_POST_LOGOUT_URI ?? 'https://localhost:5003/auth/logout',
    id_token_hint: idTokenHint,
  });
}
```

## Consequences

### Positive

- Strict compliance with OpenID Connect Core 1.0 and PKCE.
- Zero client-side bundle impact; executed strictly on Node server loaders/actions.
- Decoupled from Duende IdentityServer implementations—RoleRadar.Identity can be upgraded or substituted without changing web UI auth logic.

### Negative / Trade-offs

- Pure ESM requires the Node project (`web/`) to declare `"type": "module"` and use modern module resolution.
- OIDC metadata discovery requires outbound HTTPS trust to the Identity container during startup, resolved via ADR-0025 (`NODE_EXTRA_CA_CERTS`).
