import { describe, it, expect, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import {
  useCloseOnSubmitSuccess,
  type SubmissionFetcher,
} from "../../app/lib/use-close-on-submit-success.js";

interface Props {
  open: boolean;
  fetcher: SubmissionFetcher;
}

const idle = (data?: { ok: boolean }): SubmissionFetcher => ({ state: "idle", data });

function setup(initial: Props) {
  const onSuccess = vi.fn();
  const hook = renderHook(
    ({ open, fetcher }: Props) => useCloseOnSubmitSuccess(open, fetcher, onSuccess),
    { initialProps: initial },
  );
  return { onSuccess, ...hook };
}

describe("useCloseOnSubmitSuccess", () => {
  it("closes once when a submission made while open finishes successfully", () => {
    const { onSuccess, rerender } = setup({ open: true, fetcher: idle() });

    rerender({ open: true, fetcher: { state: "submitting", data: undefined } });
    expect(onSuccess).not.toHaveBeenCalled();

    rerender({ open: true, fetcher: { state: "loading", data: { ok: true } } });
    expect(onSuccess).not.toHaveBeenCalled();

    rerender({ open: true, fetcher: idle({ ok: true }) });
    expect(onSuccess).toHaveBeenCalledTimes(1);

    rerender({ open: true, fetcher: idle({ ok: true }) });
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it("does not close again when the dialog is reopened and the fetcher still holds the old success", () => {
    const { onSuccess, rerender } = setup({ open: true, fetcher: idle() });

    rerender({ open: true, fetcher: { state: "submitting", data: undefined } });
    rerender({ open: true, fetcher: idle({ ok: true }) });
    expect(onSuccess).toHaveBeenCalledTimes(1);

    rerender({ open: false, fetcher: idle({ ok: true }) });
    rerender({ open: true, fetcher: idle({ ok: true }) });
    rerender({ open: true, fetcher: idle({ ok: true }) });

    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it("closes again after a second successful submission", () => {
    const { onSuccess, rerender } = setup({ open: true, fetcher: idle() });

    rerender({ open: true, fetcher: { state: "submitting", data: undefined } });
    rerender({ open: true, fetcher: idle({ ok: true }) });
    rerender({ open: false, fetcher: idle({ ok: true }) });
    rerender({ open: true, fetcher: idle({ ok: true }) });

    rerender({ open: true, fetcher: { state: "submitting", data: { ok: true } } });
    rerender({ open: true, fetcher: idle({ ok: true }) });

    expect(onSuccess).toHaveBeenCalledTimes(2);
  });

  it("stays open when the submission fails", () => {
    const { onSuccess, rerender } = setup({ open: true, fetcher: idle() });

    rerender({ open: true, fetcher: { state: "submitting", data: undefined } });
    rerender({ open: true, fetcher: idle({ ok: false }) });

    expect(onSuccess).not.toHaveBeenCalled();
  });

  it("does nothing while closed, even if a submission completes", () => {
    const { onSuccess, rerender } = setup({ open: false, fetcher: idle() });

    rerender({ open: false, fetcher: { state: "submitting", data: undefined } });
    rerender({ open: false, fetcher: idle({ ok: true }) });

    expect(onSuccess).not.toHaveBeenCalled();
  });
});
