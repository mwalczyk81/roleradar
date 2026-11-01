import { describe, it, expect } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import CompaniesList from "../../app/routes/companies._index.js";
import CompanyDetail from "../../app/routes/companies.$id.js";

describe("Companies Accessibility & Hostile Text Safety (T042)", () => {
  const sampleCompany = {
    id: "comp-sample-1",
    name: "Acme Industrial Labs",
    status: "target",
    ownershipType: "private",
    description: "Industrial research tools and automation platform.",
    headquarters: "Chicago, IL",
    categoryTags: ["industrial", "robotics"],
    careerSources: [
      {
        sourceType: "ats",
        atsProvider: "greenhouse",
        atsTenant: "acme-industrial",
        sourceUrl: "https://boards.greenhouse.io/acme-industrial",
        fetchMethod: "api",
        active: true,
      },
    ],
    versionToken: "token-sample-1",
  };

  const hostileCompany = {
    id: "comp-hostile-1",
    name: "<script>alert('company-name-xss')</script>",
    status: "watch",
    ownershipType: "public",
    description: "<img src=x onerror=alert('desc-xss')> <b onmouseover='alert(1)'>Dangerous description</b>",
    headquarters: "<iframe src='evil.com'></iframe>Seattle, WA",
    categoryTags: ["<script>tagXss()</script>", "safe-tag"],
    careerSources: [
      {
        sourceType: "ats",
        atsProvider: "custom",
        atsTenant: "test",
        sourceUrl: "javascript:alert('malicious-link')",
        fetchMethod: "browser",
        active: true,
      },
      {
        sourceType: "ats",
        atsProvider: "greenhouse",
        atsTenant: "safe",
        sourceUrl: "https://careers.example.com/safe",
        fetchMethod: "api",
        active: true,
      },
    ],
    versionToken: "token-hostile-1",
  };

  const CompaniesListStub = createRoutesStub([
    {
      path: "/companies",
      Component: CompaniesList,
      loader: () => ({
        items: [sampleCompany],
        totalCount: 1,
        limit: 25,
        offset: 0,
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  const CompanyDetailStub = createRoutesStub([
    {
      path: "/companies/:id",
      Component: CompanyDetail,
      loader: () => ({
        company: sampleCompany,
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  const HostileCompanyDetailStub = createRoutesStub([
    {
      path: "/companies/:id",
      Component: CompanyDetail,
      loader: () => ({
        company: hostileCompany,
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  it("has zero serious or critical accessibility violations on /companies in light and dark themes", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(CompaniesListStub, {
          key: "list-light",
          initialEntries: ["/companies"],
        }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(CompaniesListStub, {
          key: "list-dark",
          initialEntries: ["/companies"],
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("has zero serious or critical accessibility violations on /companies/:id edit form in light and dark themes", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(CompanyDetailStub, {
          key: "detail-light",
          initialEntries: ["/companies/comp-sample-1"],
        }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(CompanyDetailStub, {
          key: "detail-dark",
          initialEntries: ["/companies/comp-sample-1"],
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("renders hostile HTML/scripts as plain inert text without DOM element execution", async () => {
    const { container, findAllByText } = render(
      React.createElement(HostileCompanyDetailStub, {
        initialEntries: ["/companies/comp-hostile-1"],
      })
    );

    // Wait for route to finish initial render
    const matching = await findAllByText(/company-name-xss/);
    expect(matching.length).toBeGreaterThan(0);

    // Zero script, iframe, or onerror img elements injected
    const scripts = container.querySelectorAll("script");
    const iframes = container.querySelectorAll("iframe");
    const errorImgs = container.querySelectorAll("img[onerror]");

    expect(scripts.length).toBe(0);
    expect(iframes.length).toBe(0);
    expect(errorImgs.length).toBe(0);

    // Verify raw strings render as safe text
    expect(container.textContent).toContain("<script>alert('company-name-xss')</script>");
    expect(container.textContent).toContain("<iframe src='evil.com'></iframe>Seattle, WA");
  });

  it("never renders javascript: URLs as clickable links (only http/https links with rel='noopener noreferrer')", async () => {
    const { container, findAllByText } = render(
      React.createElement(HostileCompanyDetailStub, {
        initialEntries: ["/companies/comp-hostile-1"],
      })
    );

    const matching = await findAllByText(/company-name-xss/);
    expect(matching.length).toBeGreaterThan(0);

    // Assert that javascript: URLs are NOT used in any <a href>
    const links = Array.from(container.querySelectorAll("a"));
    const hrefs = links.map((a) => a.getAttribute("href") ?? "");

    for (const href of hrefs) {
      expect(href.toLowerCase()).not.toContain("javascript:");
    }

    // Career links that are valid http/https must include rel="noopener noreferrer"
    const externalLinks = links.filter((a) => {
      const h = a.getAttribute("href") ?? "";
      return h.startsWith("http://") || h.startsWith("https://");
    });

    for (const link of externalLinks) {
      expect(link.getAttribute("rel")).toContain("noopener");
      expect(link.getAttribute("rel")).toContain("noreferrer");
    }
  });
});
