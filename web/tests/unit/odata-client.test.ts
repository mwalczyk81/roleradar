import { describe, it, expect } from "vitest";
import { buildODataQuery } from "../../app/lib/odata-client";

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
