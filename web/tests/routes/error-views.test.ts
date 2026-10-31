import { describe, it, expect, beforeEach, vi } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import crypto from "node:crypto";
import NotAuthorized, { loader as notAuthorizedLoader } from "../../app/routes/not-authorized.js";
import ServiceUnavailable from "../../app/routes/service-unavailable.js";
import { action as logoutAction } from "../../app/routes/auth.logout.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

vi.mock("react-router", async () => {
  const actual = await vi.importActual("react-router");
  return {
    ...actual,
    useLoaderData: () => ({ csrfToken: "test-csrf-token" }),
  };
});

describe("error views", () => {
  describe("NotAuthorized view (/not-authorized)", () => {
    it("renders clear explanation of 403 Forbidden without looping or redirect controls", () => {
      const { getByText, queryByRole } = render(React.createElement(NotAuthorized));

      expect(getByText(/Not Authorized|Forbidden|Access Denied/i)).toBeDefined();
      expect(
        getByText(/You do not have permission to access this resource/i)
      ).toBeDefined();

      // Ensure no automatic redirect link loops back to the forbidden resource
      expect(queryByRole("link", { name: /retry/i })).toBeNull();
    });

    it("has zero accessibility violations", async () => {
      const { container } = render(React.createElement(NotAuthorized));
      await expect(container).toHaveNoViolations();
    });
  });

  describe("NotAuthorized loader & sign-out integration", () => {
    const dummyKey = crypto.randomBytes(32);

    beforeEach(() => {
      setSessionEncryptionKeyForTesting(dummyKey);
    });

    it("returns ONLY csrfToken string and never the session object or tokens", async () => {
      const sessionId = crypto.randomUUID();
      const session: UserSession = {
        sessionId,
        ownerSub: "owner-123",
        accessToken: "secret-access-token",
        refreshToken: "secret-refresh-token",
        idToken: "secret-id-token",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken: "my-valid-csrf-token",
      };
      saveSession(session);

      const request = new Request("https://localhost:5003/not-authorized", {
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
      });

      const data = await notAuthorizedLoader({ request, params: {}, context: {} } as any);

      expect(data).toEqual({ csrfToken: "my-valid-csrf-token" });
      const serialized = JSON.stringify(data);
      expect(serialized).not.toContain("secret-access-token");
      expect(serialized).not.toContain("secret-refresh-token");
      expect(serialized).not.toContain("secret-id-token");
      expect(serialized).not.toContain("owner-123");
    });

    it("signing out from /not-authorized with a valid session succeeds", async () => {
      const sessionId = crypto.randomUUID();
      const csrfToken = "valid-csrf-xyz";
      const session: UserSession = {
        sessionId,
        ownerSub: "owner-123",
        accessToken: "tok",
        refreshToken: "ref",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken,
      };
      saveSession(session);

      // 1. Call loader to get csrfToken
      const loaderReq = new Request("https://localhost:5003/not-authorized", {
        headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
      });
      const loaderData = await notAuthorizedLoader({ request: loaderReq, params: {}, context: {} } as any);
      expect(loaderData.csrfToken).toBe(csrfToken);

      // 2. Submit form to /auth/logout with the csrfToken
      const formData = new FormData();
      formData.set("_csrf", loaderData.csrfToken);

      const logoutReq = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        headers: {
          Cookie: `${SECURE_COOKIE_NAME}=${sessionId}`,
        },
        body: formData,
      });

      const response = await logoutAction({ request: logoutReq, params: {}, context: {} } as any);
      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toBeTruthy();
    });

    it("signing out from /not-authorized without a session also succeeds", async () => {
      // 1. Loader returns empty csrfToken when session is missing
      const loaderReq = new Request("https://localhost:5003/not-authorized");
      const loaderData = await notAuthorizedLoader({ request: loaderReq, params: {}, context: {} } as any);
      expect(loaderData.csrfToken).toBe("");

      // 2. Submit form to /auth/logout without a session
      const formData = new FormData();
      formData.set("_csrf", loaderData.csrfToken);

      const logoutReq = new Request("https://localhost:5003/auth/logout", {
        method: "POST",
        body: formData,
      });

      const response = await logoutAction({ request: logoutReq, params: {}, context: {} } as any);
      expect(response.status).toBe(302);
      expect(response.headers.get("Location")).toBeTruthy();
    });
  });

  describe("ServiceUnavailable view (/service-unavailable)", () => {
    it("renders EmptyState and displays remediation command pwsh ./scripts/start-services.ps1", () => {
      const { getByRole, getByText } = render(React.createElement(ServiceUnavailable));

      expect(getByRole("heading", { name: /Service Unavailable/i })).toBeDefined();
      expect(getByText(/pwsh \.\/scripts\/start-services\.ps1/i)).toBeDefined();
    });

    it("has zero accessibility violations", async () => {
      const { container } = render(React.createElement(ServiceUnavailable));
      await expect(container).toHaveNoViolations();
    });
  });
});
