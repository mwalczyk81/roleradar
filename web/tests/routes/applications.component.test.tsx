import { describe, it, expect } from "vitest";
import React from "react";
import { render, fireEvent } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import ApplicationsIndex from "../../app/routes/applications._index.js";
import ApplicationDetail from "../../app/routes/applications.$id.js";

describe("Applications Component Views", () => {
  const dummyApplications = [
    {
      id: "app-1",
      postingId: "post-1",
      companyId: "comp-1",
      status: "applied",
      appliedAt: "2026-10-31",
      events: [
        {
          eventType: "applied",
          eventDate: "2026-10-31",
          notes: "Initial submission",
        },
      ],
    },
    {
      id: "app-2",
      postingId: "post-2",
      companyId: "comp-2",
      status: "screened",
      appliedAt: "2026-10-25",
      events: [],
    },
  ];

  const dummyPostingMap: Record<string, { id: string; title: string }> = {
    "post-1": { id: "post-1", title: "Senior Cloud Architect" },
    "post-2": { id: "post-2", title: "Backend Engineer" },
  };

  const dummyCompanyMap: Record<string, { id: string; name: string }> = {
    "comp-1": { id: "comp-1", name: "Acme Corp" },
    "comp-2": { id: "comp-2", name: "Beta Systems" },
  };

  it("renders applications table and toolbar in ApplicationsIndex", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/applications",
        Component: ApplicationsIndex,
        loader: () => ({
          items: dummyApplications,
          totalCount: 2,
          limit: 25,
          offset: 0,
          postingMap: dummyPostingMap,
          companyMap: dummyCompanyMap,
          statusFilter: "",
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
      },
      {
        path: "/applications/:id",
        Component: () => React.createElement("div", null, "Application Detail Stub"),
      },
    ]);

    const { findByRole, findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/applications"] })
    );

    expect(await findByRole("heading", { name: "Applications" })).toBeDefined();
    expect(await findByText("Senior Cloud Architect")).toBeDefined();
    expect(await findByText("Acme Corp")).toBeDefined();
    expect(await findByText("Backend Engineer")).toBeDefined();
    expect(await findByText("Beta Systems")).toBeDefined();

    // Click row to navigate
    const rowEl = await findByText("Senior Cloud Architect");
    fireEvent.click(rowEl);
    expect(await findByText("Application Detail Stub")).toBeDefined();
  });

  it("handles status filtering and pagination in ApplicationsIndex", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/applications",
        Component: ApplicationsIndex,
        loader: ({ request }) => {
          const url = new URL(request.url);
          const offset = parseInt(url.searchParams.get("offset") || "0", 10);
          return {
            items: dummyApplications,
            totalCount: 50,
            limit: 25,
            offset,
            postingMap: dummyPostingMap,
            companyMap: dummyCompanyMap,
            statusFilter: "",
            csrfToken: "dummy-csrf",
            userLabel: "matt@example.com",
          };
        },
      },
    ]);

    const { findByRole, findByLabelText, findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/applications"] })
    );

    const filterSelect = await findByLabelText("Filter by Status:");
    fireEvent.change(filterSelect, { target: { value: "applied" } });
    fireEvent.change(filterSelect, { target: { value: "" } });

    expect(await findByText(/Showing 1–25 of 50/)).toBeDefined();
    const nextBtn = await findByRole("button", { name: "Next" });
    fireEvent.click(nextBtn);
    expect(await findByText(/Showing 26–50 of 50/)).toBeDefined();
    expect((nextBtn as HTMLButtonElement).disabled).toBe(true);
  });

  it("renders empty state when items list is empty in ApplicationsIndex", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/applications",
        Component: ApplicationsIndex,
        loader: () => ({
          items: [],
          totalCount: 0,
          limit: 25,
          offset: 0,
          postingMap: {},
          companyMap: {},
          statusFilter: "",
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/applications"] })
    );
    expect(await findByText(/No applications recorded yet/)).toBeDefined();
  });

  it("renders ApplicationDetail view with timeline and event history", async () => {
    const sampleApp = {
      id: "app-1",
      postingId: "post-1",
      companyId: "comp-1",
      status: "applied",
      appliedAt: "2026-10-31",
      events: [
        {
          eventType: "applied",
          eventDate: "2026-10-31",
          notes: "First application step",
        },
        {
          eventType: "screened",
          eventDate: "2026-11-01",
          notes: "HR screening call completed",
        },
      ],
    };

    const samplePosting = {
      id: "post-1",
      title: "Senior Cloud Architect",
      companyName: "Acme Corp",
      location: "Remote",
    };

    const sampleCompany = {
      id: "comp-1",
      name: "Acme Corp",
    };

    const AppStub = createRoutesStub([
      {
        path: "/applications/:id",
        Component: ApplicationDetail,
        loader: () => ({
          application: sampleApp,
          posting: samplePosting,
          company: sampleCompany,
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByText, findByRole, findByLabelText, findByPlaceholderText, queryByText } = render(
      React.createElement(AppStub, { initialEntries: ["/applications/app-1"] })
    );

    expect(await findByText("Senior Cloud Architect")).toBeDefined();
    expect(await findByText("Acme Corp")).toBeDefined();
    expect(await findByText("First application step")).toBeDefined();
    expect(await findByText("HR screening call completed")).toBeDefined();

    // Open append event modal
    const appendBtn = await findByRole("button", { name: "Append Event" });
    fireEvent.click(appendBtn);
    expect(await findByText("Append Timeline Event")).toBeDefined();

    // Select rejected event type and verify sync note is displayed
    const eventTypeSelect = await findByLabelText("Event Type *");
    fireEvent.change(eventTypeSelect, { target: { value: "rejected" } });
    expect(await findByText(/Note: Appending this event will also update/)).toBeDefined();

    // Fill notes and source
    const notesInput = await findByPlaceholderText(
      "Record interview feedback, offer terms, or correspondence details..."
    );
    fireEvent.change(notesInput, { target: { value: "Offer details: $210k base" } });

    const sourceInput = await findByPlaceholderText(
      "e.g. recruiter_email, phone_call, interview_panel"
    );
    fireEvent.change(sourceInput, { target: { value: "recruiter_call" } });

    // Close modal
    const cancelBtn = await findByRole("button", { name: "Cancel" });
    fireEvent.click(cancelBtn);
    expect(queryByText("Append Timeline Event")).toBeNull();
  });

  it("renders empty timeline events state in ApplicationDetail", async () => {
    const emptyApp = {
      id: "app-2",
      postingId: "post-2",
      companyId: "comp-2",
      status: "applied",
      appliedAt: "2026-10-31",
      events: [],
    };

    const AppStub = createRoutesStub([
      {
        path: "/applications/:id",
        Component: ApplicationDetail,
        loader: () => ({
          application: emptyApp,
          posting: { id: "post-2", title: "Test Eng", companyName: "Test Corp" },
          company: { id: "comp-2", name: "Test Corp" },
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/applications/app-2"] })
    );

    expect(await findByText("No timeline events recorded for this application.")).toBeDefined();
  });
});
