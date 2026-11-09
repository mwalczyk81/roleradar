import { describe, it, expect } from "vitest";
import { base32Decode, totpCode } from "../e2e/support/totp.js";

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
