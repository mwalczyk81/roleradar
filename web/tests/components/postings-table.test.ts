import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render, fireEvent, screen } from "@testing-library/react";
import { DataTable, Toolbar } from "../../app/ui.js";

describe("Postings DataTable & Toolbar (T029)", () => {
  const samplePostings = [
    {
      id: "p1",
      companyName: "Beta LLC",
      title: "Senior Backend Engineer",
      location: "Remote",
      disposition: "scored",
      score: 92,
      createdAt: "2026-10-15T10:00:00Z",
    },
    {
      id: "p2",
      companyName: "Alpha Inc",
      title: "Principal Engineer",
      location: "New York, NY",
      disposition: "lead",
      score: 78,
      createdAt: "2026-10-20T10:00:00Z",
    },
  ];

  const columns = [
    { key: "companyName", header: "Company", sortable: true },
    { key: "title", header: "Role Title", sortable: true },
    { key: "score", header: "Score", sortable: true, numeric: true },
    { key: "disposition", header: "Disposition", sortable: true },
  ];

  it("renders table with caption, headers, rows and page-local sorting", () => {
    const { container } = render(
      React.createElement(DataTable, {
        caption: "Job Postings",
        columns,
        data: samplePostings,
        keyField: "id",
        defaultSortColumn: "score",
        defaultSortDirection: "desc",
      })
    );

    expect(screen.getByText("Beta LLC")).toBeDefined();
    expect(screen.getByText("Alpha Inc")).toBeDefined();

    // Verify sort column header is clickable for page-local sort
    const companyHeader = screen.getByRole("button", { name: /Company/i });
    expect(companyHeader).toBeDefined();

    fireEvent.click(companyHeader);

    // After clicking company header, Alpha Inc is first row
    const rows = container.querySelectorAll("tbody tr");
    expect(rows[0].textContent).toContain("Alpha Inc");
    expect(rows[1].textContent).toContain("Beta LLC");
  });

  it("renders Toolbar with search input and filter chips", () => {
    const onSearchChange = vi.fn();
    const onRemoveFilter = vi.fn();

    render(
      React.createElement(Toolbar, {
        searchPlaceholder: "Search postings...",
        searchValue: "engineer",
        onSearchChange,
        filters: [
          {
            id: "disp",
            label: "Disposition",
            value: "Scored",
            onRemove: onRemoveFilter,
          },
        ],
      })
    );

    expect(screen.getByPlaceholderText("Search postings...")).toBeDefined();
    expect(screen.getByText(/Scored/)).toBeDefined();

    const removeBtn = screen.getByRole("button", { name: /Remove filter/i });
    fireEvent.click(removeBtn);
    expect(onRemoveFilter).toHaveBeenCalledTimes(1);
  });

  it("has zero accessibility violations in light and dark mode", async () => {
    const { container: lightContainer } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(Toolbar, {
          key: "t",
          searchPlaceholder: "Search postings...",
          searchLabel: "Search postings",
          filters: [],
        }),
        React.createElement(DataTable, {
          key: "d",
          caption: "Job Postings",
          columns,
          data: samplePostings,
          keyField: "id",
        }),
      ])
    );
    await expect(lightContainer).toHaveNoViolations();

    const { container: darkContainer } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(Toolbar, {
          key: "t",
          searchPlaceholder: "Search postings...",
          searchLabel: "Search postings",
          filters: [],
        }),
        React.createElement(DataTable, {
          key: "d",
          caption: "Job Postings",
          columns,
          data: samplePostings,
          keyField: "id",
        }),
      ])
    );
    await expect(darkContainer).toHaveNoViolations();
  });
});
