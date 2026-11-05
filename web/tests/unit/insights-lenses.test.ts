import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  LENS_KEYS,
  COUNTED_LENS_KEYS,
  DRILL_ONLY_LENS_KEYS,
  COVERAGE_KEYS,
  FUNNELS,
  lensEntity,
  lensFilterString,
  lensCountQuery,
  lensListHref,
  coverageFilterString,
  coverageCountQuery,
  parseLensParams,
  defaultAsOf,
  type LensKey,
  type LensContext,
} from "../../app/lib/insights-lenses.js";
import { DISPOSITION_VALUES } from "../../app/lib/dispositions.js";
import { APPLICATION_OUTCOME_VALUES } from "../../app/lib/application-enums.js";

const AS_OF = "2026-09-30";
const NINETY: LensContext = { window: "90d", asOf: AS_OF };
const ALL: LensContext = { window: "all", asOf: AS_OF };

/** Joins clause strings the way the typed builder does: a left-associated chain of "(a) and (b)". */
function chain(parts: string[]): string {
  return parts.reduce((acc, part, i) => (i === 0 ? part : `(${acc}) and (${part})`));
}

const REVIEWED = "((disposition ne 'lead') and (disposition ne 'unscored')) or (hasApplication eq true)";
const MATURE = "appliedAt lt 2026-09-17T00:00:00Z";

// Hand-written clauses per lens (before the window), from contracts/insights-lenses.md.
const CLAUSES: Record<LensKey, string[]> = {
  leads: [],
  reviewed: [REVIEWED],
  "applied-to": [REVIEWED, "hasApplication eq true"],
  "screened-out": [REVIEWED, "disposition eq 'screened_out'"],
  expired: ["disposition eq 'closed'", "hasApplication eq false"],
  applied: [],
  responded: ["firstResponseAt ne null"],
  interviewed: ["hasInterview eq true"],
  offers: ["hasOffer eq true"],
  "offers-interviewed": ["hasOffer eq true", "hasInterview eq true"],
  "offers-without-interview": ["hasOffer eq true", "hasInterview eq false"],
  mature: [MATURE],
  "mature-interviewed": [MATURE, "hasInterview eq true"],
  rejected: ["hasRejection eq true"],
  "rejected-before-interview": ["hasRejection eq true", "rejectedBeforeInterview eq true"],
  "rejected-after-interview": ["hasRejection eq true", "rejectedBeforeInterview eq false"],
  "ghost-eligible": ["appliedAt lt 2026-09-01T00:00:00Z"],
  ghosted: ["status eq 'applied'", "firstResponseAt eq null", "appliedAt lt 2026-08-31T00:00:00Z"],
  open: ["status eq 'applied'"],
};

function windowParts(entity: "postings" | "applications", ctx: LensContext): string[] {
  const field = entity === "postings" ? "firstSeenAt" : "appliedAt";
  return ctx.window === "90d"
    ? [`${field} ge 2026-07-03T00:00:00Z`, `${field} lt 2026-10-01T00:00:00Z`]
    : [`${field} lt 2026-10-01T00:00:00Z`];
}

