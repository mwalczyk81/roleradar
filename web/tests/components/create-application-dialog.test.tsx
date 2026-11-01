import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { CreateApplicationDialog } from "../../app/components/CreateApplicationDialog.js";
import type { TransitionActionData } from "../../app/routes/postings.$id.js";
import { createMockFetcher } from "../test-helpers.js";

describe("CreateApplicationDialog Component", () => {
  const dummyFetcher = createMockFetcher<TransitionActionData>();

  it("renders when open with form inputs and default values", () => {
    render(
      <CreateApplicationDialog
        open={true}
        onClose={vi.fn()}
        postingId="post-123"
        postingTitle="Principal Cloud Architect"
        currentDisposition="lead"
        csrfToken="csrf-test-token"
        fetcher={dummyFetcher}
      />
    );

    expect(screen.getByText("Create Job Application")).toBeDefined();
    expect(screen.getByText(/Principal Cloud Architect/)).toBeDefined();
    expect(screen.getByLabelText(/Submission Date/)).toBeDefined();
    expect(screen.getByLabelText(/Resume Variant/)).toBeDefined();
    expect(screen.getByLabelText(/Desired Min/)).toBeDefined();
  });

  it("displays override warning and checkbox when disposition requires override", () => {
    render(
      <CreateApplicationDialog
        open={true}
        onClose={vi.fn()}
        postingId="post-123"
        postingTitle="Principal Cloud Architect"
        currentDisposition="screened_out"
        csrfToken="csrf-test-token"
        fetcher={dummyFetcher}
      />
    );

    expect(screen.getByText("Override Required")).toBeDefined();
    expect(
      screen.getByText(/Recording an application requires confirming the decision override/)
    ).toBeDefined();
    expect(screen.getByLabelText(/Override decided disposition/)).toBeDefined();
    expect(screen.getByLabelText(/Override Reason/)).toBeDefined();
  });

  it("renders field errors when fetcher contains validation errors", () => {
    const errorFetcher = createMockFetcher<TransitionActionData>({
      data: {
        ok: false,
        fieldErrors: {
          submittedDate: "Date cannot be in the future",
          desiredSalaryMin: "Minimum salary must be positive",
        },
      },
    });

    render(
      <CreateApplicationDialog
        open={true}
        onClose={vi.fn()}
        postingId="post-123"
        currentDisposition="lead"
        csrfToken="csrf-test-token"
        fetcher={errorFetcher}
      />
    );

    expect(screen.getByText("Date cannot be in the future")).toBeDefined();
    expect(screen.getByText("Minimum salary must be positive")).toBeDefined();
  });

  it("calls onClose when fetcher.data.ok is true and state is idle", () => {
    const onClose = vi.fn();
    const successFetcher = createMockFetcher<TransitionActionData>({
      state: "idle",
      data: { ok: true } as unknown as TransitionActionData,
    });

    render(
      <CreateApplicationDialog
        open={true}
        onClose={onClose}
        postingId="post-123"
        currentDisposition="lead"
        csrfToken="csrf-test-token"
        fetcher={successFetcher}
      />
    );

    expect(onClose).toHaveBeenCalled();
  });
});
