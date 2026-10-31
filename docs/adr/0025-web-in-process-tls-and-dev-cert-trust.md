# ADR 0025: Web Host In-Process TLS Termination, Dev Certificate Trust, and Port 5003 Migration

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Feature 005b introduces the web application container (`roleradar-web`) to the local Docker Compose stack (`deploy/docker-compose.apps.yml`).

Several transport and trust requirements must be coordinated:

1. **Cookie Security**: The session cookie must use `Secure` and `SameSite=Lax` (and the `__Host-` prefix). Modern browsers refuse `Secure` cookies over unencrypted HTTP (except plain `http://localhost`, but `__Host-` cookies and OIDC redirect security prefer HTTPS). The web application server must terminate TLS directly on `https://localhost:5003` using the repository's existing local development certificate (`deploy/certs/dev-cert.pfx` or `dev-cert.crt`/`dev-cert.key`).
2. **Backchannel Container Trust**: The web container makes backchannel OIDC discovery/token requests to `https://identity:5001` and REST calls to `https://api:5002`. Both endpoints present certificates signed by the local development certificate authority. By default, Node.js refuses self-signed or private CA certificates.
3. **Redirect URI Migration**: In Feature 005a, `IdentityServerConfig` configured the `roleradar-web` client with defaults pointing to port 5002 (`https://localhost:5002/signin-oidc`), which conflicts with the API host. The registered redirect and post-logout URIs must point to port 5003 (`https://localhost:5003/auth/callback` and `https://localhost:5003/auth/logout`).
4. **Hardened Container Runtime**: The web image must run as an unprivileged user with a read-only root filesystem, tmpfs `/tmp`, and an internal healthcheck.

How should web host TLS termination, certificate trust, and container networking be structured?

## Decision Drivers

- **Zero Insecure Flags in Production/Testing**: Avoid disabling TLS verification (`NODE_TLS_REJECT_UNAUTHORIZED=0` is strictly forbidden).
- **Consistent Local CA Authority**: Reuse `deploy/certs/dev-cert.crt` and `dev-cert.key` which already include Subject Alternative Names (SANs) for `localhost`, `identity`, `api`, and `127.0.0.1`.
- **Minimal Container Attack Surface**: Non-root execution, read-only rootfs, no host network leaks (port 5003 bound to `127.0.0.1` only).

## Decision Outcome

Chosen design: **Node HTTPS Server with Mounted Dev Cert + NODE_EXTRA_CA_CERTS Trust Anchor + Port 5003 Registration**.

### 1. In-Process Node TLS Termination

The web application's production and development Node server loads certificates from `/https/cert.key` and `/https/cert.crt` (or converts from `.pfx`):

```typescript
import https from 'node:https';
import fs from 'node:fs';

const options: https.ServerOptions = {
  key: fs.readFileSync(process.env.HTTPS_CERT_KEY_PATH ?? './deploy/certs/dev-cert.key'),
  cert: fs.readFileSync(process.env.HTTPS_CERT_PATH ?? './deploy/certs/dev-cert.crt'),
};

const server = https.createServer(options, app);
server.listen(5003, '0.0.0.0', () => {
  console.error('[INFO] RoleRadar Web listening on https://*:5003');
});
```

### 2. Backchannel Certificate Trust via `NODE_EXTRA_CA_CERTS`

To allow Node's native `fetch` and `openid-client` to trust `https://identity:5001` and `https://api:5002`:

- Mount `./certs/dev-cert.crt:/https/dev-cert.crt:ro` into the web container.
- Set environment variable:

  ```yaml
  environment:
    - NODE_EXTRA_CA_CERTS=/https/dev-cert.crt
  ```

Node automatically appends the certificate to its internal root CA bundle for all outbound TLS handshakes. No custom `Agent` or unsafe bypass is required.

### 3. Identity Host Client Redirect URI Update

In `src/RoleRadar.Identity/Configuration/IdentityServerConfig.cs`:

```csharp
var webRedirectUri = configuration["Clients:Web:RedirectUri"]
    ?? "https://localhost:5003/auth/callback";
var webPostLogoutUri = configuration["Clients:Web:PostLogoutRedirectUri"]
    ?? "https://localhost:5003/auth/logout";
```

The client definition allows both `https://localhost:5003/auth/callback` and `https://localhost:5002/signin-oidc` during transition, with 5003 as the default.

### 4. Hardened Multi-Stage Dockerfile Layout

Base image: `node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914`

- Multi-stage build with `pnpm install --frozen-lockfile` and `pnpm build`.
- Final stage runs as unprivileged `node:node` (UID 1000:1000).
- Root filesystem is `read_only: true`.
- Tmpfs mounted at `/tmp:rw,noexec,nosuid,size=64m`.
- Loopback healthcheck checks `https://127.0.0.1:5003/health` using Node script or curl with `--cacert /https/dev-cert.crt`.
- Compose ports: `"127.0.0.1:5003:5003"`.

### 5. Compose Service Definition (`deploy/docker-compose.apps.yml`)

```yaml
  web:
    profiles: ["apps"]
    build:
      context: ..
      dockerfile: deploy/Dockerfile.web
    image: roleradar-web:local
    read_only: true
    tmpfs:
      - /tmp:rw,noexec,nosuid,size=64m
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true
    ports:
      - "127.0.0.1:5003:5003"
    environment:
      - NODE_ENV=production
      - NODE_EXTRA_CA_CERTS=/https/dev-cert.crt
      - ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001
      - ROLERADAR_API_BASE_URL=https://api:5002
      - ROLERADAR_WEB_CLIENT_SECRET=${ROLERADAR_WEB_CLIENT_SECRET:?set it}
      - SESSION_ENCRYPTION_KEY=${SESSION_ENCRYPTION_KEY:?set it}
      - HTTPS_CERT_PATH=/https/cert.crt
      - HTTPS_CERT_KEY_PATH=/https/cert.key
    volumes:
      - ./certs/dev-cert.crt:/https/dev-cert.crt:ro
      - ./certs/dev-cert.crt:/https/cert.crt:ro
      - ./certs/dev-cert.key:/https/cert.key:ro
    depends_on:
      identity:
        condition: service_healthy
      api:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "node", "-e", "fetch('https://127.0.0.1:5003/health').then(r => r.ok ? process.exit(0) : process.exit(1)).catch(() => process.exit(1))"]
      interval: 5s
      timeout: 3s
      retries: 3
      start_period: 4s
```

## Consequences

### Positive

- Strict HTTPS end-to-end enables `__Host-` cookies and satisfies browser security requirements.
- Uses native `NODE_EXTRA_CA_CERTS` without monkey-patching TLS agents.
- Local compose stack services bind exclusively to `127.0.0.1`, eliminating LAN exposure.

### Negative / Trade-offs

- Developers must trust `dev-cert.crt` in their local browser or accept the self-signed warning once on `https://localhost:5003`.
