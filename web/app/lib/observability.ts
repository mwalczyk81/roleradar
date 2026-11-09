// Feature 013 (ADR 0041): the web half of the shared log contract (specs/013-operability/contracts/log-records.md).
// Pure functions only, so they are unit- and mutation-tested. The rules mirror RoleRadar.Observability's LogScrubber
// and CorrelationId: change both together.

export type LogLevel = "trace" | "debug" | "info" | "warn" | "error" | "fatal";
export type LogFormat = "json" | "text";

export interface LogRecordInput {
  level: LogLevel;
  msg: string;
  event?: string;
  correlationId?: string;
  props?: Record<string, unknown>;
  error?: unknown;
  ts?: Date;
}

export const REDACTED = "[redacted]";

const LEVEL_ORDER: Record<LogLevel, number> = { trace: 0, debug: 1, info: 2, warn: 3, error: 4, fatal: 5 };
const SENSITIVE_FRAGMENTS = ["password", "passphrase", "secret", "token", "authorization", "cookie"];
const SENSITIVE_EXACT = new Set([
  "otp", "totp", "code", "body", "content", "appendedtext", "notebody", "state", "nonce", "codechallenge", "codeverifier",
]);

const CORRELATION_ID = /^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$/;
const AUTH_CREDENTIAL = /\b(Bearer|Basic)\s+[^\s"',;]+/gi;
const JWT = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*/g;
const MONGO_USER_INFO = /(mongodb(?:\+srv)?:\/\/)[^@/\s]+@/gi;
const QUERY_STRING = /(?<=[A-Za-z0-9/._%-])\?[^\s"'<>]*=[^\s"'<>]*/g;
const SENSITIVE_PAIR = /\b([A-Za-z_-]*(?:password|passphrase|secret|token|authorization|cookie)[A-Za-z_-]*|otp|totp|code)=[^\s&"',;]+/gi;
const JSON_PAIR = /"([A-Za-z_][A-Za-z0-9_-]*)"(\s*:\s*)"(?:[^"\\]|\\.)*"/g;

/** True for keys whose values are never logged (case, '-' and '_' ignored). */
export function isSensitiveKey(key: string): boolean {
  const normalized = key.replace(/[-_]/g, "").toLowerCase();
  return SENSITIVE_EXACT.has(normalized) || SENSITIVE_FRAGMENTS.some((fragment) => normalized.includes(fragment));
}

/** Applies every free-text rule. Text without secrets comes back unchanged. */
export function scrub(text: string): string {
  if (text.length === 0) return text;
  return text
    .replace(AUTH_CREDENTIAL, (_m, scheme: string) => `${scheme} ${REDACTED}`)
    .replace(JWT, REDACTED)
    .replace(MONGO_USER_INFO, (_m, scheme: string) => `${scheme}${REDACTED}@`)
    .replace(QUERY_STRING, "")
    .replace(SENSITIVE_PAIR, (_m, key: string) => `${key}=${REDACTED}`)
    .replace(JSON_PAIR, (match, key: string, separator: string) => (isSensitiveKey(key) ? `"${key}"${separator}"${REDACTED}"` : match));
}

/** Structured properties: sensitive keys are redacted, strings are scrubbed, numbers and booleans are kept. */
export function scrubProps(props: Record<string, unknown>): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(props)) {
    if (isSensitiveKey(key)) {
      result[key] = REDACTED;
    } else if (typeof value === "number" || typeof value === "boolean" || value === null || value === undefined) {
      result[key] = value;
    } else {
      result[key] = scrub(typeof value === "string" ? value : String(value));
    }
  }
  return result;
}

export function acceptCorrelationId(candidate: string | null | undefined): string | undefined {
  return typeof candidate === "string" && CORRELATION_ID.test(candidate) ? candidate : undefined;
}

export function newCorrelationId(): string {
  return globalThis.crypto.randomUUID();
}

export function resolveLogFormat(value: string | undefined): LogFormat {
  return value?.trim().toLowerCase() === "json" ? "json" : "text";
}

function isLogLevel(value: string): value is LogLevel {
  return Object.prototype.hasOwnProperty.call(LEVEL_ORDER, value);
}

export function resolveLogLevel(value: string | undefined): LogLevel {
  const candidate = value?.trim().toLowerCase() ?? "";
  return isLogLevel(candidate) ? candidate : "info";
}

export function isLevelEnabled(level: LogLevel, minimum: LogLevel): boolean {
  return LEVEL_ORDER[level] >= LEVEL_ORDER[minimum];
}

function errorObject(error: unknown): Record<string, string> {
  if (error instanceof Error) {
    return {
      type: error.name,
      message: scrub(error.message),
      stack: scrub(error.stack ?? `${error.name}: ${error.message}`),
    };
  }
  return { type: typeof error, message: scrub(String(error)) };
}

function oneLine(value: string): string {
  return value.replace(/\r\n|\n/g, "\\n").replace(/\r/g, "\\r");
}

/** One record on one line (no trailing newline). */
export function formatRecord(input: LogRecordInput, format: LogFormat, service: string = "web"): string {
  const ts = (input.ts ?? new Date()).toISOString();
  const msg = scrub(input.msg);
  const props = input.props && Object.keys(input.props).length > 0 ? scrubProps(input.props) : undefined;
  const error = input.error === undefined ? undefined : errorObject(input.error);

  if (format === "json") {
    const record: Record<string, unknown> = { ts, level: input.level, service, msg };
    if (input.event) record.event = input.event;
    if (input.correlationId) record.correlationId = input.correlationId;
    if (props) record.props = props;
    if (error) record.error = error;
    return JSON.stringify(record);
  }

  let line = `${ts} ${input.level} ${service} [${input.correlationId ?? "-"}] ${oneLine(msg)}`;
  for (const [key, value] of Object.entries(props ?? {})) {
    line += ` ${key}=${oneLine(String(value))}`;
  }
  if (error) {
    line += ` error=${error.type}: ${oneLine(error.message)}`;
  }
  return line;
}
