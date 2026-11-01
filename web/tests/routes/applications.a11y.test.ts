import { describe, it, expect } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import ApplicationsList from "../../app/routes/applications._index.js";
import ApplicationDetail from "../../app/routes/applications.$id.js";

describe("Applications Accessibility & Hostile Text Safety (T048)", () => {
  const sampleApp = {
    id: "app-sample-1",
    postingId: "post-sample-1",
    companyId: "comp-sample-1",
    status: "interview",
    appliedAt: "2026-10-15",
    events: [
      {
        eventType: "applied",
        eventDate: "2026-10-15",
        notes: "Applied via referral link",
      },
      {
        eventType: "interview",
        eventDate: "2026-10-22",
        notes: "Round 1 technical phone screen",
      },
    ],
  };

  const samplePosting = {
    id: "post-sample-1",
    title: "Principal Infrastructure Engineer",
    companyName: "Acme Industrial Labs",
    companyId: "comp-sample-1",
    location: "Remote, US",
    disposition: "applied",
  };

  const sampleCompany = {
    id: "comp-sample-1",
    name: "Acme Industrial Labs",
  };

  const hostileApp = {
    id: "app-hostile-1",
    postingId: "post-hostile-1",
    companyId: "comp-hostile-1",
    status: "applied",
    appliedAt: "2026-10-31",
    events: [
      {
        eventType: "applied",
        eventDate: "2026-10-31",
        notes: "<script>alert('event-notes-xss')</script> <img src=x onerror=alert('img-xss')>",
      },
      {
        eventType: "interview",
        eventDate: "2026-11-01",
        notes: "<iframe src='javascript:alert(1)'></iframe>Hostile interview details",
      },
    ],
  };

  const hostilePosting = {
    id: "post-hostile-1",
    title: "<script>alert('posting-title-xss')</script>",
    companyName: "<b onmouseover='alert(2)'>Hostile Company</b>",
    companyId: "comp-hostile-1",
    location: "Remote",
    disposition: "applied",
  };

  const hostileCompany = {
    id: "comp-hostile-1",
    name: "<script>alert('company-name-xss')</script>",
  };

  const ApplicationsListStub = createRoutesStub([
    {
      path: "/applications",
      Component: ApplicationsList,
      loader: () => ({
        items: [sampleApp],
        totalCount: 1,
        limit: 25,
        offset: 0,
        postingMap: { [samplePosting.id]: samplePosting },
        companyMap: { [sampleCompany.id]: sampleCompany },
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  const ApplicationDetailStub = createRoutesStub([
    {
      path: "/applications/:id",
      Component: ApplicationDetail,
      loader: () => ({
        application: sampleApp,
        posting: samplePosting,
        company: sampleCompany,
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  const HostileApplicationDetailStub = createRoutesStub([
    {
      path: "/applications/:id",
      Component: ApplicationDetail,
      loader: () => ({
        application: hostileApp,
        posting: hostilePosting,
        company: hostileCompany,
        csrfToken: "mock-csrf",
        userLabel: "Signed in",
      }),
    },
  ]);

  it("has zero serious or critical accessibility violations on /applications in light and dark themes", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(ApplicationsListStub, {
          key: "list-light",
          initialEntries: ["/applications"],
        }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(ApplicationsListStub, {
          key: "list-dark",
          initialEntries: ["/applications"],
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("has zero serious or critical accessibility violations on /applications/:id timeline detail in light and dark themes", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(ApplicationDetailStub, {
          key: "detail-light",
          initialEntries: ["/applications/app-sample-1"],
        }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(ApplicationDetailStub, {
          key: "detail-dark",
          initialEntries: ["/applications/app-sample-1"],
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("renders hostile HTML/scripts in applications, postings, and events as plain inert text without DOM element execution", async () => {
    const { container, findAllByText } = render(
      React.createElement(HostileApplicationDetailStub, {
        initialEntries: ["/applications/app-hostile-1"],
      })
    );

    const matching = await findAllByText(/event-notes-xss/);
    expect(matching.length).toBeGreaterThan(0);

    const scripts = container.querySelectorAll("script");
    const iframes = container.querySelectorAll("iframe");
    const errorImgs = container.querySelectorAll("img[onerror]");

    expect(scripts.length).toBe(0);
    expect(iframes.length).toBe(0);
    expect(errorImgs.length).toBe(0);

    expect(container.textContent).toContain("<script>alert('posting-title-xss')</script>");
    expect(container.textContent).toContain("<script>alert('event-notes-xss')</script>");
  });
});
