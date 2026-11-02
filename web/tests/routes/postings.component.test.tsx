import { describe, it, expect } from "vitest";
import React from "react";
import { render, fireEvent, waitFor } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import PostingsIndex from "../../app/routes/postings._index.js";

describe("PostingsIndex Component View", () => {
  const dummyPostings = [
    {
      id: "post-1",
      companyId: "c-1",
      companyName: "Acme Corp",
      title: "Senior Cloud Architect",
      requisitionId: "REQ-01",
      location: "San Francisco, CA",
      disposition: "lead",
      resurfacePolicy: "never",
      score: 95.0,
      versionToken: "ver-1",
      createdAt: "2026-10-31T10:00:00Z",
      updatedAt: "2026-10-31T11:00:00Z",
    },
    {
      id: "post-2",
      companyId: "c-2",
      companyName: "Beta Systems",
      title: "Backend Engineer",
      requisitionId: "REQ-02",
      location: "Remote",
      disposition: "applied",
      resurfacePolicy: "never",
      score: 82.5,
      versionToken: "ver-2",
      createdAt: "2026-10-30T10:00:00Z",
      updatedAt: "2026-10-30T11:00:00Z",
    },
  ];

  it("renders postings table and toolbar with search, filters, and records", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings",
        Component: PostingsIndex,
        loader: () => ({
          items: dummyPostings,
          totalCount: 2,
          limit: 25,
          offset: 0,
          csrfToken: "dummy-csrf-token",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByRole, findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/postings"] })
    );

    expect(await findByRole("heading", { name: "Job Postings" })).toBeDefined();
    expect(await findByText("Senior Cloud Architect")).toBeDefined();
    expect(await findByText("Beta Systems")).toBeDefined();
    expect(await findByText("Backend Engineer")).toBeDefined();
  });

  it("supports filter interactions (minScore, disposition) and clears filters via chips", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings",
        Component: PostingsIndex,
        loader: () => ({
          items: dummyPostings,
          totalCount: 2,
          limit: 25,
          offset: 0,
          csrfToken: "dummy-csrf-token",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByLabelText, findByRole, queryByLabelText } = render(
      React.createElement(AppStub, {
        initialEntries: ["/postings?disposition=lead&minScore=80&resurfacePolicy=never&q=Cloud"],
      })
    );

    // Active filter chips should appear
    expect(await findByLabelText("Remove filter Disposition: Lead")).toBeDefined();
    expect(await findByLabelText("Remove filter Min Score: 80")).toBeDefined();
    expect(await findByLabelText("Remove filter Policy: Never")).toBeDefined();

    // Clear all filters button
    const clearAllButton = await findByRole("button", { name: "Clear all" });
    expect(clearAllButton).toBeDefined();
    fireEvent.click(clearAllButton);
    await waitFor(() => {
      expect(queryByLabelText("Remove filter Disposition: Lead")).toBeNull();
      expect(queryByLabelText("Remove filter Min Score: 80")).toBeNull();
      expect(queryByLabelText("Remove filter Policy: Never")).toBeNull();
    });
  });

  it("renders empty state when items list is empty", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings",
        Component: PostingsIndex,
        loader: () => ({
          items: [],
          totalCount: 0,
          limit: 25,
          offset: 0,
          csrfToken: "dummy-csrf-token",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByText } = render(React.createElement(AppStub, { initialEntries: ["/postings"] }));
    expect(await findByText("No job postings match your current filters.")).toBeDefined();
  });

  it("renders pagination controls when totalCount exceeds limit", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings",
        Component: PostingsIndex,
        loader: ({ request }) => {
          const url = new URL(request.url);
          const offset = parseInt(url.searchParams.get("offset") || "0", 10);
          return {
            items: dummyPostings,
            totalCount: 50,
            limit: 25,
            offset,
            csrfToken: "dummy-csrf-token",
            userLabel: "matt@example.com",
          };
        },
      },
    ]);

    const { findByText, findByRole } = render(
      React.createElement(AppStub, { initialEntries: ["/postings"] })
    );
    expect(await findByText(/Showing 1–25 of 50/)).toBeDefined();
    const nextBtn = await findByRole("button", { name: "Next" });
    expect(nextBtn).toBeDefined();
    fireEvent.click(nextBtn);
    expect(await findByText(/Showing 26–50 of 50/)).toBeDefined();
    expect((nextBtn as HTMLButtonElement).disabled).toBe(true);
  });

  it("triggers toolbar input changes, select changes, individual chip removals, and column sort changes", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings",
        Component: PostingsIndex,
        loader: () => ({
          items: dummyPostings,
          totalCount: 2,
          limit: 25,
          offset: 0,
          csrfToken: "dummy-csrf-token",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByLabelText, findByPlaceholderText, findByRole } = render(
      React.createElement(AppStub, {
        initialEntries: ["/postings?disposition=lead&minScore=80&resurfacePolicy=never&q=Cloud"],
      })
    );

    // Individual chip removal
    const removeDispChip = await findByLabelText("Remove filter Disposition: Lead");
    fireEvent.click(removeDispChip);

    const removeMinScoreChip = await findByLabelText("Remove filter Min Score: 80");
    fireEvent.click(removeMinScoreChip);

    const removePolicyChip = await findByLabelText("Remove filter Policy: Never");
    fireEvent.click(removePolicyChip);

    // Selects
    const dispSelect = await findByLabelText(/Filter by Disposition/i);
    fireEvent.change(dispSelect, { target: { value: "scored" } });
    fireEvent.change(dispSelect, { target: { value: "" } });

    const policySelect = await findByLabelText(/Filter by Resurface Policy/i);
    fireEvent.change(policySelect, { target: { value: "if_reopened" } });
    fireEvent.change(policySelect, { target: { value: "" } });

    // Min score input
    const minScoreInput = await findByLabelText(/Filter by Minimum Score/i);
    fireEvent.change(minScoreInput, { target: { value: "90" } });
    fireEvent.change(minScoreInput, { target: { value: "" } });

    // Search input
    const searchInput = await findByLabelText(/Search job postings/i);
    fireEvent.change(searchInput, { target: { value: "engineer" } });
    fireEvent.change(searchInput, { target: { value: "" } });

    // Sort header change
    const companyHeader = await findByRole("button", { name: "Company" });
    fireEvent.click(companyHeader);
  });
});
