import { describe, it, expect } from "vitest";
import React from "react";
import { render, fireEvent } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import CompaniesIndex from "../../app/routes/companies._index.js";
import CompanyDetail from "../../app/routes/companies.$id.js";

describe("Companies Component Views", () => {
  const dummyCompanies = [
    {
      id: "comp-1",
      name: "Acme Corp",
      status: "target",
      ownershipType: "private",
      careerSources: [
        {
          sourceType: "ats",
          atsProvider: "greenhouse",
          atsTenant: "acme",
          sourceUrl: "https://boards.greenhouse.io/acme",
          fetchMethod: "api",
          active: true,
        },
      ],
      versionToken: "cver-1",
    },
    {
      id: "comp-2",
      name: "Beta Systems",
      status: "active",
      ownershipType: "public",
      careerSources: [],
      versionToken: "cver-2",
    },
  ];

  it("renders companies table and toolbar in CompaniesIndex", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/companies",
        Component: CompaniesIndex,
        loader: () => ({
          items: dummyCompanies,
          totalCount: 2,
          limit: 25,
          offset: 0,
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
      },
      {
        path: "/companies/:id",
        Component: () => React.createElement("div", null, "Company Detail Stub"),
      },
    ]);

    const { findByRole, findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/companies"] })
    );

    expect(await findByRole("heading", { name: "Companies" })).toBeDefined();
    expect(await findByText("Acme Corp")).toBeDefined();
    expect(await findByText("Beta Systems")).toBeDefined();

    // Click row to navigate
    const rowEl = await findByText("Acme Corp");
    fireEvent.click(rowEl);
    expect(await findByText("Company Detail Stub")).toBeDefined();
  });

  it("handles pagination in CompaniesIndex", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/companies",
        Component: CompaniesIndex,
        loader: ({ request }) => {
          const url = new URL(request.url);
          const offset = parseInt(url.searchParams.get("offset") || "0", 10);
          return {
            items: dummyCompanies,
            totalCount: 50,
            limit: 25,
            offset,
            csrfToken: "dummy-csrf",
            userLabel: "matt@example.com",
          };
        },
      },
    ]);

    const { findByRole, findByText } = render(
      React.createElement(AppStub, { initialEntries: ["/companies"] })
    );

    expect(await findByText(/Showing 1–25 of 50/)).toBeDefined();
    const nextBtn = await findByRole("button", { name: "Next" });
    fireEvent.click(nextBtn);
    expect(await findByText(/Showing 26–50 of 50/)).toBeDefined();
    expect((nextBtn as HTMLButtonElement).disabled).toBe(true);
  });

  it("renders CompanyDetail edit form, adds/removes category tags, and adds/removes career sources", async () => {
    const sampleCompany = {
      id: "comp-1",
      name: "Acme Corp",
      status: "target",
      ownershipType: "private",
      description: "Cloud infrastructure tools",
      headquarters: "Seattle, WA",
      categoryTags: ["cloud", "infra"],
      careerSources: [
        {
          sourceType: "ats",
          atsProvider: "greenhouse",
          atsTenant: "acme",
          sourceUrl: "https://boards.greenhouse.io/acme",
          fetchMethod: "api",
          active: true,
        },
      ],
      versionToken: "comp-v1",
    };

    const AppStub = createRoutesStub([
      {
        path: "/companies/:id",
        Component: CompanyDetail,
        loader: () => ({
          company: sampleCompany,
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
        action: async () => ({
          ok: true,
          company: sampleCompany,
        }),
      },
    ]);

    const { findByText, findByDisplayValue, findByPlaceholderText, findByRole, findByLabelText, queryByText, findAllByText } =
      render(React.createElement(AppStub, { initialEntries: ["/companies/comp-1"] }));

    expect(await findByDisplayValue("Acme Corp")).toBeDefined();
    expect(await findByDisplayValue("Cloud infrastructure tools")).toBeDefined();
    expect(await findByDisplayValue("Seattle, WA")).toBeDefined();
    expect(await findByText("https://boards.greenhouse.io/acme")).toBeDefined();

    // Remove existing category tag
    const removeTagBtn = await findByLabelText("Remove category tag cloud");
    fireEvent.click(removeTagBtn);
    expect(queryByText("cloud")).toBeNull();

    // Add new category tag via button
    const tagInput = await findByPlaceholderText("New tag...");
    fireEvent.change(tagInput, { target: { value: "kubernetes" } });
    const addTagBtn = await findByRole("button", { name: "Add Tag" });
    fireEvent.click(addTagBtn);
    expect(await findByText("kubernetes")).toBeDefined();

    // Add another category tag via Enter key
    fireEvent.change(tagInput, { target: { value: "golang" } });
    fireEvent.keyDown(tagInput, { key: "Enter" });
    expect(await findByText("golang")).toBeDefined();

    // Adding duplicate tag or empty tag is a no-op
    fireEvent.change(tagInput, { target: { value: "golang" } });
    fireEvent.click(addTagBtn);
    expect((await findAllByText("golang")).length).toBe(1);
    fireEvent.change(tagInput, { target: { value: "   " } });
    fireEvent.click(addTagBtn);
    expect((await findAllByText("golang")).length).toBe(1);

    // Remove career source
    const removeSourceBtn = await findByLabelText(
      "Remove career source https://boards.greenhouse.io/acme"
    );
    fireEvent.click(removeSourceBtn);
    expect(await findByText("No career sources registered.")).toBeDefined();

    // Empty URL add source is a no-op
    const addSourceBtn = await findByRole("button", { name: "Add Source" });
    fireEvent.click(addSourceBtn);
    expect(await findByText("No career sources registered.")).toBeDefined();

    // Add career source with provider, tenant and fetch method
    const urlInput = await findByPlaceholderText("https://jobs.example.com");
    fireEvent.change(urlInput, { target: { value: "https://boards.greenhouse.io/beta" } });
    const providerInput = await findByPlaceholderText("e.g. greenhouse, lever, custom");
    fireEvent.change(providerInput, { target: { value: "greenhouse" } });
    const tenantInput = await findByPlaceholderText("Optional tenant slug");
    fireEvent.change(tenantInput, { target: { value: "beta" } });
    const fetchMethodSelect = await findByLabelText("Fetch Method");
    fireEvent.change(fetchMethodSelect, { target: { value: "browser" } });

    fireEvent.click(addSourceBtn);
    expect(await findByText("https://boards.greenhouse.io/beta")).toBeDefined();

    // Save changes and assert success toast
    const saveBtn = await findByRole("button", { name: "Save Changes" });
    fireEvent.click(saveBtn);
    expect(await findByText("Changes Saved")).toBeDefined();
  });

  it("displays 409 Concurrency Conflict toast on CompanyDetail", async () => {
    const sampleCompany = {
      id: "comp-1",
      name: "Acme Corp",
      status: "target",
      ownershipType: "private",
      categoryTags: [],
      careerSources: [],
      versionToken: "comp-v1",
    };

    const AppStub = createRoutesStub([
      {
        path: "/companies/:id",
        Component: CompanyDetail,
        loader: () => ({
          company: sampleCompany,
          csrfToken: "dummy-csrf",
          userLabel: "matt@example.com",
        }),
        action: async () => ({
          ok: false,
          conflict: true,
          error: "This company was modified by another operation.",
        }),
      },
    ]);

    const { findByRole, findByText, queryByText } = render(
      React.createElement(AppStub, { initialEntries: ["/companies/comp-1"] })
    );

    const saveBtn = await findByRole("button", { name: "Save Changes" });
    fireEvent.click(saveBtn);

    expect(await findByText("Concurrency Conflict")).toBeDefined();
    const reloadBtn = await findByRole("button", { name: "Reload" });
    expect(reloadBtn).toBeDefined();
    fireEvent.click(reloadBtn);
    expect(queryByText("Concurrency Conflict")).toBeNull();
  });
});
