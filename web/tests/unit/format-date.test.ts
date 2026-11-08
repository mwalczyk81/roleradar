import { describe, it, expect } from "vitest";
import { formatDate, formatShortDate } from "../../app/lib/format-date.js";

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

describe("formatDate and formatShortDate runtime guard", () => {
  // The signature is typed, but values arrive from JSON at runtime. A truthy non-string must give "", not a date.
  // Reflect.apply passes the untyped value without a cast.
  it("returns an empty string for a truthy non-string value", () => {
    expect(Reflect.apply(formatDate, undefined, [5])).toBe("");
    expect(Reflect.apply(formatDate, undefined, [{ iso: "2026-01-01" }])).toBe("");
    expect(Reflect.apply(formatShortDate, undefined, [5])).toBe("");
    expect(Reflect.apply(formatShortDate, undefined, [{ iso: "2026-01-01" }])).toBe("");
  });
});

describe("formatShortDate unit tests", () => {
  it("writes the UTC month and day as 'Jul 6', never locale formatted", () => {
    expect(formatShortDate("2026-07-06T09:00:00Z")).toBe("Jul 6");
    expect(formatShortDate("2026-12-31T23:59:59Z")).toBe("Dec 31");
    expect(formatShortDate("2026-01-05T00:00:00Z")).toBe("Jan 5");
  });

  it("uses the UTC date, not the offset date", () => {
    expect(formatShortDate("2026-10-31T23:00:00-05:00")).toBe("Nov 1");
  });

  it("keeps formatDate's behaviour for invalid and empty input", () => {
    expect(formatShortDate("not a date")).toBe("not a date");
    expect(formatShortDate(null)).toBe("");
    expect(formatShortDate(undefined)).toBe("");
  });
});
