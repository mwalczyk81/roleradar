import { describe, it, expect } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import PostingDetail from "../../app/routes/postings.$id.js";

describe("Posting detail untrusted text escaping security (T030)", () => {
  const hostilePosting = {
    id: "post-hostile-1",
    companyId: "comp-1",
    companyName: "<script>alert('company-xss')</script>",
    title: "<script>alert('title-xss')</script>",
    requisitionId: "<img src=x onerror=alert('req-xss')>",
    location: "<iframe src='evil.com'></iframe>",
    disposition: "lead",
    resurfacePolicy: "never",
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
        detectedAt: "2026-10-31T10:00:00Z",
      },
    ],
    assessmentNote: {
      id: "note-1",
      summary: "<b>Strong Match</b> <script>stealCookies()</script>",
      updatedAt: "2026-10-31T10:00:00Z",
    },
  };

  const PostingDetailStub = createRoutesStub([
    {
      path: "/postings/:id",
      Component: PostingDetail,
      loader: () => ({
        posting: hostilePosting,
        criteria: [],
        csrfToken: "mock-csrf-token",
        userLabel: "local-owner",
      }),
    },
  ]);

  it("renders hostile tags in title, location, compensation, assessmentNote, and signals as plain text without DOM element execution", async () => {
    const { container, findByText } = render(
      React.createElement(PostingDetailStub, {
        initialEntries: ["/postings/post-hostile-1"],
      })
    );

    // Wait for route to load via findByText
    await findByText(/company-xss/);

    // Zero script, img with onerror, or iframe tags created in the DOM
    const scripts = container.querySelectorAll("script");
    const iframes = container.querySelectorAll("iframe");
    const errorImgs = container.querySelectorAll("img[onerror]");

    expect(scripts.length).toBe(0);
    expect(iframes.length).toBe(0);
    expect(errorImgs.length).toBe(0);

    // Verify raw strings render as safe plain text in DOM
    expect(container.textContent).toContain("<script>alert('title-xss')</script>");
    expect(container.textContent).toContain("<iframe src='evil.com'></iframe>");
    expect(container.textContent).toContain("<b onmouseover='alert(1)'>$200,000 - $240,000</b>");
    expect(container.textContent).toContain("<b>Strong Match</b> <script>stealCookies()</script>");
  });
});
