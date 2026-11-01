import { describe, it, expect, beforeEach, vi } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
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

import CriteriaPage, { loader as criteriaLoader } from "../../app/routes/criteria.js";
import NotesPage, { loader as notesLoader } from "../../app/routes/notes._index.js";
import NoteDetailPage, { loader as noteDetailLoader } from "../../app/routes/notes.$id.js";
import SearchRunsPage, { loader as searchRunsLoader } from "../../app/routes/search-runs.js";

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
    } as any);
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
    it("loader calls GET /api/v1/criteria with only effectiveDate parameter", async () => {
      const { sessionId } = createTestSession();
      mockApiGet.mockResolvedValueOnce({
        data: [
          {
            id: "crit-1",
            name: "Remote Only",
            weight: "hard_skip",
            ruleText: "Must be 100% remote within US.",
            effectiveFrom: "2026-01-01T00:00:00Z",
            effectiveTo: null,
            isActive: true,
          },
        ],
        error: undefined,
        response: { status: 200 } as Response,
      });

      const request = new Request("https://localhost:5003/criteria?effectiveDate=2026-06-01&badParam=hack", {
        headers: { Cookie: `__Host-roleradar_session=${sessionId}` },
      });

      const result = await criteriaLoader({ request, params: {}, context: {} } as any);
      expect(result.items.length).toBe(1);
      expect(result.items[0].name).toBe("Remote Only");
      expect(mockApiGet).toHaveBeenCalledWith("/api/v1/criteria", {
        params: {
          query: {
            effectiveDate: "2026-06-01",
          },
        },
      });
    });

    it("renders exact CriterionDto fields and contains zero mutation controls", async () => {
      const sampleCriteria = [
        {
          id: "crit-1",
          name: "Remote Only",
          weight: "hard_skip",
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
  });

  describe("Notes List (/notes)", () => {
    it("loader sends only q, limit, offset and clamps limit 1..100 and offset >= 0", async () => {
      const { sessionId } = createTestSession();
      mockApiGet.mockResolvedValueOnce({
        data: {
          items: [
            {
              id: "note-1",
              heading: "Systems Architecture Notes",
              content: "First round architecture review notes.",
              tags: ["architecture", "review"],
              linkedEntityType: "posting",
              linkedEntityId: "post-100",
              createdAt: "2026-10-15T12:00:00Z",
              updatedAt: "2026-10-16T12:00:00Z",
            },
          ],
          totalCount: 1,
          limit: 100,
          offset: 0,
        },
        error: undefined,
        response: { status: 200 } as Response,
      });

      const request = new Request("https://localhost:5003/notes?q=architecture&limit=999&offset=-5&sort=desc", {
        headers: { Cookie: `__Host-roleradar_session=${sessionId}` },
      });

      const result = await notesLoader({ request, params: {}, context: {} } as any);
      expect(result.items.length).toBe(1);
      expect(result.limit).toBe(100);
      expect(result.offset).toBe(0);
      expect(mockApiGet).toHaveBeenCalledWith("/api/v1/notes", {
        params: {
          query: {
            q: "architecture",
            limit: 100,
            offset: 0,
          },
        },
      });
    });

    it("renders linked entity as route link when type is posting, company or application; plain text otherwise", async () => {
      const sampleNotes = [
        {
          id: "note-p",
          heading: "Note Linking to Posting",
          content: "Discussed posting details.",
          tags: ["posting-tag"],
          linkedEntityType: "posting",
          linkedEntityId: "post-100",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-15T12:00:00Z",
        },
        {
          id: "note-c",
          heading: "Note Linking to Company",
          content: "Company research details.",
          tags: ["company-tag"],
          linkedEntityType: "company",
          linkedEntityId: "comp-200",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-15T12:00:00Z",
        },
        {
          id: "note-a",
          heading: "Note Linking to Application",
          content: "Application interview notes.",
          tags: ["app-tag"],
          linkedEntityType: "application",
          linkedEntityId: "app-300",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-15T12:00:00Z",
        },
        {
          id: "note-unknown",
          heading: "Note Linking to Contact (No Route)",
          content: "Met recruiter at conference.",
          tags: ["contact-tag"],
          linkedEntityType: "contact",
          linkedEntityId: "contact-400",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-15T12:00:00Z",
        },
      ];

      const Stub = createRoutesStub([
        {
          path: "/notes",
          Component: NotesPage,
          loader: () => ({
            items: sampleNotes,
            totalCount: 4,
            limit: 25,
            offset: 0,
            q: "",
            csrfToken: "mock-csrf",
            userLabel: "operational-user",
          }),
        },
      ]);

      const { container, findByText } = render(
        React.createElement(Stub, { initialEntries: ["/notes"] })
      );

      expect(await findByText("Note Linking to Posting")).toBeDefined();

      const postingLink = container.querySelector('a[href="/postings/post-100"]');
      expect(postingLink).not.toBeNull();

      const companyLink = container.querySelector('a[href="/companies/comp-200"]');
      expect(companyLink).not.toBeNull();

      const appLink = container.querySelector('a[href="/applications/app-300"]');
      expect(appLink).not.toBeNull();

      // Contact has NO route: no link to /contact or /contacts
      const contactLink = container.querySelector('a[href*="contact-400"]');
      expect(contactLink).toBeNull();

      // Zero mutation controls (no POST forms outside signOut)
      const forms = Array.from(container.querySelectorAll("form")).filter(
        (f) => f.getAttribute("method")?.toLowerCase() === "post" && f.getAttribute("action") !== "/auth/logout"
      );
      expect(forms.length).toBe(0);
    });
  });

  describe("Note Detail (/notes/:id)", () => {
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

      await expect(noteDetailLoader({ request, params: { id: "note-missing" }, context: {} } as any)).rejects.toThrow();
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
    it("loader sends only limit and offset and does not support sort", async () => {
      const { sessionId } = createTestSession();
      mockApiGet.mockResolvedValueOnce({
        data: {
          items: [
            {
              id: "run-1",
              runDate: "2026-10-31T08:00:00Z",
              companiesSearched: 25,
              postingsFound: 14,
              newPostings: 3,
              errorsCount: 0,
              summary: "Automated batch search run completed with 3 new postings.",
            },
          ],
          totalCount: 1,
          limit: 25,
          offset: 0,
        },
        error: undefined,
        response: { status: 200 } as Response,
      });

      const request = new Request("https://localhost:5003/search-runs?limit=25&offset=0&sort=runDate:asc", {
        headers: { Cookie: `__Host-roleradar_session=${sessionId}` },
      });

      const result = await searchRunsLoader({ request, params: {}, context: {} } as any);
      expect(result.items.length).toBe(1);
      expect(mockApiGet).toHaveBeenCalledWith("/api/v1/search-runs", {
        params: {
          query: {
            limit: 25,
            offset: 0,
          },
        },
      });
    });

    it("renders exact SearchRunDto fields and zero mutation controls", async () => {
      const sampleRuns = [
        {
          id: "run-1",
          runDate: "2026-10-31T08:00:00Z",
          companiesSearched: 25,
          postingsFound: 14,
          newPostings: 3,
          errorsCount: 0,
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
  });
});