describe("insights lens catalog", () => {
  it("has the 18 counted lenses and 1 drill-only lens of the data model", () => {
    expect([...LENS_KEYS].sort()).toEqual(Object.keys(CLAUSES).sort());
    expect(LENS_KEYS).toHaveLength(19);
    expect(COUNTED_LENS_KEYS).toHaveLength(18);
    expect([...DRILL_ONLY_LENS_KEYS]).toEqual(["offers-without-interview"]);
    expect([...COUNTED_LENS_KEYS, ...DRILL_ONLY_LENS_KEYS].sort()).toEqual([...LENS_KEYS].sort());
  });

  it("assigns every lens to the postings or applications entity", () => {
    const postings = LENS_KEYS.filter((k) => lensEntity(k) === "postings");
    expect([...postings].sort()).toEqual(["applied-to", "expired", "leads", "reviewed", "screened-out"]);
    expect(LENS_KEYS.filter((k) => lensEntity(k) === "applications")).toHaveLength(14);
  });

  for (const ctx of [NINETY, ALL]) {
    describe(`window ${ctx.window}`, () => {
      for (const key of LENS_KEYS) {
        it(`${key}: exact $filter`, () => {
          const expected = chain([...CLAUSES[key], ...windowParts(lensEntity(key), ctx)]);
          expect(lensFilterString(key, ctx)).toBe(expected);
        });
      }
    });
  }

  it("uses firstSeenAt for posting lenses and appliedAt for application lenses", () => {
    for (const key of LENS_KEYS) {
      const filter = lensFilterString(key, NINETY);
      if (lensEntity(key) === "postings") {
        expect(filter).toContain("firstSeenAt ge 2026-07-03T00:00:00Z");
        expect(filter).not.toContain("appliedAt ge");
      } else {
        expect(filter).toContain("appliedAt ge 2026-07-03T00:00:00Z");
        expect(filter).not.toContain("firstSeenAt");
      }
    }
  });

  it("the 90 day window includes asOf: start is asOf minus 89 days, end is exclusive asOf plus 1 day", () => {
    expect(lensFilterString("leads", { window: "90d", asOf: "2026-03-31" })).toBe(
      "(firstSeenAt ge 2026-01-01T00:00:00Z) and (firstSeenAt lt 2026-04-01T00:00:00Z)"
    );
    // across a year boundary and a leap day
    expect(lensFilterString("applied", { window: "90d", asOf: "2024-03-01" })).toBe(
      "(appliedAt ge 2023-12-03T00:00:00Z) and (appliedAt lt 2024-03-02T00:00:00Z)"
    );
  });

  it("the all-time window keeps only the upper bound", () => {
    expect(lensFilterString("applied", ALL)).toBe("appliedAt lt 2026-10-01T00:00:00Z");
  });

  it("cutoffs follow ageInDays: 14 days old is appliedAt < asOf-13d, 30 days old is < asOf-29d, older than 30 is < asOf-30d", () => {
    const f = (key: LensKey, asOf: string) => lensFilterString(key, { window: "all", asOf });
    expect(f("mature", "2026-09-30")).toContain("appliedAt lt 2026-09-17T00:00:00Z");
    expect(f("ghost-eligible", "2026-09-30")).toContain("appliedAt lt 2026-09-01T00:00:00Z");
    expect(f("ghosted", "2026-09-30")).toContain("appliedAt lt 2026-08-31T00:00:00Z");
    // month and year rollover
    expect(f("mature", "2026-01-05")).toContain("appliedAt lt 2025-12-23T00:00:00Z");
  });
});

describe("insights lens count queries and links", () => {
  it("count queries are count-only: $top=0&$count=true, no $select, no paging", () => {
    for (const key of LENS_KEYS) {
      for (const ctx of [NINETY, ALL]) {
        const qs = lensCountQuery(key, ctx);
        expect(qs).toContain("$top=0");
        expect(qs).toContain("$count=true");
        expect(qs).not.toContain("$select");
        expect(qs).not.toContain("$skip");
        expect(qs).not.toContain("$orderby");
        expect(qs).not.toMatch(/\$top=(?!0(&|$))/);
        const filter = new URLSearchParams(qs).get("$filter");
        expect(filter).toBe(lensFilterString(key, ctx));
      }
    }
  });

  it("every lens is windowed, so a count query always sends a filter", () => {
    expect(lensCountQuery("leads", ALL)).toBe(
      `$filter=${encodeURIComponent("firstSeenAt lt 2026-10-01T00:00:00Z")}&$top=0&$count=true`
    );
  });

  it("drill-down links carry only the lens key, window and asOf and point at the right list", () => {
    expect(lensListHref("applied-to", NINETY)).toBe("/postings?lens=applied-to&window=90d&asOf=2026-09-30");
    expect(lensListHref("ghosted", ALL)).toBe("/applications?lens=ghosted&window=all&asOf=2026-09-30");
    for (const key of LENS_KEYS) {
      const href = lensListHref(key, NINETY);
      expect(href.startsWith(lensEntity(key) === "postings" ? "/postings?" : "/applications?")).toBe(true);
      expect(href).not.toContain("filter");
    }
  });

  it("coverage checks are windowed counts of rows that carry the derived flag", () => {
    expect(COVERAGE_KEYS).toEqual(["coverage-postings", "coverage-applications"]);
    expect(coverageFilterString("coverage-postings", NINETY)).toBe(
      chain(["(hasApplication eq true) or (hasApplication eq false)", ...windowParts("postings", NINETY)])
    );
    expect(coverageFilterString("coverage-applications", ALL)).toBe(
      chain(["(rejectedBeforeInterview eq true) or (rejectedBeforeInterview eq false)", ...windowParts("applications", ALL)])
    );
    expect(coverageCountQuery("coverage-postings", NINETY)).toContain("$top=0&$count=true");
  });

  it("each funnel is one cohort and its stages are lens keys in the documented order", () => {
    expect(FUNNELS.postings).toEqual(["leads", "reviewed", "applied-to"]);
    expect(FUNNELS.applications).toEqual(["applied", "responded", "interviewed", "offers-interviewed"]);
    expect(new Set(FUNNELS.postings.map(lensEntity))).toEqual(new Set(["postings"]));
    expect(new Set(FUNNELS.applications.map(lensEntity))).toEqual(new Set(["applications"]));
  });
});

