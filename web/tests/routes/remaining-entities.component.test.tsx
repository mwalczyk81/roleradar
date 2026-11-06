import { describe, it, expect } from "vitest";
import React from "react";
import { render, fireEvent, waitFor } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import CompaniesIndex from "../../app/routes/companies._index.js";
import NotesIndex from "../../app/routes/notes._index.js";
import CriteriaIndex from "../../app/routes/criteria.js";
import SearchRunsIndex from "../../app/routes/search-runs.js";

interface Harness {
  urls: URL[];
  last: () => URL;
}

// Renders a route with a loader that records every URL it is asked to load, so a test can prove that
// header clicks, searches and paging go through the URL (and therefore the server) instead of re-sorting locally.
function mount(
  path: string,
  Component: React.ComponentType,
  items: unknown[],
  extra: Record<string, unknown> = {},
  initial = path
) {
  const harness: Harness = { urls: [], last: () => harness.urls[harness.urls.length - 1] };
  const Stub = createRoutesStub([
    {
      path,
      Component,
      loader: ({ request }) => {
        const url = new URL(request.url);
        harness.urls.push(url);
        return {
          items,
          totalCount: 60,
          limit: 25,
          offset: Number(url.searchParams.get("offset") ?? "0"),
          sort: url.searchParams.get("sort") ?? undefined,
          direction: url.searchParams.get("direction") ?? "asc",
          q: url.searchParams.get("q") ?? "",
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
          ...extra,
        };
      },
    },
    { path: `${path}/:id`, Component: () => React.createElement("div", null, "Detail Stub") },
  ]);
  const view = render(React.createElement(Stub, { initialEntries: [initial] }));
  return { harness, ...view };
}

const COMPANIES = [
  { id: "c-1", name: "Acme Robotics", status: "target", ownershipType: "public", headquarters: "Denver, CO", description: null, categoryTags: ["fintech"] },
  { id: "c-2", name: "Beta Systems", status: "mystery", ownershipType: "weird", headquarters: null, description: null, categoryTags: [] },
];

const NOTES = [
  { id: "n-1", heading: "Interview prep", tags: ["prep"], linkedEntityType: "area-a", linkedEntityId: null, createdAt: "2026-10-01T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z" },
  { id: "n-2", heading: "Untagged note", tags: [], linkedEntityType: "", linkedEntityId: null, createdAt: "2026-10-02T00:00:00Z", updatedAt: "2026-10-02T00:00:00Z" },
];

const CRITERIA = [
  { id: "k-1", name: "Remote only", category: "hard_skip", ruleText: "Fully remote", effectiveFrom: "2026-01-01T00:00:00Z", effectiveTo: null, isActive: true },
  { id: "k-2", name: "Salary floor", category: "mystery", ruleText: "180k+", effectiveFrom: "2026-01-01T00:00:00Z", effectiveTo: "2026-06-30T00:00:00Z", isActive: false },
];

const RUNS = [
  { id: "r-1", runDate: "2026-10-31T08:00:00Z", newPostings: 3, summary: "weekly sweep" },
  { id: "r-2", runDate: "2026-10-24T08:00:00Z", newPostings: 0, summary: null },
];

