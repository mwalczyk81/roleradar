import crypto from "node:crypto";
import { parse as parseCookie } from "cookie";
import { log } from "./log.server.js";

export interface UserSession {
  sessionId: string;
  ownerSub: string;
  ownerUsername?: string;
  accessToken: string;
  refreshToken: string;
  idToken?: string;
  expiresAt: number; // Unix epoch ms
  createdAt: number;
  csrfToken: string;
}

export interface EncryptedSessionRecord {
  ciphertext: string; // Base64
  iv: string; // Base64 (12 bytes)
  tag: string; // Base64 (16 bytes)
  expiresAt: number;
}

export interface RefreshResult {
  accessToken: string;
  refreshToken?: string;
  idToken?: string;
  expiresAt?: number;
}

export const SECURE_COOKIE_NAME = "__Host-roleradar_session";
export const INSECURE_COOKIE_NAME = "roleradar_session";
export const DEFAULT_ABSOLUTE_SESSION_LIFETIME_MS = 12 * 60 * 60 * 1000; // 12 hours

export function getAbsoluteSessionLifetimeMs(): number {
  if (process.env.SESSION_ABSOLUTE_LIFETIME_MS) {
    const parsed = Number(process.env.SESSION_ABSOLUTE_LIFETIME_MS);
    if (!Number.isNaN(parsed) && parsed > 0) return parsed;
  }
  if (process.env.SESSION_ABSOLUTE_LIFETIME_HOURS) {
    const parsed = Number(process.env.SESSION_ABSOLUTE_LIFETIME_HOURS);
    if (!Number.isNaN(parsed) && parsed > 0) return parsed * 60 * 60 * 1000;
  }
  return DEFAULT_ABSOLUTE_SESSION_LIFETIME_MS;
}

export function isSecureCookieRequired(): boolean {
  if (process.env.COOKIE_SECURE !== undefined) {
    return process.env.COOKIE_SECURE === "true";
  }
  return process.env.NODE_ENV !== "development" && process.env.NODE_ENV !== "test";
}

export function getSessionCookieName(): string {
  return isSecureCookieRequired() ? SECURE_COOKIE_NAME : INSECURE_COOKIE_NAME;
}

interface RoleRadarSessionGlobal {
  __ROLERADAR_SESSION_STORE__?: Map<string, EncryptedSessionRecord>;
  __ROLERADAR_INFLIGHT_REFRESHES__?: Map<string, Promise<UserSession | null>>;
  __ROLERADAR_CACHED_KEY__?: Buffer;
}

const sessionGlobal = globalThis as unknown as RoleRadarSessionGlobal;
let cachedKey: Buffer | null = null;
const sessionStore: Map<string, EncryptedSessionRecord> =
  (sessionGlobal.__ROLERADAR_SESSION_STORE__ ??= new Map<
    string,
    EncryptedSessionRecord
  >());
const inFlightRefreshes: Map<string, Promise<UserSession | null>> =
  (sessionGlobal.__ROLERADAR_INFLIGHT_REFRESHES__ ??= new Map<
    string,
    Promise<UserSession | null>
  >());

export function getSessionEncryptionKey(): Buffer {
  if (cachedKey) return cachedKey;
  if (sessionGlobal.__ROLERADAR_CACHED_KEY__) {
    return sessionGlobal.__ROLERADAR_CACHED_KEY__;
  }

  const raw = process.env.SESSION_ENCRYPTION_KEY;
  if (!raw || raw.trim().length === 0) {
    if (process.env.NODE_ENV === "development" || process.env.NODE_ENV === "test") {
      cachedKey = crypto.randomBytes(32);
      return cachedKey;
    }
    const msg =
      "RoleRadar Web refused to start: SESSION_ENCRYPTION_KEY is required outside Development.";
    log.fatal(msg);
    throw new Error(msg);
  }

  const buf = Buffer.from(raw.trim(), "base64");
  if (buf.length !== 32) {
    const msg =
      "RoleRadar Web refused to start: SESSION_ENCRYPTION_KEY must be exactly 32 bytes base64.";
    log.fatal(msg);
    throw new Error(msg);
  }

  cachedKey = buf;
  return cachedKey;
}

export function setSessionEncryptionKeyForTesting(keyBuf: Buffer | null): void {
  cachedKey = keyBuf;
}

export function encryptSession(session: UserSession): EncryptedSessionRecord {
  const key = getSessionEncryptionKey();
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv("aes-256-gcm", key, iv);

  const serialized = JSON.stringify(session);
  const encrypted = Buffer.concat([
    cipher.update(serialized, "utf8"),
    cipher.final(),
  ]);
  const tag = cipher.getAuthTag();

  return {
    ciphertext: encrypted.toString("base64"),
    iv: iv.toString("base64"),
    tag: tag.toString("base64"),
    expiresAt: session.expiresAt,
  };
}

