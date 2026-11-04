import { describe, it, expect } from "vitest";
import { getNavItems } from "../../app/lib/nav.js";

describe("Shared Navigation Items", () => {
  it("includes Postings, Companies, Applications, Criteria, Notes, Search Runs and Insights", () => {
    const items = getNavItems("/postings");
    expect(items).toEqual([
      { label: "Postings", href: "/postings", active: true },
      { label: "Companies", href: "/companies", active: false },
      { label: "Applications", href: "/applications", active: false },
      { label: "Criteria", href: "/criteria", active: false },
      { label: "Notes", href: "/notes", active: false },
      { label: "Search Runs", href: "/search-runs", active: false },
      { label: "Insights", href: "/insights", active: false },
    ]);
  });

  it("activates the matching route prefix", () => {
    expect(getNavItems("/criteria")[3].active).toBe(true);
    expect(getNavItems("/notes")[4].active).toBe(true);
    expect(getNavItems("/notes/note-123")[4].active).toBe(true);
    expect(getNavItems("/search-runs")[5].active).toBe(true);
  });

  it("activates Insights on /insights and nothing else on that page", () => {
    const items = getNavItems("/insights");
    expect(items[6]).toEqual({ label: "Insights", href: "/insights", active: true });
    expect(items.filter((i) => i.active)).toHaveLength(1);
  });
});