describe("Companies list interactions", () => {
  it("writes a server-side sort to the URL from a column header, and resets paging", async () => {
    const { harness, findByRole } = mount("/companies", CompaniesIndex, COMPANIES, {}, "/companies?offset=25");

    fireEvent.click(await findByRole("button", { name: "Status" }));

    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("status"));
    expect(harness.last().searchParams.get("direction")).toMatch(/^(asc|desc)$/);
    expect(harness.last().searchParams.get("offset")).toBe("0");
  });

  it("does not re-sort the page locally: rows stay in the order the server returned", async () => {
    const { findAllByRole, findByRole } = mount("/companies", CompaniesIndex, COMPANIES);

    fireEvent.click(await findByRole("button", { name: "Company" }));
    fireEvent.click(await findByRole("button", { name: "Company" }));

    const rows = await findAllByRole("row");
    expect(rows[1].textContent).toContain("Acme Robotics");
    expect(rows[2].textContent).toContain("Beta Systems");
  });

  it("submits a name search through the URL", async () => {
    const { harness, findByLabelText } = mount("/companies", CompaniesIndex, COMPANIES);

    const input = await findByLabelText("Search companies");
    fireEvent.change(input, { target: { value: "acme" } });
    fireEvent.submit(input.closest("form") as HTMLFormElement);

    await waitFor(() => expect(harness.last().searchParams.get("q")).toBe("acme"));
  });

  it("pages through the server and keeps company links on one line", async () => {
    const { harness, findByRole, findByText } = mount("/companies", CompaniesIndex, COMPANIES);

    expect(await findByText(/Showing 1–25 of 60/)).toBeDefined();
    fireEvent.click(await findByRole("button", { name: "Next" }));
    await waitFor(() => expect(harness.last().searchParams.get("offset")).toBe("25"));

    const link = (await findByText("Acme Robotics")).closest("a");
    // The no-wrap rule lives in app.css (.cell-nowrap); the computed value is pinned in tests/e2e/converted-styles.spec.ts.
    expect(link?.classList.contains("cell-nowrap")).toBe(true);
  });

  it("renders unknown status and ownership values as plain text and empty headquarters as a dash", async () => {
    const { findByText, container } = mount("/companies", CompaniesIndex, COMPANIES);

    expect(await findByText("mystery")).toBeDefined();
    expect(await findByText("weird")).toBeDefined();
    expect(container.textContent).toContain("—");
  });

  it("shows the empty message when the server returns no rows", async () => {
    const { findByText } = mount("/companies", CompaniesIndex, [], { totalCount: 0 });

    expect(await findByText("No companies found.")).toBeDefined();
  });
});

describe("Notes list interactions", () => {
  it("shows recent notes with no search term and no body preview column", async () => {
    const { findByText, container } = mount("/notes", NotesIndex, NOTES);

    expect(await findByText("Interview prep")).toBeDefined();
    expect(container.textContent).not.toContain("Preview");
    expect(container.textContent).toContain("—");
  });

  it("writes heading, created and updated sorts to the URL", async () => {
    const { harness, findByRole } = mount("/notes", NotesIndex, NOTES);

    fireEvent.click(await findByRole("button", { name: "Heading" }));
    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("heading"));

    fireEvent.click(await findByRole("button", { name: "Created" }));
    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("createdAt"));

    fireEvent.click(await findByRole("button", { name: "Updated" }));
    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("updatedAt"));
  });

  it("submits a heading search and pages through the server", async () => {
    const { harness, findByLabelText, findByRole } = mount("/notes", NotesIndex, NOTES);

    const input = await findByLabelText("Search notes");
    fireEvent.change(input, { target: { value: "prep" } });
    fireEvent.submit(input.closest("form") as HTMLFormElement);
    await waitFor(() => expect(harness.last().searchParams.get("q")).toBe("prep"));

    fireEvent.click(await findByRole("button", { name: "Next" }));
    await waitFor(() => expect(harness.last().searchParams.get("offset")).toBe("25"));
  });

  it("keeps date cells on one line", async () => {
    const { findAllByText } = mount("/notes", NotesIndex, NOTES);

    const dates = await findAllByText("2026-10-03");
    // The no-wrap rule lives in app.css (.cell-nowrap); the computed value is pinned in tests/e2e/converted-styles.spec.ts.
    expect((dates[0] as HTMLElement).classList.contains("cell-nowrap")).toBe(true);
  });

  it("shows the empty message when no notes match", async () => {
    const { findByText } = mount("/notes", NotesIndex, [], { totalCount: 0 });

    expect(await findByText("No notes found.")).toBeDefined();
  });
});