export function decryptSession(record: EncryptedSessionRecord): UserSession | null {
  try {
    const key = getSessionEncryptionKey();
    const iv = Buffer.from(record.iv, "base64");
    const tag = Buffer.from(record.tag, "base64");
    const ciphertext = Buffer.from(record.ciphertext, "base64");

    const decipher = crypto.createDecipheriv("aes-256-gcm", key, iv);
    decipher.setAuthTag(tag);

    const decrypted = Buffer.concat([
      decipher.update(ciphertext),
      decipher.final(),
    ]);

    return JSON.parse(decrypted.toString("utf8")) as UserSession;
  } catch {
    return null;
  }
}

export function saveSession(session: UserSession): void {
  const record = encryptSession(session);
  sessionStore.set(session.sessionId, record);
}

export function getSessionStoreSizeForTesting(): number {
  return sessionStore.size;
}

export function getSession(sessionId: string): UserSession | null {
  const record = sessionStore.get(sessionId);
  if (!record) return null;

  const session = decryptSession(record);
  if (!session) {
    sessionStore.delete(sessionId);
    return null;
  }

  const now = Date.now();
  const absoluteLifetime = getAbsoluteSessionLifetimeMs();

  // Drop expired session if absolute lifetime is exceeded
  if (now > session.createdAt + absoluteLifetime) {
    sessionStore.delete(sessionId);
    inFlightRefreshes.delete(sessionId);
    return null;
  }

  return session;
}

export function destroySession(sessionId: string): void {
  sessionStore.delete(sessionId);
  inFlightRefreshes.delete(sessionId);
}

export function clearAllSessions(): void {
  sessionStore.clear();
  inFlightRefreshes.clear();
}

export async function getValidSession(
  sessionId: string,
  refreshFn?: (refreshToken: string) => Promise<RefreshResult | null>
): Promise<UserSession | null> {
  const session = getSession(sessionId);
  if (!session) return null;

  const now = Date.now();
  const absoluteLifetime = getAbsoluteSessionLifetimeMs();

  if (now > session.createdAt + absoluteLifetime) {
    destroySession(sessionId);
    return null;
  }

  const timeRemaining = session.expiresAt - now;

  // If token is already expired and cannot be refreshed, drop from store
  if (timeRemaining <= 0 && (!refreshFn || !session.refreshToken)) {
    destroySession(sessionId);
    return null;
  }

  // If token is valid for > 60 seconds, or no refreshFn provided, return existing session
  if (timeRemaining > 60_000 || !refreshFn) {
    return session;
  }

  // Single-flight refresh lock: await in-flight refresh or initiate one
  let refreshPromise = inFlightRefreshes.get(sessionId);
  if (!refreshPromise) {
    refreshPromise = (async () => {
      try {
        const refreshed = await refreshFn(session.refreshToken);
        if (!refreshed || !refreshed.accessToken) {
          // Failed refresh ends the session instead of looping
          destroySession(sessionId);
          return null;
        }

        const updated: UserSession = {
          ...session,
          accessToken: refreshed.accessToken,
          refreshToken: refreshed.refreshToken ?? session.refreshToken,
          idToken: refreshed.idToken ?? session.idToken,
          expiresAt: refreshed.expiresAt ?? Date.now() + 600_000,
        };
        saveSession(updated);
        return updated;
      } catch {
        // Failed refresh ends the session instead of looping
        destroySession(sessionId);
        return null;
      } finally {
        inFlightRefreshes.delete(sessionId);
      }
    })();

    inFlightRefreshes.set(sessionId, refreshPromise);
  }

  return await refreshPromise;
}

export function createSessionCookie(
  sessionId: string,
  secure: boolean = isSecureCookieRequired()
): { name: string; value: string; header: string } {
  const name = secure ? SECURE_COOKIE_NAME : INSECURE_COOKIE_NAME;
  const secureAttr = secure ? "; Secure" : "";
  const header = `${name}=${sessionId}; Path=/; HttpOnly${secureAttr}; SameSite=Lax`;
  return { name, value: sessionId, header };
}

export function destroySessionCookie(
  secure: boolean = isSecureCookieRequired()
): { name: string; header: string } {
  const name = secure ? SECURE_COOKIE_NAME : INSECURE_COOKIE_NAME;
  const secureAttr = secure ? "; Secure" : "";
  const header = `${name}=; Path=/; HttpOnly${secureAttr}; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  return { name, header };
}

export function parseSessionCookie(
  cookieHeader: string | null | undefined
): string | null {
  if (!cookieHeader) return null;
  const parsed = parseCookie(cookieHeader);
  if (isSecureCookieRequired()) {
    // When secure cookies are required, accept ONLY the __Host- prefix cookie. Unprefixed is strictly ignored.
    return parsed[SECURE_COOKIE_NAME] ?? null;
  }
  return parsed[SECURE_COOKIE_NAME] ?? parsed[INSECURE_COOKIE_NAME] ?? null;
}