describe("parseLensParams", () => {
  const NOW = new Date("2026-09-30T10:00:00Z");
  const parse = (query: string, entity: "postings" | "applications") =>
    parseLensParams(new URL(`https://localhost:5003/${entity}?${query}`), entity, NOW);

  it("accepts a valid lens for its own entity", () => {
    expect(parse("lens=applied-to&window=all&asOf=2026-09-01", "postings")).toEqual({
      key: "applied-to",
      ctx: { window: "all", asOf: "2026-09-01" },
    });
  });

  it("defaults the window to 90d and asOf to today (UTC)", () => {
    expect(parse("lens=ghosted", "applications")).toEqual({ key: "ghosted", ctx: { window: "90d", asOf: "2026-09-30" } });
    expect(defaultAsOf(NOW)).toBe("2026-09-30");
  });

  it("ignores unknown keys, prototype names, and keys that belong to the other entity", () => {
    for (const lens of ["nope", "__proto__", "constructor", "toString", "", "LEADS", "leads%20"]) {
      expect(parse(`lens=${lens}`, "postings")).toBeUndefined();
    }
    expect(parse("lens=ghosted", "postings")).toBeUndefined();
    expect(parse("lens=leads", "applications")).toBeUndefined();
    expect(parse("window=all", "postings")).toBeUndefined();
  });

  it("falls back to the defaults for a bad window or asOf instead of forwarding them", () => {
    expect(parse("lens=leads&window=forever&asOf=2026-09-01", "postings")?.ctx).toEqual({ window: "90d", asOf: "2026-09-01" });
    for (const asOf of ["abc", "2026-13-45", "2026-02-30", "2026-9-1", "20260901", "2026-09-01T00:00:00Z", "' or 1 eq 1", "2099-01-01"]) {
      expect(parse(`lens=leads&asOf=${encodeURIComponent(asOf)}`, "postings")?.ctx.asOf).toBe("2026-09-30");
    }
  });

  it("allows asOf up to one day ahead of now (time zones) and no further", () => {
    expect(parse("lens=leads&asOf=2026-10-01", "postings")?.ctx.asOf).toBe("2026-10-01");
    expect(parse("lens=leads&asOf=2026-10-02", "postings")?.ctx.asOf).toBe("2026-09-30");
  });

  it("never reads a raw $filter from the URL", () => {
    const parsed = parse("lens=leads&%24filter=title%20eq%20%27x%27", "postings");
    expect(parsed).toEqual({ key: "leads", ctx: { window: "90d", asOf: "2026-09-30" } });
  });
});

describe("lens filters stay inside the 006 pipeline limits and use Domain values", () => {
  const everyFilter = [
    ...LENS_KEYS.flatMap((k) => [lensFilterString(k, NINETY), lensFilterString(k, ALL)]),
    ...COVERAGE_KEYS.flatMap((k) => [coverageFilterString(k, NINETY), coverageFilterString(k, ALL)]),
  ];

  it("keeps expression depth within 10 and node count within 100", () => {
    for (const filter of everyFilter) {
      let depth = 0;
      let maxDepth = 0;
      for (const ch of filter) {
        if (ch === "(") maxDepth = Math.max(maxDepth, ++depth);
        if (ch === ")") depth--;
      }
      const nodes = (filter.match(/\b(eq|ne|lt|le|gt|ge|and|or|not)\b/g) ?? []).length * 2 + 1;
      expect(maxDepth).toBeLessThanOrEqual(10);
      expect(nodes).toBeLessThanOrEqual(100);
    }
  });

  it("every quoted literal in a filter is a Domain disposition or application status", () => {
    const allowed = new Set<string>([...DISPOSITION_VALUES, ...APPLICATION_OUTCOME_VALUES]);
    for (const filter of everyFilter) {
      for (const m of filter.matchAll(/'([^']*)'/g)) {
        expect(allowed.has(m[1]), `${m[1]} in ${filter}`).toBe(true);
      }
    }
  });

  it("the module types no Domain value as an OData literal; values come from dispositions.ts and application-enums.ts", () => {
    const source = fs.readFileSync(path.resolve(process.cwd(), "app/lib/insights-lenses.ts"), "utf8");
    expect(source).toMatch(/from "\.\/dispositions\.js"/);
    expect(source).toMatch(/from "\.\/application-enums\.js"/);
    const domain = [...DISPOSITION_VALUES, ...APPLICATION_OUTCOME_VALUES].join("|");
    expect(new RegExp(`'(${domain})'`).test(source)).toBe(false);
    expect(new RegExp(`value:\\s*"(${domain})"`).test(source)).toBe(false);
  });

  it("filters never contain a string function, so the tolower() rule is not in play", () => {
    for (const filter of everyFilter) {
      expect(filter).not.toMatch(/contains|startswith|endswith|tolower/);
    }
  });
});
