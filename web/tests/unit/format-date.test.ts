import { describe, it, expect } from "vitest";
import { formatDate } from "../../app/lib/format-date.js";

describe("formatDate unit tests", () => {
  it("formats standard ISO UTC timestamp to YYYY-MM-DD", () => {
    expect(formatDate("2026-10-31T12:30:00Z")).toBe("2026-10-31");
    expect(formatDate("2026-01-05T00:00:00Z")).toBe("2026-01-05");
  });

  it("normalizes ISO timestamp with non-zero timezone offset to UTC date", () => {
    // 2026-10-31 23:00 in UTC-5 is 2026-11-01 04:00 in UTC
    expect(formatDate("2026-10-31T23:00:00-05:00")).toBe("2026-11-01");
  });

  it("returns raw input string when date is invalid", () => {
    expect(formatDate("invalid-date-string")).toBe("invalid-date-string");
    expect(formatDate("not a date")).toBe("not a date");
  });

  it("handles null, undefined, and empty string safely", () => {
    expect(formatDate(null)).toBe("");
    expect(formatDate(undefined)).toBe("");
    expect(formatDate("")).toBe("");
  });
});
