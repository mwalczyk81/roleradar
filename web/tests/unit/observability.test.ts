import { describe, it, expect } from "vitest";
import {
  REDACTED,
  acceptCorrelationId,
  formatRecord,
  isSensitiveKey,
  newCorrelationId,
  resolveLogFormat,
  scrub,
  scrubProps,
} from "../../app/lib/observability";

// Feature 013 US1: the web half of the shared log contract (specs/013-operability/contracts/log-records.md).
// The rules mirror RoleRadar.Observability's LogScrubber and CorrelationId, one case per rule (SC-003).

const JWT = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkyIn0.c2lnbmF0dXJlLWZpY3Rpb25hbA"; // gitleaks:allow (fictional redaction test vector, not a credential)
const FIXED = new Date(Date.UTC(2026, 10, 9, 14, 3, 22, 418));

describe("formatRecord", () => {
  it("writes one JSON object with the contract fields", () => {
    const line = formatRecord(
      { level: "info", msg: "Request done", event: "RequestCompleted", correlationId: "test-corr-0001", props: { method: "GET", status: 200 }, ts: FIXED },
      "json"
    );

    expect(line).not.toContain("\n");
    const record = JSON.parse(line);
    expect(record).toEqual({
      ts: "2026-11-09T14:03:22.418Z",
      level: "info",
      service: "web",
      msg: "Request done",
      event: "RequestCompleted",
      correlationId: "test-corr-0001",
      props: { method: "GET", status: 200 },
    });
  });

  it("omits optional fields that are absent", () => {
    const record = JSON.parse(formatRecord({ level: "warn", msg: "plain", ts: FIXED }, "json"));

    expect(Object.keys(record).sort()).toEqual(["level", "msg", "service", "ts"]);
  });

  it("writes the error object, scrubbed", () => {
    const error = new Error(`refused Bearer ${JWT}`);
    const record = JSON.parse(formatRecord({ level: "error", msg: "failed", error, ts: FIXED }, "json"));

    expect(record.error.type).toBe("Error");
    expect(record.error.message).not.toContain(JWT);
    expect(typeof record.error.stack).toBe("string");
    expect(record.error.stack).not.toContain(JWT);
  });

  it("writes a non-Error thrown value as a string error", () => {
    const record = JSON.parse(formatRecord({ level: "error", msg: "failed", error: "plain failure", ts: FIXED }, "json"));

    expect(record.error).toEqual({ type: "string", message: "plain failure" });
  });

  it("writes one readable text line", () => {
    const line = formatRecord(
      { level: "warn", msg: "Posting moved", correlationId: "test-corr-0001", props: { postingId: "p-1" }, ts: FIXED },
      "text"
    );

    expect(line).toBe("2026-11-09T14:03:22.418Z warn web [test-corr-0001] Posting moved postingId=p-1");
  });

  it("uses a dash in text mode when there is no correlation id", () => {
    expect(formatRecord({ level: "info", msg: "started", ts: FIXED }, "text")).toBe("2026-11-09T14:03:22.418Z info web [-] started");
  });

  it("keeps embedded newlines on one line in both modes", () => {
    for (const format of ["json", "text"] as const) {
      const line = formatRecord({ level: "error", msg: "first\r\nsecond", props: { value: "a\nb" }, error: new Error("x\ny"), ts: FIXED }, format);
      expect(line).not.toMatch(/[\r\n]/);
    }
  });

  it("scrubs the message and every property", () => {
    const line = formatRecord(
      {
        level: "warn",
        msg: `header Bearer SENTINEL-TOKEN-7f3a at /auth/callback?code=SENTINEL-CODE&state=SENTINEL-STATE`,
        props: { accessToken: "SENTINEL-ACCESS", note: JWT },
        ts: FIXED,
      },
      "json"
    );

    for (const secret of ["SENTINEL-TOKEN-7f3a", "SENTINEL-CODE", "SENTINEL-STATE", "SENTINEL-ACCESS", JWT]) {
      expect(line).not.toContain(secret);
    }
  });
});

