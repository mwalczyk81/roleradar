import { describe, it, expect } from "vitest";
import { getNavItems } from "../../app/lib/nav.js";

describe("Shared Navigation Items", () => {
  it("includes Postings, Companies, Applications, Criteria, Notes, and Search Runs", () => {
    const items = getNavItems("/postings");
    expect(items).toEqual([
      { label: "Postings", href: "/postings", active: true },
      { label: "Companies", href: "/companies", active: false },
      { label: "Applications", href: "/applications", active: false },
      { label: "Criteria", href: "/criteria", active: false },
      { label: "Notes", href: "/notes", active: false },
      { label: "Search Runs", href: "/search-runs", active: false },
    ]);
  });

  it("activates the matching route prefix", () => {
    expect(getNavItems("/criteria")[3].active).toBe(true);
    expect(getNavItems("/notes")[4].active).toBe(true);
    expect(getNavItems("/notes/note-123")[4].active).toBe(true);
    expect(getNavItems("/search-runs")[5].active).toBe(true);
  });
});
