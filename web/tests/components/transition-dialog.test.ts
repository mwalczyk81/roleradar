import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { TransitionPostingDialog } from "../../app/components/TransitionPostingDialog.jsx";
import { Toast, ToastRegion } from "../../app/ui.js";
import { createMockFetcher } from "../test-helpers.js";

describe("Transition Posting Dialog & Conflict Toasts (T036)", () => {
  const criteria = [
    { id: "crit-1", name: "Requires Remote" },
    { id: "crit-2", name: "High Compensation" },
  ];

  it("renders target options excluding 'applied' and required fields with hidden _csrf and versionToken", () => {
    const { container } = render(
      React.createElement(TransitionPostingDialog, {
        open: true,
        onClose: vi.fn(),
        postingId: "post-123",
        currentDisposition: "lead",
        currentResurfacePolicy: "never",
        versionToken: "token-abc-123",
        csrfToken: "csrf-xyz-789",
        criteria,
      })
    );

    // Valid target options for 'lead'
    expect(screen.getAllByText(/Scored/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Screened Out/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Closed/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Unscored/i).length).toBeGreaterThan(0);

    // Critical invariant: 'applied' is NEVER offered as a transition option
    const options = Array.from(container.querySelectorAll("option")).map((o) => o.value);
    expect(options).not.toContain("applied");
    expect(options).toEqual(expect.arrayContaining(["scored", "screened_out", "closed", "unscored"]));

    // Hidden inputs
    const csrfInput = container.querySelector("input[name='_csrf']") as HTMLInputElement;
    expect(csrfInput).toBeDefined();
    expect(csrfInput.value).toBe("csrf-xyz-789");

    const versionInput = container.querySelector("input[name='versionToken']") as HTMLInputElement;
    expect(versionInput).toBeDefined();
    expect(versionInput.value).toBe("token-abc-123");

    // Criteria options
    expect(screen.getByText("Requires Remote")).toBeDefined();
    expect(screen.getByText("High Compensation")).toBeDefined();
  });

  it("renders inline error messages from fetcher action data when validation fails", () => {
    const mockFetcher = createMockFetcher({
      state: "idle",
      data: {
        ok: false as const,
        message: "Invalid state transition from 'closed' to 'scored'.",
        fieldErrors: {
          reason: "Reason is required for this transition.",
        },
      },
    });

    render(
      React.createElement(TransitionPostingDialog, {
        open: true,
        onClose: vi.fn(),
        postingId: "post-123",
        currentDisposition: "lead",
        versionToken: "token-1",
        csrfToken: "csrf-1",
        fetcher: mockFetcher,
      })
    );

    expect(screen.getByText("Invalid state transition from 'closed' to 'scored'.")).toBeDefined();
    expect(screen.getByText("Reason is required for this transition.")).toBeDefined();
  });

  it("renders 409 Conflict Toast with reload action in ToastRegion", () => {
    const onReload = vi.fn();
    render(
      React.createElement(
        ToastRegion,
        null,
        React.createElement(Toast, {
          title: "Conflict Detected",
          variant: "warning",
          message: "This posting was updated elsewhere. Please reload to see the latest changes.",
          action: {
            label: "Reload Posting",
            onClick: onReload,
          },
        })
      )
    );

    expect(screen.getByText("Conflict Detected")).toBeDefined();
    expect(screen.getByText(/This posting was updated elsewhere/)).toBeDefined();
    expect(screen.getByRole("button", { name: "Reload Posting" })).toBeDefined();
  });

  it("has zero accessibility violations in light and dark mode", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(TransitionPostingDialog, {
          key: "d",
          open: true,
          onClose: vi.fn(),
          postingId: "post-1",
          currentDisposition: "lead",
          currentResurfacePolicy: "never",
          versionToken: "tok-1",
          csrfToken: "csrf-1",
          criteria,
        }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(TransitionPostingDialog, {
          key: "d",
          open: true,
          onClose: vi.fn(),
          postingId: "post-1",
          currentDisposition: "lead",
          currentResurfacePolicy: "never",
          versionToken: "tok-1",
          csrfToken: "csrf-1",
          criteria,
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });
});
