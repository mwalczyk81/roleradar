import { describe, it, expect, beforeEach, vi } from "vitest";
import React from "react";
import { render, fireEvent } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import crypto from "node:crypto";
import * as client from "openid-client";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  type UserSession,
} from "../../app/lib/session.server.js";

import CriteriaPage from "../../app/routes/criteria.js";
import NotesPage from "../../app/routes/notes._index.js";
import NoteDetailPage, { loader as noteDetailLoader } from "../../app/routes/notes.$id.js";
import SearchRunsPage from "../../app/routes/search-runs.js";
import { callLoader } from "../test-helpers.js";

describe("Operational Views (T054: Criteria, Notes, Search Runs)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiGet = vi.fn();
  const mockOidcConfig = new client.Configuration(
    {
      issuer: "https://identity:5001",
      authorization_endpoint: "https://identity:5001/connect/authorize",
      token_endpoint: "https://identity:5001/connect/token",
      end_session_endpoint: "https://identity:5001/connect/endsession",
    },
    "roleradar-web",
    "test-secret"
  );

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    setOidcConfigForTesting(mockOidcConfig);
    vi.restoreAllMocks();
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: mockApiGet,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function createTestSession(): { sessionId: string; csrfToken: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const csrfToken = "test-csrf-token-ops";
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-sub",
      ownerUsername: "operational-user",
      accessToken: "mock-token-ops",
      refreshToken: "mock-refresh-token",
      idToken: "mock-id-token",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken,
    };
    saveSession(session);
    return { sessionId, csrfToken, session };
  }

  describe("Criteria (/criteria)", () => {
    it("renders exact CriterionDto fields and contains zero mutation controls", async () => {
      const sampleCriteria = [
        {
          id: "crit-1",
          name: "Remote Only",
          category: "hard_skip",
          ruleText: "Must be 100% remote within US.",
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: null,
          isActive: true,
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/criteria",
          Component: CriteriaPage,
          loader: () => ({
            items: sampleCriteria,
            totalCount: sampleCriteria.length,
            limit: 25,
            offset: 0,
            sort: "name",
            direction: "asc",
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { container, findByText } = render(
        React.createElement(Stub, { initialEntries: ["/criteria"] })
      );

      expect(await findByText("Remote Only")).toBeDefined();
      expect(await findByText("Must be 100% remote within US.")).toBeDefined();

      // Zero mutation forms outside signOut
      const forms = Array.from(container.querySelectorAll("form"));
      const mutationForms = forms.filter((f) => f.getAttribute("action") !== "/auth/logout");
      expect(mutationForms.length).toBe(0);

      // Zero create/edit/delete/save buttons
      const buttons = Array.from(container.querySelectorAll("button"));
      const mutationButtons = buttons.filter((b) => {
        const text = (b.textContent || "").toLowerCase();
        return (
          text.includes("create") ||
          text.includes("edit") ||
          text.includes("delete") ||
          text.includes("save") ||
          text.includes("add")
        );
      });
      expect(mutationButtons.length).toBe(0);
    });

    it("renders inactive criterion with custom category and effectiveTo date", async () => {
      const sampleCriteria = [
        {
          id: "crit-2",
          name: "Security Clearance",
          category: "custom_weight",
          ruleText: "Must hold active secret clearance.",
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: "2026-12-31T00:00:00Z",
          isActive: false,
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/criteria",
          Component: CriteriaPage,
          loader: () => ({
            items: sampleCriteria,
            totalCount: sampleCriteria.length,
            limit: 25,
            offset: 0,
            sort: "name",
            direction: "asc",
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { findByText } = render(
        React.createElement(Stub, { initialEntries: ["/criteria"] })
      );

      expect(await findByText("Security Clearance")).toBeDefined();
      expect(await findByText("Inactive")).toBeDefined();
      expect(await findByText("custom_weight")).toBeDefined();
    });
  });

  describe("Notes List (/notes)", () => {
    it("renders the area as plain text, never as a link, and shows no note body", async () => {
      const sampleNotes = [
        {
          id: "note-p",
          heading: "Note With Hostile Area",
          tags: ["posting-tag"],
          linkedEntityType: "javascript:alert(1)",
          linkedEntityId: null,
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-16T12:00:00Z",
        },
        {
          id: "note-b",
          heading: "Note Without Area",
          tags: [],
          linkedEntityType: "",
          linkedEntityId: null,
          createdAt: "2026-10-14T12:00:00Z",
          updatedAt: "2026-10-14T12:00:00Z",
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/notes",
          Component: NotesPage,
          loader: () => ({
            items: sampleNotes,
            totalCount: 2,
            limit: 25,
            offset: 0,
            sort: "updatedAt",
            direction: "desc",
            q: "",
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
        {
          path: "/notes/:id",
          Component: () => React.createElement("div", null, "Note Detail Stub"),
        },
      ]);

      const { container, findByText } = render(
        React.createElement(Stub, { initialEntries: ["/notes"] })
      );

      expect(await findByText("Note With Hostile Area")).toBeDefined();
      expect(await findByText("javascript:alert(1)")).toBeDefined();
      expect(container.querySelector('a[href^="javascript"]')).toBeNull();
      expect(container.querySelector('a[href*="alert"]')).toBeNull();
      expect(container.textContent).not.toContain("Preview");

      // Zero mutation controls (no POST forms outside signOut)
      const forms = Array.from(container.querySelectorAll("form")).filter(
        (f) => f.getAttribute("method")?.toLowerCase() === "post" && f.getAttribute("action") !== "/auth/logout"
      );
      expect(forms.length).toBe(0);

      // Row click navigates to note detail
      fireEvent.click(await findByText("Note With Hostile Area"));
      expect(await findByText("Note Detail Stub")).toBeDefined();
    });

    it("handles pagination in NotesPage", async () => {
      const Stub = createRoutesStub([
        {
          path: "/notes",
          Component: NotesPage,
          loader: ({ request }) => {
            const url = new URL(request.url);
            const offset = parseInt(url.searchParams.get("offset") || "0", 10);
            return {
              items: [
                {
                  id: "note-1",
                  heading: "Note Page 1",
                  linkedEntityType: "posting",
                  linkedEntityId: "p-1",
                  createdAt: "2026-10-15T12:00:00Z",
                  updatedAt: "2026-10-15T12:00:00Z",
                },
              ],
              totalCount: 50,
              limit: 25,
              offset,
              q: "",
              csrfToken: "mock-csrf",
              userLabel: "operational-user",
            };
          },
        },
      ]);

      const { findByText, findByRole } = render(
        React.createElement(Stub, { initialEntries: ["/notes"] })
      );

      expect(await findByText(/Showing 1–25 of 50/)).toBeDefined();
      const nextBtn = await findByRole("button", { name: "Next" });
      fireEvent.click(nextBtn);
      expect(await findByText(/Showing 26–50 of 50/)).toBeDefined();
      expect((nextBtn as HTMLButtonElement).disabled).toBe(true);
    });
  });

  describe("Note Detail (/notes/:id)", () => {
    it("loader rejects missing id parameter with 400", async () => {
      const { sessionId } = createTestSession();
      const request = new Request("https://localhost:5003/notes/", {
        headers: { Cookie: `__Host-roleradar_session=${sessionId}` },
      });

      let thrown: Response | null = null;
      try {
        await callLoader(noteDetailLoader, request);
      } catch (err) {
        if (err instanceof Response) thrown = err;
      }
      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(400);
    });

    it("loader calls GET /api/v1/notes/{id} and returns 404 when missing", async () => {
      const { sessionId } = createTestSession();
      mockApiGet.mockResolvedValueOnce({
        data: undefined,
        error: { message: "Note not found" },
        response: { status: 404 } as Response,
      });

      const request = new Request("https://localhost:5003/notes/note-missing", {
        headers: { Cookie: `__Host-roleradar_session=${sessionId}` },
      });

      await expect(callLoader(noteDetailLoader, request, { id: "note-missing" })).rejects.toThrow();
    });

    it("renders hostile HTML/scripts and javascript URLs safely as plain text preserving whitespace", async () => {
      const hostileNote = {
        id: "note-hostile",
        heading: "<script>alert('heading-xss')</script>Hostile Heading",
        content: "Line 1\n<script>alert('body-xss')</script>\n<a href='javascript:alert(1)'>Click</a>\nLine 2",
        tags: ["<b onmouseover='alert(2)'>tag</b>", "security"],
        linkedEntityType: "posting",
        linkedEntityId: "post-100",
        createdAt: "2026-10-15T12:00:00Z",
        updatedAt: "2026-10-16T12:00:00Z",
      };

      const Stub = createRoutesStub([
        {
          path: "/notes/:id",
          Component: NoteDetailPage,
          loader: () => ({
            note: hostileNote,
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { container, findAllByText } = render(
        React.createElement(Stub, { initialEntries: ["/notes/note-hostile"] })
      );

      const matchingHeadings = await findAllByText(/Hostile Heading/);
      expect(matchingHeadings.length).toBeGreaterThan(0);

      const scripts = container.querySelectorAll("script");
      const iframes = container.querySelectorAll("iframe");
      const javascriptLinks = container.querySelectorAll('a[href^="javascript:"]');

      expect(scripts.length).toBe(0);
      expect(iframes.length).toBe(0);
      expect(javascriptLinks.length).toBe(0);

      expect(container.textContent).toContain("<script>alert('body-xss')</script>");
      expect(container.textContent).toContain("<script>alert('heading-xss')</script>");

      // Preserves whitespace via pre-wrap
      const contentEl = container.querySelector('[data-testid="note-content"]');
      expect(contentEl).not.toBeNull();
      expect(contentEl?.getAttribute("style") ?? "").toContain("pre-wrap");

      // Zero mutation controls (no POST forms outside signOut)
      const forms = Array.from(container.querySelectorAll("form")).filter(
        (f) => f.getAttribute("method")?.toLowerCase() === "post" && f.getAttribute("action") !== "/auth/logout"
      );
      expect(forms.length).toBe(0);
    });
  });

  describe("Search Runs (/search-runs)", () => {
    it("renders the stored search run fields and zero mutation controls", async () => {
      const sampleRuns = [
        {
          id: "run-1",
          runDate: "2026-10-31T08:00:00Z",
          newPostings: 3,
          summary: "Automated batch search run completed with 3 new postings.",
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/search-runs",
          Component: SearchRunsPage,
          loader: () => ({
            items: sampleRuns,
            totalCount: 1,
            limit: 25,
            offset: 0,
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { container, findByText } = render(
        React.createElement(Stub, { initialEntries: ["/search-runs"] })
      );

      expect(await findByText("Automated batch search run completed with 3 new postings.")).toBeDefined();

      // Zero mutation controls (no POST forms outside signOut)
      const forms = Array.from(container.querySelectorAll("form")).filter(
        (f) => f.getAttribute("method")?.toLowerCase() === "post" && f.getAttribute("action") !== "/auth/logout"
      );
      expect(forms.length).toBe(0);
    });

    it("renders search runs with no new postings and an empty summary", async () => {
      const sampleRuns = [
        {
          id: "run-2",
          runDate: "2026-10-30T08:00:00Z",
          newPostings: 0,
          summary: null,
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/search-runs",
          Component: SearchRunsPage,
          loader: () => ({
            items: sampleRuns,
            totalCount: 50,
            limit: 25,
            offset: 0,
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { findByText } = render(
        React.createElement(Stub, { initialEntries: ["/search-runs"] })
      );

      expect(await findByText("0 new")).toBeDefined();
      expect(await findByText("—")).toBeDefined();
    });
  });
});
