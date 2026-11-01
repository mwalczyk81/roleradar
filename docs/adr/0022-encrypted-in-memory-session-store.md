# ADR 0022: Encrypted In-Memory Session Store and Single-Flight Token Refresh

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Requirements FR-003 and FR-005 mandate that browser clients never receive or store raw access, refresh, or ID tokens. The browser must hold only a signed cookie containing an opaque session identifier.

In addition:

1. Tokens stored in server memory must be encrypted at rest using AES-256-GCM to prevent memory inspection leaks.
2. The server-side encryption key (`SESSION_ENCRYPTION_KEY`) must fail closed: outside Development, a missing or invalid key must cause the server to refuse startup with a clear stderr error message.
3. Duende IdentityServer configures `RefreshTokenUsage = TokenUsage.OneTimeOnly`. When an access token expires or is near expiry (within 60 seconds), concurrent HTTP requests from the owner must not trigger parallel refresh calls, which would result in token rejection. Refresh must be single-flight.
4. Session identifiers must be rotated upon login and invalidated cleanly upon logout.
5. Cross-Site Request Forgery (CSRF) protection must protect state-changing routes because the session cookie uses `SameSite=Lax`.

What session storage and concurrency architecture should be implemented?

## Decision Drivers

- **Zero Browser Token Leakage (Principle I)**: No tokens in cookies, local storage, or HTML responses.
- **Fail-Closed Secret Management**: Server refuses to start without a valid 256-bit encryption key in production.
- **One-Time Token Safe Concurrency**: Single-flight token refresh promise prevents race conditions against Identity's refresh token rotation.
- **Container Lifecycle Scoping**: For local single-owner operation, session loss upon container restart is acceptable; an in-memory store avoids external database dependencies.
- **CSRF Defense-in-Depth**: Synchronizer token pattern for all POST/PATCH/DELETE mutations.

## Decision Outcome

Chosen design: **Encrypted In-Memory Map Session Store + AES-256-GCM + Single-Flight Promise Lock + Signed Host Cookie**.

### 1. In-Memory Session Store Architecture

The Node server maintains a private memory map:

```typescript
interface SessionData {
  ownerSub: string;
  accessToken: string;
  refreshToken: string;
  idToken?: string;
  expiresAt: number; // Unix epoch ms
  createdAt: number;
}

interface EncryptedSessionRecord {
  ciphertext: string; // Base64
  iv: string;         // Base64 (12 bytes)
  tag: string;        // Base64 (16 bytes)
  expiresAt: number;
}

const sessionStore = new Map<string, EncryptedSessionRecord>();
```

### 2. AES-256-GCM Token Encryption at Rest

Each session record is encrypted using AES-256-GCM with a 32-byte key:

- **Key Source**: `process.env.SESSION_ENCRYPTION_KEY` (base64-encoded 32 bytes).
- **Startup Guard**:

  ```typescript
  export function getSessionEncryptionKey(): Buffer {
    const raw = process.env.SESSION_ENCRYPTION_KEY;
    if (!raw) {
      if (process.env.NODE_ENV === 'development') {
        console.warn('[SECURITY WARNING] SESSION_ENCRYPTION_KEY not set. Generating ephemeral key for development.');
        return crypto.randomBytes(32);
      }
      console.error('[FATAL] RoleRadar Web refused to start: SESSION_ENCRYPTION_KEY is required outside Development.');
      process.exit(1);
    }
    const buf = Buffer.from(raw, 'base64');
    if (buf.length !== 32) {
      console.error('[FATAL] SESSION_ENCRYPTION_KEY must be exactly 32 bytes base64.');
      process.exit(1);
    }
    return buf;
  }
  ```

- **Cipher**: `crypto.createCipheriv('aes-256-gcm', key, iv)` with 12-byte cryptographically random IV per write and 16-byte authentication tag.

### 3. Signed Opaque Session Cookie

- **Cookie Name**: `__Host-roleradar_session` on HTTPS (or `roleradar_session` in non-SSL dev fallback).
- **Attributes**: `HttpOnly; Secure; SameSite=Lax; Path=/`.
- **Value**: HMAC-SHA256 signed opaque session identifier: `${sessionId}.${signature}`.
- **Rotation**: A new session ID is generated on successful authentication; old session state is deleted.

### 4. Single-Flight Token Refresh Lock

To handle concurrent requests safely with `RefreshTokenUsage=OneTimeOnly`:

```typescript
const inFlightRefreshes = new Map<string, Promise<SessionData>>();

export async function getValidSession(sessionId: string): Promise<SessionData | null> {
  const session = decryptSession(sessionId);
  if (!session) return null;

  const now = Date.now();
  const timeRemaining = session.expiresAt - now;

  // If token is valid for more than 60s, return immediately
  if (timeRemaining > 60_000) {
    return session;
  }

  // Single-flight refresh lock: await active refresh or initiate one
  let refreshPromise = inFlightRefreshes.get(sessionId);
  if (!refreshPromise) {
    refreshPromise = (async () => {
      try {
        const tokenSet = await refreshTokens(session.refreshToken);
        const updated: SessionData = {
          ...session,
          accessToken: tokenSet.access_token!,
          refreshToken: tokenSet.refresh_token ?? session.refreshToken,
          expiresAt: Date.now() + ((tokenSet.expires_in ?? 600) * 1000),
        };
        saveSession(sessionId, updated);
        return updated;
      } finally {
        inFlightRefreshes.delete(sessionId);
      }
    })();
    inFlightRefreshes.set(sessionId, refreshPromise);
  }

  return await refreshPromise;
}
```

### 5. CSRF Protection for State-Changing Routes

Because session cookies use `SameSite=Lax`, top-level GET navigations carry the cookie, but cross-site POSTs could theoretically be triggered.

- A synchronizer CSRF token is generated and bound to each active session.
- Every HTML form submission (e.g. disposition transitions, company edits, application creations, and sign-out) includes an `<input type="hidden" name="_csrf" value={csrfToken} />`.
- Server actions validate `_csrf` matches the session CSRF token prior to executing any backend API mutation.

## Consequences

### Positive

- Browser exposure of credentials or tokens is mathematically impossible: the browser only holds an opaque random string.
- In-memory data is encrypted; memory dump inspection cannot yield plain-text bearer tokens.
- Single-flight refresh prevents concurrency failures on token rotation.
- No database migrations or persistent session storage dependencies needed.

### Negative / Trade-offs

- Web container restarts invalidate in-memory sessions, requiring the single owner to re-authenticate (acceptable per spec clarification and single-owner usage).
- If the owner leaves an unsaved form open until session expiration, submitting the form discards input and redirects to login (documented in spec edge cases).
