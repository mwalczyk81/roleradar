import { describe, it, expect, beforeEach, vi } from "vitest";
import React from "react";
import crypto from "node:crypto";
import * as client from "openid-client";
import { render, fireEvent, waitFor } from "@testing-library/react";
import { createRoutesStub, data } from "react-router";
import PostingDetail, { loader as postingDetailLoader } from "../../app/routes/postings.$id.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

describe("GET /postings/:id (T031)", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockApiGet = vi.fn();
  const mockFetchOData = vi.fn();
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
    mockFetchOData.mockReset();
    mockFetchOData.mockResolvedValue({
      data: { value: [], "@odata.count": 0 },
      error: undefined,
      response: { status: 200 } as Response,
    });
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
    vi.spyOn(apiClientModule, "createApiClient").mockReturnValue({
      GET: mockApiGet,
    } as unknown as ReturnType<typeof apiClientModule.createApiClient>);
  });

  function createTestSession(): { sessionId: string; session: UserSession } {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-matt-001",
      accessToken: "mock-access-token-111",
      refreshToken: "mock-refresh-token-222",
      idToken: "mock-id-token-333",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-444",
    };
    saveSession(session);
    return { sessionId, session };
  }

  const samplePosting = {
    id: "post-1",
    companyId: "comp-1",
    companyName: "Acme Innovations",
    title: "Lead Platform Engineer",
    requisitionId: "REQ-7788",
    location: "San Francisco, CA / Remote",
    disposition: "lead",
    resurfacePolicy: "never",
    score: 91.5,
    versionToken: "v-token-1",
    createdAt: "2026-10-20T12:00:00Z",
    updatedAt: "2026-10-25T14:30:00Z",
    url: "https://acme.example.com/careers/lead-platform",
    compensation: {
      currency: "USD",
      rawText: "$190,000 - $230,000 base + equity",
      hasSalary: true,
      compHighPriority: false,
    },
    signals: [
      {
        signalType: "tech_stack",
        evidence: "TypeScript, .NET 10, Docker, Linux",
        detectedAt: "2026-10-20T12:00:00Z",
      },
    ],
    assessmentNote: {
      id: "note-1",
      summary: "Excellent tech stack alignment and compensation match.",
      updatedAt: "2026-10-20T12:05:00Z",
    },
  };

  const sampleCriteria = [
    {
      id: "crit-1",
      name: "Remote Work Preference",
      weight: "mandatory",
      ruleText: "Must allow 100% remote within US.",
      effectiveFrom: "2026-01-01T00:00:00Z",
      effectiveTo: null,
      isActive: true,
    },
  ];

  it("fetches posting detail and criteria, mapping all PostingDetailDto fields and never leaking tokens", async () => {
    const { sessionId } = createTestSession();
    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: samplePosting,
          error: undefined,
          response: { status: 200 } as Response,
        });
      }
      return Promise.resolve({ data: undefined, response: { status: 404 } as Response });
    });
    mockFetchOData.mockResolvedValue({
      data: { value: sampleCriteria.map(({ id, name }) => ({ id, name })), "@odata.count": sampleCriteria.length },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const request = new Request("https://localhost:5003/postings/post-1", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    const result = await callLoader(postingDetailLoader, request, { id: "post-1" });

    expect(result.posting).toEqual(samplePosting);
    expect(result.criteria).toEqual(sampleCriteria.map(({ id, name }) => ({ id, name })));

    // The criteria list comes from the OData Criteria set (id and name only), never the retired list endpoint.
    expect(mockApiGet).not.toHaveBeenCalledWith("/api/v1/criteria", expect.anything());
    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [path, query] = mockFetchOData.mock.calls[0] as [string, string, string];
    expect(path).toBe("/odata/Criteria");
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain("$select=id,name");
    expect(decoded).toContain("$orderby=name asc");
    expect(decoded).toContain("$top=100");
    expect(mockFetchOData.mock.calls[0][2]).toBe("mock-access-token-111");
    expect(result.csrfToken).toBe("mock-csrf-token-444");
    expect(result.userLabel).toBe("Signed in");

    // Invariant: Never return tokens to browser
    const serialized = JSON.stringify(result);
    expect(serialized).not.toContain("mock-access-token-111");
    expect(serialized).not.toContain("mock-refresh-token-222");
    expect(serialized).not.toContain("mock-id-token-333");
  });

  it("handles 404 Not Found by throwing a 404 Response", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: undefined,
          error: { ok: false, errorCode: "NOT_FOUND", message: "Not found" },
          response: { status: 404 } as Response,
        });
      }
      return Promise.resolve({ data: [], response: { status: 200 } as Response });
    });

    const request = new Request("https://localhost:5003/postings/post-99", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(postingDetailLoader, request, { id: "post-99" });
    } catch (err: unknown) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(404);
  });

  it("handles 401 Unauthorized from API by destroying session and redirecting to login with returnTo", async () => {
    const { sessionId } = createTestSession();

    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === "/api/v1/postings/{id}") {
        return Promise.resolve({
          data: undefined,
          error: { ok: false, errorCode: "UNAUTHORIZED", message: "Token expired" },
          response: { status: 401 } as Response,
        });
      }
      return Promise.resolve({ data: [], response: { status: 200 } as Response });
    });

    const request = new Request("https://localhost:5003/postings/post-1", {
      headers: {
        Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
      },
    });

    let thrown: Response | null = null;
    try {
      await callLoader(postingDetailLoader, request, { id: "post-1" });
    } catch (err: unknown) {
      if (err instanceof Response) thrown = err;
    }

    expect(thrown).not.toBeNull();
    expect(thrown?.status).toBe(302);
    const location = thrown?.headers.get("Location");
    expect(location).toContain("/auth");
    expect(thrown?.headers.get("Set-Cookie")).toBeDefined();
  });

  it("renders PostingDetail component with action buttons and opens dialogs", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings/:id",
        Component: PostingDetail,
        loader: () => ({
          posting: samplePosting,
          criteria: sampleCriteria,
          csrfToken: "mock-csrf-token-444",
          userLabel: "matt@example.com",
        }),
      },
    ]);

    const { findByText, findAllByText, findByRole, queryByText } = render(
      React.createElement(AppStub, { initialEntries: ["/postings/post-1"] })
    );

    expect((await findAllByText("Lead Platform Engineer")).length).toBeGreaterThan(0);
    expect(await findByText("Acme Innovations")).toBeDefined();
    expect(await findByText("• San Francisco, CA / Remote")).toBeDefined();
    expect(await findByText("$190,000 - $230,000 base + equity")).toBeDefined();
    expect(await findByText("— TypeScript, .NET 10, Docker, Linux")).toBeDefined();

    // Open change disposition dialog
    const changeDispBtn = await findByRole("button", { name: "Change Disposition" });
    fireEvent.click(changeDispBtn);
    expect(await findByText("Target Disposition *")).toBeDefined();

    // Close change disposition dialog
    const closeBtn = await findByRole("button", { name: "Cancel" });
    fireEvent.click(closeBtn);
    expect(queryByText("Target Disposition *")).toBeNull();

    // Open create application dialog
    const createBtn = await findByRole("button", { name: "Create Application" });
    fireEvent.click(createBtn);
    expect(await findByText("Create Job Application")).toBeDefined();

    // Close create application dialog
    const cancelAppBtn = await findByRole("button", { name: "Cancel" });
    fireEvent.click(cancelAppBtn);
    expect(queryByText("Create Job Application")).toBeNull();
  });

  it("displays 409 Concurrency Conflict toast and revalidates on Reload click", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/postings/:id",
        Component: PostingDetail,
        loader: () => ({
          posting: samplePosting,
          criteria: sampleCriteria,
          csrfToken: "mock-csrf-token-444",
          userLabel: "matt@example.com",
        }),
        action: async () => {
          return {
            ok: false,
            isConflict: true,
            errorCode: "CONCURRENCY_CONFLICT",
            message: "The posting was modified by another process. Please reload.",
          };
        },
      },
    ]);

    const { findByText, findByRole, queryByText } = render(
      React.createElement(AppStub, { initialEntries: ["/postings/post-1"] })
    );

    // Open change disposition dialog
    const changeDispBtn = await findByRole("button", { name: "Change Disposition" });
    fireEvent.click(changeDispBtn);
    expect(await findByRole("button", { name: "Apply Transition" })).toBeDefined();

    // Fill reason and submit
    const reasonInput = await findByRole("textbox", { name: "Reason *" });
    fireEvent.change(reasonInput, { target: { value: "Trigger conflict test" } });

    const submitBtn = await findByRole("button", { name: "Apply Transition" });
    fireEvent.click(submitBtn);

    // Assert conflict toast appears
    expect(await findByText("Concurrency Conflict")).toBeDefined();
    const reloadBtn = await findByRole("button", { name: "Reload" });
    expect(reloadBtn).toBeDefined();
    fireEvent.click(reloadBtn);
    expect(queryByText("Concurrency Conflict")).toBeNull();
  });

  // ---- 008 phase 4: application notice, event date, warnings (T047, T048, T050) ----

  const APPLIED_NOTICE = "This also records the rejection on the application.";

  function stubFor(
    posting: Record<string, unknown>,
    action?: (args: { request: Request }) => unknown
  ) {
    return createRoutesStub([
      {
        path: "/postings/:id",
        Component: PostingDetail,
        loader: () => ({
          posting,
          criteria: sampleCriteria,
          csrfToken: "mock-csrf-token-444",
          userLabel: "matt@example.com",
        }),
        ...(action ? { action: action as never } : {}),
      },
    ]);
  }

  async function openDialog(view: ReturnType<typeof render>) {
    fireEvent.click(await view.findByRole("button", { name: "Change Disposition" }));
    return view.findByLabelText("Target Disposition *");
  }

  describe("application notice (FR-015)", () => {
    const applied = { ...samplePosting, disposition: "applied", applicationId: "app-1" };

    it("states the rejection is also recorded on the application: applied posting WITH an application, target rejected", async () => {
      const Stub = stubFor(applied);
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "rejected" } });
      expect(await view.findByText(APPLIED_NOTICE)).toBeDefined();
    });

    it("does not state it when the posting has no application", async () => {
      const Stub = stubFor({ ...applied, applicationId: null });
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "rejected" } });
      expect(view.queryByText(APPLIED_NOTICE)).toBeNull();
      expect(view.queryByLabelText("Event Date (Optional)")).toBeNull();
    });

    it("does not state it when the target is not rejected", async () => {
      const Stub = stubFor(applied);
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "closed" } });
      expect(view.queryByText(APPLIED_NOTICE)).toBeNull();
      expect(view.queryByLabelText("Event Date (Optional)")).toBeNull();
    });

    it("does not state it for a disposition other than applied, even with an application id", async () => {
      const Stub = stubFor({ ...samplePosting, disposition: "scored", applicationId: "app-1" });
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "closed" } });
      expect(view.queryByText(APPLIED_NOTICE)).toBeNull();
    });
  });

  describe("event date field", () => {
    const applied = { ...samplePosting, disposition: "applied", applicationId: "app-1" };

    function okPosting(extra: Record<string, unknown> = {}) {
      return { ...applied, disposition: "rejected", versionToken: "v-token-2", ...extra };
    }

    it("defaults to today's UTC date and posts as eventDate", async () => {
      vi.useFakeTimers({ toFake: ["Date"], now: new Date("2026-11-05T23:59:00Z") });
      try {
        let captured: FormData | null = null;
        const Stub = stubFor(applied, async ({ request }) => {
          captured = await request.formData();
          return { ok: true, posting: okPosting() };
        });
        const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
        const target = await openDialog(view);
        fireEvent.change(target, { target: { value: "rejected" } });

        const dateInput = (await view.findByLabelText("Event Date (Optional)")) as HTMLInputElement;
        expect(dateInput.value).toBe("2026-11-05");

        fireEvent.change(await view.findByRole("textbox", { name: "Reason *" }), { target: { value: "Rejected by email" } });
        fireEvent.click(await view.findByRole("button", { name: "Apply Transition" }));

        await waitFor(() => expect(captured).not.toBeNull());
        expect((captured as unknown as FormData).get("eventDate")).toBe("2026-11-05");
      } finally {
        vi.useRealTimers();
      }
    });

    it("posts the date the owner typed", async () => {
      let captured: FormData | null = null;
      const Stub = stubFor(applied, async ({ request }) => {
        captured = await request.formData();
        return { ok: true, posting: okPosting() };
      });
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "rejected" } });
      fireEvent.change(await view.findByLabelText("Event Date (Optional)"), { target: { value: "2026-09-15" } });
      fireEvent.change(await view.findByRole("textbox", { name: "Reason *" }), { target: { value: "Rejected by email" } });
      fireEvent.click(await view.findByRole("button", { name: "Apply Transition" }));

      await waitFor(() => expect(captured).not.toBeNull());
      expect((captured as unknown as FormData).get("eventDate")).toBe("2026-09-15");
    });

    it("shows a 400 eventDate field error inline on the date field and keeps the dialog open", async () => {
      const Stub = stubFor(applied, async () =>
        data(
          {
            ok: false,
            errorCode: "validation_error",
            message: "Validation failed.",
            fieldErrors: { eventDate: "Event date must be a valid date in yyyy-MM-dd format." },
          },
          { status: 400 }
        )
      );
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "rejected" } });
      fireEvent.change(await view.findByLabelText("Event Date (Optional)"), { target: { value: "09/15/2026" } });
      fireEvent.change(await view.findByRole("textbox", { name: "Reason *" }), { target: { value: "Rejected by email" } });
      fireEvent.click(await view.findByRole("button", { name: "Apply Transition" }));

      expect(await view.findByText("Event date must be a valid date in yyyy-MM-dd format.")).toBeDefined();
      expect(view.getByLabelText("Event Date (Optional)").getAttribute("aria-invalid")).toBe("true");
      expect(view.getByRole("button", { name: "Apply Transition" })).toBeDefined();
    });
  });

  describe("returned warnings (FR-011a)", () => {
    const rejectedPosting = { ...samplePosting, disposition: "rejected", applicationId: "app-1" };

    async function submitReopen(view: ReturnType<typeof render>) {
      const target = await openDialog(view);
      fireEvent.change(target, { target: { value: "lead" } });
      fireEvent.change(await view.findByRole("textbox", { name: "Reason *" }), { target: { value: "Recruiter reopened" } });
      fireEvent.click(await view.findByRole("button", { name: "Apply Transition" }));
    }

    it("shows each returned message on the page after the dialog closes, whatever the code is", async () => {
      const Stub = stubFor(rejectedPosting, async () => ({
        ok: true,
        posting: {
          ...rejectedPosting,
          disposition: "lead",
          versionToken: "v-token-2",
          warnings: [
            { code: "zz_code_the_page_has_never_seen", message: "Fictional notice: the application stays as it was." },
            { code: "application_still_rejected", message: "Second fictional notice." },
          ],
        },
      }));
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      await submitReopen(view);

      expect(await view.findByText("Fictional notice: the application stays as it was.")).toBeDefined();
      expect(view.getByText("Second fictional notice.")).toBeDefined();
      await waitFor(() => expect(view.queryByLabelText("Target Disposition *")).toBeNull());
      // The code is never rendered or used to pick wording.
      expect(view.queryByText(/zz_code_the_page_has_never_seen/)).toBeNull();
      expect(view.getAllByRole("alert").length).toBeGreaterThan(0);
    });

    it("renders warning text as plain text, never as markup", async () => {
      const hostile = "<img src=x onerror=alert(1)> <b>bold</b>";
      const Stub = stubFor(rejectedPosting, async () => ({
        ok: true,
        posting: {
          ...rejectedPosting,
          disposition: "lead",
          warnings: [{ code: "application_closed", message: hostile }],
        },
      }));
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      await submitReopen(view);

      expect(await view.findByText(hostile)).toBeDefined();
      expect(view.container.querySelector("img")).toBeNull();
      expect(view.container.querySelector("b")).toBeNull();
    });

    it("shows nothing extra when warnings are empty", async () => {
      const Stub = stubFor(rejectedPosting, async () => ({
        ok: true,
        posting: { ...rejectedPosting, disposition: "lead", warnings: [] },
      }));
      const view = render(React.createElement(Stub, { initialEntries: ["/postings/post-1"] }));
      await submitReopen(view);

      await waitFor(() => expect(view.queryByLabelText("Target Disposition *")).toBeNull());
      expect(view.queryByRole("alert")).toBeNull();
    });
  });
});