describe("scrub, one case per rule", () => {
  it("redacts Bearer credentials", () => {
    expect(scrub("Authorization: Bearer abc.def-ghi_123")).not.toContain("abc.def-ghi_123");
    expect(scrub("bearer SENTINEL-TOKEN-7f3a")).toContain(REDACTED);
  });

  it("redacts Basic credentials", () => {
    expect(scrub("Authorization: Basic cnItdXNlcjpTRU5USU5FTA==")).not.toContain("cnItdXNlcjpTRU5USU5FTA==");
  });

  it("redacts bare JWT shapes", () => {
    const output = scrub(`token issued ${JWT} for client`);
    expect(output).not.toContain(JWT);
    expect(output.startsWith("token issued ")).toBe(true);
    expect(output.endsWith(" for client")).toBe(true);
  });

  it("redacts mongodb user info", () => {
    for (const input of [
      "mongodb://rr-fictional-user:SENTINEL-PW-7f3a@mongo.test:27017/?replicaSet=rs0",
      "mongodb+srv://rr-fictional-user:SENTINEL-PW-7f3a@cluster0.example.test/roleradar",
    ]) {
      const output = scrub(input);
      expect(output).not.toContain("SENTINEL-PW-7f3a");
      expect(output).not.toContain("rr-fictional-user");
    }
  });

  it("strips query strings from URL-shaped values", () => {
    expect(scrub("GET https://identity.test/auth/callback?code=abc&state=xyz done")).toBe("GET https://identity.test/auth/callback done");
    expect(scrub("/auth/callback?code=abc&state=xyz")).toBe("/auth/callback");
  });

  it("redacts sensitive key=value pairs in free text", () => {
    for (const [input, secret] of [
      ["invalid_grant refresh_token=SENTINEL-REFRESH-7f3a rejected", "SENTINEL-REFRESH-7f3a"],
      ["client_secret=SENTINEL-CS-7f3a&grant_type=x", "SENTINEL-CS-7f3a"],
      ["login failed password=SENTINEL-PW-7f3a", "SENTINEL-PW-7f3a"], // gitleaks:allow (fictional redaction test vector, not a credential)
      ["otp=123456 rejected", "123456"],
    ] as const) {
      const output = scrub(input);
      expect(output).not.toContain(secret);
      expect(output).toContain(REDACTED);
    }
  });

  it("redacts JSON pairs whose key is sensitive and keeps the others", () => {
    const input = `{"State": "SENTINEL-STATE", "Raw": { "code_challenge": "SENTINEL-CHALLENGE", "scope": "openid" }, "client_secret":"SENTINEL-CS", "ClientId": "roleradar-web"}`;
    const output = scrub(input);
    for (const secret of ["SENTINEL-STATE", "SENTINEL-CHALLENGE", "SENTINEL-CS"]) {
      expect(output).not.toContain(secret);
    }
    expect(output).toContain("openid");
    expect(output).toContain("roleradar-web");
  });

  it("leaves values without secrets byte-identical", () => {
    for (const input of ["Now listening on port 5003", "Ready?", "Is the store ready? yes", ""]) {
      expect(scrub(input)).toBe(input);
    }
  });
});

describe("sensitive keys", () => {
  it.each([
    "password", "Password", "passphrase", "secret", "client_secret", "token", "accessToken", "refresh_token",
    "authorization", "Authorization", "cookie", "set-cookie", "otp", "totp", "TOTP", "code", "body", "content",
    "appendedText", "noteBody", "state", "nonce", "code_challenge", "code_verifier", "id_token_hint",
  ])("treats %s as sensitive", (key) => {
    expect(isSensitiveKey(key)).toBe(true);
    expect(scrubProps({ [key]: "SENTINEL-VALUE-7f3a" })).toEqual({ [key]: REDACTED });
  });

  it.each(["statusCode", "method", "path", "durationMs", "subject", "correlationId"])("leaves %s alone", (key) => {
    expect(isSensitiveKey(key)).toBe(false);
    expect(scrubProps({ [key]: "ordinary" })).toEqual({ [key]: "ordinary" });
  });

  it("keeps numbers and booleans as they are", () => {
    expect(scrubProps({ status: 404, ok: false })).toEqual({ status: 404, ok: false });
  });
});

describe("correlation ids", () => {
  it.each([
    "abcdefgh",
    "01970f3e-5b1c-7d2a-9a77-3c2f1e0d4b5a",
    "test-corr-0001",
    "A1.b2_c3-D4",
    "0123456789012345678901234567890123456789012345678901234567890123",
  ])("accepts %s", (candidate) => {
    expect(acceptCorrelationId(candidate)).toBe(candidate);
  });

  it.each([
    null,
    undefined,
    "",
    "abcdefg",
    "01234567890123456789012345678901234567890123456789012345678901234",
    "-abcdefgh",
    ".abcdefgh",
    "abc defgh",
    "abcdefgh\r\n",
    "abcd\nefgh",
    "abcdefgh%0d%0a",
    "abcdéfghij",
    "<script>alert(1)</script>",
  ])("rejects %s", (candidate) => {
    expect(acceptCorrelationId(candidate)).toBeUndefined();
  });

  it("generates UUIDs that are accepted and distinct", () => {
    const first = newCorrelationId();
    const second = newCorrelationId();
    expect(first).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    expect(acceptCorrelationId(first)).toBe(first);
    expect(first).not.toBe(second);
  });
});

describe("resolveLogFormat", () => {
  it.each([
    [undefined, "text"],
    ["", "text"],
    ["text", "text"],
    ["json", "json"],
    ["JSON", "json"],
    [" json ", "json"],
    ["production", "text"],
  ] as const)("%s resolves to %s", (value, expected) => {
    expect(resolveLogFormat(value)).toBe(expected);
  });
});
