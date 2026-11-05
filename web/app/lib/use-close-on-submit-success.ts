import * as React from "react";

/** The part of a React Router fetcher this hook needs. */
export interface SubmissionFetcher {
  state: "idle" | "loading" | "submitting";
  data?: { ok: boolean };
}

/**
 * Calls `onSuccess` once when a submission made while the dialog was open finishes with `ok: true`.
 *
 * A fetcher keeps its last `data` until the next submission, so "data is ok and the fetcher is idle" is also
 * true when the dialog is reopened later. Closing on that condition makes a reopened dialog flash and close.
 * Only a submission seen in flight while open counts as a fresh success.
 */
export function useCloseOnSubmitSuccess(
  open: boolean,
  fetcher: SubmissionFetcher | undefined,
  onSuccess: () => void,
): void {
  const sawSubmission = React.useRef(false);
  const state = fetcher?.state;
  const ok = fetcher?.data?.ok === true;

  React.useEffect(() => {
    if (!open) {
      sawSubmission.current = false;
      return;
    }
    if (state !== "idle") {
      sawSubmission.current = true;
      return;
    }
    if (sawSubmission.current && ok) {
      sawSubmission.current = false;
      onSuccess();
    }
  }, [open, state, ok, onSuccess]);
}
