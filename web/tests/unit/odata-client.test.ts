import { describe, it, expect } from "vitest";
import { buildODataQuery, toODataDateTime } from "../../app/lib/odata-client";

describe("odata-client query builder", () => {
  it("builds basic top and skip parameters", () => {
    const qs = buildODataQuery({ top: 25, skip: 50, count: true });
    expect(qs).toBe("$top=25&$skip=50&$count=true");
  });

  it("builds orderby expressions", () => {
    const qs = buildODataQuery({
      orderBy: [
        { field: "disposition", direction: "desc" },
        { field: "score", direction: "asc" },
      ],
    });
    expect(qs).toBe("$orderby=disposition%20desc%2Cscore%20asc");
  });

  it("builds string filters enforcing tolower() for contains, startswith, endswith", () => {
    const qs = buildODataQuery({
      filter: {
        type: "function",
        name: "contains",
        field: "title",
        value: "Senior Engineer", // Must be lowercased automatically by builder
      },
    });
    expect(qs).toBe(
      "$filter=contains(tolower(title)%2C'senior%20engineer')"
    );
  });

  it("builds composite filter expressions with and/or", () => {
    const qs = buildODataQuery({
      filter: {
        type: "binary",
        operator: "and",
        left: {
          type: "comparison",
          operator: "eq",
          field: "disposition",
          value: "applied",
        },
        right: {
          type: "function",
          name: "startswith",
          field: "companyName",
          value: "Acme",
        },
      },
    });
    expect(qs).toBe(
      "$filter=(disposition%20eq%20'applied')%20and%20(startswith(tolower(companyName)%2C'acme'))"
    );
  });

  it("builds select list", () => {
    const qs = buildODataQuery({
      select: ["id", "title", "companyName", "disposition"],
    });
    expect(qs).toBe("$select=id%2Ctitle%2CcompanyName%2Cdisposition");
  });

  it("builds not filter expressions", () => {
    const qs = buildODataQuery({
      filter: {
        type: "not",
        inner: {
          type: "comparison",
          operator: "eq",
          field: "disposition",
          value: "applied",
        },
      },
    });
    expect(qs).toBe("$filter=not%20(disposition%20eq%20'applied')");
  });

  it("builds plain string filter and orderby", () => {
    const qs = buildODataQuery({
      filter: "score ge 50",
      orderBy: "score desc",
      select: ["id", "title"],
    });
    expect(qs).toBe("$filter=score%20ge%2050&$orderby=score%20desc&$select=id%2Ctitle");
  });

  it("handles orderBy items without direction", () => {
    const qs = buildODataQuery({
      orderBy: [{ field: "title" }],
    });
    expect(qs).toBe("$orderby=title");
  });

  it("percent-encodes search terms with special characters (C&A, 100%, a#b, it's, c++)", () => {
    const terms = [
      { input: "C&A", expectedEncoded: "contains(tolower(title)%2C'c%26a')" },
      { input: "100%", expectedEncoded: "contains(tolower(title)%2C'100%25')" },
      { input: "a#b", expectedEncoded: "contains(tolower(title)%2C'a%23b')" },
      { input: "it's", expectedEncoded: "contains(tolower(title)%2C'it''s')" },
      { input: "c++", expectedEncoded: "contains(tolower(title)%2C'c%2B%2B')" },
    ];

    for (const { input, expectedEncoded } of terms) {
      const qs = buildODataQuery({
        filter: {
          type: "function",
          name: "contains",
          field: "title",
          value: input,
        },
      });
      expect(qs).toBe(`$filter=${expectedEncoded}`);
    }
  });
});

describe("odata-client date-time literals", () => {
  it("converts a plain date to a UTC midnight literal", () => {
    expect(toODataDateTime("2026-10-01")).toEqual({ dateTime: "2026-10-01T00:00:00Z" });
  });

  it("normalizes an offset timestamp to UTC and drops fractional seconds", () => {
    expect(toODataDateTime("2026-10-01T12:30:15.123+02:00")).toEqual({ dateTime: "2026-10-01T10:30:15Z" });
    expect(toODataDateTime(" 2026-10-01T12:30:00Z ")).toEqual({ dateTime: "2026-10-01T12:30:00Z" });
  });

  it("rejects anything that is not a real date", () => {
    for (const bad of ["", "tomorrow", "2026-13-45", "2026-10-01' or true", "2026-10-01T99:99:99Z", "20261001"]) {
      expect(toODataDateTime(bad), bad).toBeUndefined();
    }
  });

  it("emits date-time comparisons unquoted and null as the null literal", () => {
    const qs = buildODataQuery({
      filter: {
        type: "binary",
        operator: "and",
        left: { type: "comparison", operator: "le", field: "effectiveFrom", value: { dateTime: "2026-10-01T00:00:00Z" } },
        right: { type: "comparison", operator: "eq", field: "effectiveTo", value: null },
      },
    });
    expect(decodeURIComponent(qs)).toBe("$filter=(effectiveFrom le 2026-10-01T00:00:00Z) and (effectiveTo eq null)");
  });

  it("refuses to emit a hand-built literal that is not a strict UTC timestamp", () => {
    expect(() =>
      buildODataQuery({
        filter: { type: "comparison", operator: "eq", field: "effectiveFrom", value: { dateTime: "x or true" } },
      })
    ).toThrow(/Invalid OData date-time literal/);
  });
});
