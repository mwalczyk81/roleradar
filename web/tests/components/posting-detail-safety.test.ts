import { describe, it, expect } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import PostingDetail from "../../app/routes/postings.$id.js";

vi.mock("react-router", async () => {
  const actual = await vi.importActual("react-router");
  return {
    ...actual,
    useLoaderData: () => ({
      posting: {
        id: "post-hostile-1",
        companyId: "comp-1",
        companyName: "<script>alert('company-xss')</script>",
        title: "<script>alert('title-xss')</script>",
        requisitionId: "<img src=x onerror=alert('req-xss')>",
        location: "<iframe src='evil.com'></iframe>",
        disposition: "lead",
        resurfacePolicy: "manual_only",
        score: 95,
        versionToken: "token-1",
        createdAt: "2026-10-31T10:00:00Z",
        updatedAt: "2026-10-31T10:00:00Z",
        url: "https://example.com",
        compensation: {
          currency: "USD",
          rawText: "<b onmouseover='alert(1)'>$200,000 - $240,000</b>",
        },
        signals: [
          {
            signalType: "tech_stack",
            evidence: "<svg onload=alert('svg')>",
          },
        ],
        assessmentNote: {
          id: "note-1",
          authorName: "Matcher",
          summary: "<b>Strong Match</b> <script>stealCookies()</script>",
          createdAt: "2026-10-31T10:00:00Z",
        },
      },
      criteria: [],
      csrfToken: "mock-csrf-token",
      userLabel: "local-owner",
    }),
  };
});

describe("Posting detail untrusted text escaping security (T030)", () => {
  it("renders hostile tags in title, location, compensation, assessmentNote, and signals as plain text without DOM element execution", () => {
    const { container } = render(
      React.createElement(
        MemoryRouter,
        null,
        React.createElement(PostingDetail)
      )
    );

    // Zero script, img with onerror, or iframe tags created in the DOM
    expect(container.querySelectorAll("script")).toHaveLength(0);
    expect(container.querySelectorAll("iframe")).toHaveLength(0);
    expect(container.querySelectorAll("svg[onload]")).toHaveLength(0);
    expect(container.querySelectorAll("img[onerror]")).toHaveLength(0);

    // Hostile string literals must be preserved as literal textContent
    expect(container.textContent).toContain("<script>alert('title-xss')</script>");
    expect(container.textContent).toContain("<iframe src='evil.com'></iframe>");
    expect(container.textContent).toContain("<b onmouseover='alert(1)'>$200,000 - $240,000</b>");
    expect(container.textContent).toContain("<svg onload=alert('svg')>");
    expect(container.textContent).toContain("<script>stealCookies()</script>");
  });
});
