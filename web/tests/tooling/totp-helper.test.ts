import { afterEach, describe, it, expect, vi } from "vitest";
import { base32Decode, nextUnusedTotpCode, totpCode } from "../e2e/support/totp.js";

// RFC 6238 appendix B, SHA-1, secret "12345678901234567890" (Base32 below). The RFC lists 8-digit codes; the last six
// digits are the 6-digit code the authenticator apps and Identity use.
const RFC_SECRET = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // gitleaks:allow (public RFC 6238 test vector, not a credential)

describe("TOTP helper for the screenshot spec (feature 012)", () => {
  it.each([
    [59, "287082"],
    [1111111109, "081804"],
    [1111111111, "050471"],
    [1234567890, "005924"],
    [2000000000, "279037"],
    [20000000000, "353130"],
  ])("matches the RFC 6238 vector at %i seconds", (seconds, expected) => {
    expect(totpCode(RFC_SECRET, seconds * 1000)).toBe(expected);
  });

  it("decodes Base32 like the identity host (case and spaces are ignored, padding is ignored)", () => {
    expect(base32Decode("gezd gnbv").length).toBe(5);
    expect(Buffer.from(base32Decode("MZXW6===")).toString("ascii")).toBe("foo");
  });

  it("rejects an empty secret instead of returning a code", () => {
    expect(() => totpCode("", 0)).toThrow(/secret/i);
  });

  it("returns six digits, zero padded", () => {
    expect(totpCode(RFC_SECRET, 1234567890_000)).toMatch(/^\d{6}$/);
  });
});

// Feature 014 (ADR 0042): the identity host now rejects a code it has already accepted, so a sign-in must never reuse
// one. nextUnusedTotpCode hands out each code at most once per secret, within the five steps the provider accepts, and
// waits for the next 30-second step when all five are used. A time the caller passes to totpCode stays a pure function.
describe("nextUnusedTotpCode (feature 014)", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  const STEP_MS = 30_000;

  it("returns five distinct codes for five calls in one step, in the order 0, +1, -1, +2, -2", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:10Z"));
    const secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"; // fictional
    const current = Math.floor(Date.now() / STEP_MS);

    const codes: string[] = [];
    for (let call = 0; call < 5; call++) {
      codes.push(await nextUnusedTotpCode(secret));
    }

    expect(new Set(codes).size).toBe(5);
    expect(codes).toEqual([0, 1, -1, 2, -2].map((offset) => totpCode(secret, (current + offset) * STEP_MS)));
  });

  it("waits for the next step boundary when all five are used, then returns a step that is new", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:10Z"));
    const secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // gitleaks:allow (public RFC 6238 test vector, not a credential)
    const current = Math.floor(Date.now() / STEP_MS);
    const firstFive: string[] = [];
    for (let call = 0; call < 5; call++) {
      firstFive.push(await nextUnusedTotpCode(secret));
    }

    let settled = false;
    const sixth = nextUnusedTotpCode(secret).then((code) => {
      settled = true;
      return code;
    });
    await vi.advanceTimersByTimeAsync(5_000);
    expect(settled).toBe(false);

    await vi.advanceTimersByTimeAsync(20_000);
    const code = await sixth;

    expect(settled).toBe(true);
    expect(firstFive).not.toContain(code);
    // Now one step later, steps current-1 to current+3 are accepted; current+3 is the first unused one in the usual order.
    expect(code).toBe(totpCode(secret, (current + 3) * STEP_MS));
  });

  it("tracks two secrets separately", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:10Z"));
    const one = "MFRGGZDFMFRGGZDFMFRGGZDFMFRGGZDF";
    const two = "MZXW6YTBOIMZXW6YTBOIMZXW6YTBOIMZ";
    const current = Math.floor(Date.now() / STEP_MS);

    expect(await nextUnusedTotpCode(one)).toBe(totpCode(one, current * STEP_MS));
    expect(await nextUnusedTotpCode(two)).toBe(totpCode(two, current * STEP_MS));
  });

  it("leaves totpCode with an explicit time as the pure RFC 6238 function", () => {
    expect(totpCode(RFC_SECRET, 59 * 1000)).toBe("287082");
    expect(totpCode(RFC_SECRET, 59 * 1000)).toBe("287082");
  });
});
