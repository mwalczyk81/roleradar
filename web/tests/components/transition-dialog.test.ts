import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { TransitionPostingDialog } from "../../app/components/TransitionPostingDialog.jsx";
import { Toast } from "../../app/ds/components/src/feedback/Toast.jsx";
import { ToastRegion } from "../../app/ds/components/src/feedback/ToastRegion.jsx";

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
        versionToken: "token-abc-123",
        csrfToken: "csrf-xyz-789",
        criteria,
      })
    );

    // Target options present
    expect(screen.getByText(/Screen In|screened_in/i)).toBeDefined();
    expect(screen.getByText(/Screen Out|screened_out/i)).toBeDefined();

    // Critical invariant: 'applied' is NEVER offered as a transition option
    const options = Array.from(container.querySelectorAll("option")).map((o) => o.value);
    expect(options).not.toContain("applied");

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

  it("renders inline field error messages when validation fails", () => {
    render(
      React.createElement(TransitionPostingDialog, {
        open: true,
        onClose: vi.fn(),
        postingId: "post-123",
        currentDisposition: "lead",
        versionToken: "token-1",
        csrfToken: "csrf-1",
        error: "Cannot transition from archived to lead.",
        fieldErrors: {
          reason: "Reason is required for this transition.",
        },
      })
    );

    expect(screen.getByText("Cannot transition from archived to lead.")).toBeDefined();
    expect(screen.getByText("Reason is required for this transition.")).toBeDefined();
  });

  it("renders 409 Conflict Toast with reload action in ToastRegion", () => {
    const onReload = vi.fn();
    render(
      React.createElement(
        ToastRegion,
        { label: "Notifications" },
        React.createElement(Toast, {
          title: "Conflict Detected",
          message: "This posting was updated elsewhere. Please reload to see the latest changes.",
          variant: "warning",
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
          versionToken: "tok-1",
          csrfToken: "csrf-1",
          criteria,
        }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });
});