describe("Criteria list interactions", () => {
  it("writes sorts for every sortable column to the URL", async () => {
    const { harness, findByRole } = mount("/criteria", CriteriaIndex, CRITERIA);

    const expectations: Array<[string, string]> = [
      ["Rule Name", "name"],
      ["Category / Weight", "category"],
      ["Effective From", "effectiveFrom"],
      ["Effective To", "effectiveTo"],
      ["Status", "isActive"],
    ];
    for (const [label, field] of expectations) {
      fireEvent.click(await findByRole("button", { name: label }));
      await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe(field));
    }
  });

  it("pages through the server", async () => {
    const { harness, findByRole } = mount("/criteria", CriteriaIndex, CRITERIA);

    fireEvent.click(await findByRole("button", { name: "Next" }));
    await waitFor(() => expect(harness.last().searchParams.get("offset")).toBe("25"));
  });

  it("renders open-ended criteria as Present, unknown categories as plain text, and keeps dates on one line", async () => {
    const { findByText, findAllByText } = mount("/criteria", CriteriaIndex, CRITERIA);

    expect(await findByText("Present")).toBeDefined();
    expect(await findByText("mystery")).toBeDefined();
    expect(await findByText("Inactive")).toBeDefined();
    const dates = await findAllByText("2026-01-01");
    // The no-wrap rule lives in app.css (.cell-nowrap); the computed value is pinned in tests/e2e/converted-styles.spec.ts.
    expect((dates[0] as HTMLElement).classList.contains("cell-nowrap")).toBe(true);
  });

  it("shows the empty message when nothing is in force", async () => {
    const { findByText } = mount("/criteria", CriteriaIndex, [], { totalCount: 0 });

    expect(await findByText("No criteria found.")).toBeDefined();
  });
});

describe("Search runs list interactions", () => {
  it("writes run date and new postings sorts to the URL", async () => {
    const { harness, findByRole } = mount("/search-runs", SearchRunsIndex, RUNS);

    fireEvent.click(await findByRole("button", { name: "Run Date" }));
    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("runDate"));

    fireEvent.click(await findByRole("button", { name: "New Postings" }));
    await waitFor(() => expect(harness.last().searchParams.get("sort")).toBe("newPostings"));
  });

  it("shows only stored columns: no companies-searched, postings-found or errors counters", async () => {
    const { findByText, queryByText, queryByRole } = mount("/search-runs", SearchRunsIndex, RUNS);

    await findByText("weekly sweep");
    for (const header of ["Companies Searched", "Postings Found", "Errors"]) {
      expect(queryByText(header)).toBeNull();
      expect(queryByRole("button", { name: header })).toBeNull();
    }
    expect(queryByText("Run Date")).not.toBeNull();
    expect(queryByText("New Postings")).not.toBeNull();
    expect(queryByText("Batch Summary")).not.toBeNull();
  });

  it("pages through the server, keeps run dates on one line, and renders text as plain text", async () => {
    const { harness, findByRole, findByText } = mount("/search-runs", SearchRunsIndex, [
      ...RUNS,
      { ...RUNS[0], id: "r-3", summary: "<img src=x onerror=alert(1)>" },
    ]);

    fireEvent.click(await findByRole("button", { name: "Next" }));
    await waitFor(() => expect(harness.last().searchParams.get("offset")).toBe("25"));

    expect(await findByText("<img src=x onerror=alert(1)>")).toBeDefined();
    const date = (await findByText("2026-10-24")) as HTMLElement;
    // The no-wrap rule lives in app.css (.cell-nowrap); the computed value is pinned in tests/e2e/converted-styles.spec.ts.
    expect(date.classList.contains("cell-nowrap")).toBe(true);
  });

  it("shows the empty message when there are no runs", async () => {
    const { findByText } = mount("/search-runs", SearchRunsIndex, [], { totalCount: 0 });

    expect(await findByText("No search runs found.")).toBeDefined();
  });
});
