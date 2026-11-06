import React from "react";
import type { FetcherWithComponents } from "react-router";
import { Alert, Modal, Select, Input, Button } from "../ui.js";
import { formatDate } from "../lib/format-date.js";
import type { TransitionActionData } from "../routes/postings.$id.js";
import { useCloseOnSubmitSuccess } from "../lib/use-close-on-submit-success.js";
import {
  type PostingDisposition,
  type ResurfacePolicy,
  DISPOSITION_METADATA,
  RESURFACE_POLICY_VALUES,
  RESURFACE_POLICY_LABELS,
  getValidTransitionTargets,
} from "../lib/dispositions.js";

export interface TransitionPostingDialogProps {
  open: boolean;
  onClose: () => void;
  postingId: string;
  currentDisposition: string;
  currentResurfacePolicy?: string;
  versionToken: string;
  /** Id of the posting's application, or null/undefined when it has none (FR-015). */
  applicationId?: string | null;
  csrfToken: string;
  criteria?: Array<{ id: string; name: string }>;
  fetcher?: FetcherWithComponents<TransitionActionData>;
}

export function TransitionPostingDialog({
  open,
  onClose,
  postingId,
  currentDisposition,
  currentResurfacePolicy = "never",
  versionToken,
  applicationId = null,
  csrfToken,
  criteria = [],
  fetcher,
}: TransitionPostingDialogProps) {
  const validRules = getValidTransitionTargets(
    currentDisposition as PostingDisposition,
    currentResurfacePolicy as ResurfacePolicy
  );

  const initialTarget = validRules.length > 0 ? validRules[0].to : "";
  const [selectedDisposition, setSelectedDisposition] = React.useState(initialTarget);
  const [reason, setReason] = React.useState("");
  const [criterionId, setCriterionId] = React.useState("");
  const [resurfacePolicy, setResurfacePolicy] = React.useState("");
  const [overrideReason, setOverrideReason] = React.useState("");
  const [eventDate, setEventDate] = React.useState("");

  // Update selected target when current disposition or open state changes
  React.useEffect(() => {
    if (open) {
      const targets = getValidTransitionTargets(
        currentDisposition as PostingDisposition,
        currentResurfacePolicy as ResurfacePolicy
      );
      if (targets.length > 0) {
        setSelectedDisposition(targets[0].to);
      } else {
        setSelectedDisposition("");
      }
      setReason("");
      setCriterionId("");
      setResurfacePolicy("");
      setOverrideReason("");
      setEventDate(formatDate(new Date().toISOString()));
    }
  }, [open, currentDisposition, currentResurfacePolicy]);

  // When a transition made from this dialog succeeds, close it (not on a reopen with an old success in the fetcher)
  useCloseOnSubmitSuccess(open, fetcher, onClose);

  if (!open) return null;

  const isSubmitting = fetcher?.state === "submitting" || fetcher?.state === "loading";
  const actionData = fetcher?.data;
  const generalError = actionData?.ok === false ? actionData.message : null;
  const fieldErrors = actionData?.ok === false ? actionData.fieldErrors : undefined;

  const FormComponent = fetcher?.Form || "form";

  // Rejecting an applied posting that has an application also records the rejection there (FR-015).
  const recordsOnApplication =
    currentDisposition === "applied" && selectedDisposition === "rejected" && Boolean(applicationId);

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Change Posting Disposition"
      ariaLabel="Change Posting Disposition"
      width={520}
    >
      <FormComponent
        method="post"
        action={`/postings/${postingId}`}
        className="dialog-form"
      >
        <input type="hidden" name="_csrf" value={csrfToken} />
        <input type="hidden" name="versionToken" value={versionToken} />

        {generalError ? (
          <div
            role="alert"
            className="dialog-alert-danger"
          >
            {generalError}
          </div>
        ) : null}

        <div className="field-caption">
          Current disposition:{" "}
          <strong>
            {DISPOSITION_METADATA[currentDisposition as PostingDisposition]?.label ?? currentDisposition}
          </strong>
        </div>

        {validRules.length === 0 ? (
          <div
            className="dialog-notice"
          >
            No disposition transitions are permitted from current disposition &apos;{currentDisposition}&apos; with policy &apos;{currentResurfacePolicy}&apos;.
          </div>
        ) : (
          <>
            {/* Target disposition */}
            <div className="dialog-field">
              <label
                htmlFor="toDisposition"
                className="dialog-field-label"
              >
                Target Disposition *
              </label>
              <Select
                id="toDisposition"
                name="toDisposition"
                value={selectedDisposition}
                onChange={(e) => setSelectedDisposition(e.target.value)}
                required
                invalid={Boolean(fieldErrors?.toDisposition)}
              >
                {validRules.map((rule) => {
                  const meta = DISPOSITION_METADATA[rule.to];
                  return (
                    <option key={rule.to} value={rule.to}>
                      {meta?.label ?? rule.to} ({rule.to})
                    </option>
                  );
                })}
              </Select>
              {fieldErrors?.toDisposition ? (
                <span className="dialog-field-error">
                  {fieldErrors.toDisposition}
                </span>
              ) : null}
            </div>

            {recordsOnApplication ? (
              <>
                <Alert variant="info">This also records the rejection on the application.</Alert>

                <div className="dialog-field">
                  <label htmlFor="eventDate" className="dialog-field-label">
                    Event Date (Optional)
                  </label>
                  <Input
                    id="eventDate"
                    name="eventDate"
                    value={eventDate}
                    onChange={(e) => setEventDate(e.target.value)}
                    placeholder="YYYY-MM-DD"
                    invalid={Boolean(fieldErrors?.eventDate)}
                    aria-describedby="eventDate-hint"
                  />
                  <span id="eventDate-hint" className="dialog-field-hint">
                    The date the rejection is recorded on the application. Defaults to today (UTC).
                  </span>
                  {fieldErrors?.eventDate ? (
                    <span className="dialog-field-error">{fieldErrors.eventDate}</span>
                  ) : null}
                </div>
              </>
            ) : null}

            {/* Reason */}
            <div className="dialog-field">
              <label
                htmlFor="reason"
                className="dialog-field-label"
              >
                Reason *
              </label>
              <Input
                id="reason"
                name="reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="Reason for changing disposition..."
                required
                invalid={Boolean(fieldErrors?.reason)}
              />
              {fieldErrors?.reason ? (
                <span className="dialog-field-error">
                  {fieldErrors.reason}
                </span>
              ) : null}
            </div>

            {/* Criterion */}
            <div className="dialog-field">
              <label
                htmlFor="criterionId"
                className="dialog-field-label"
              >
                Associated Criterion (Optional)
              </label>
              <Select
                id="criterionId"
                name="criterionId"
                value={criterionId}
                onChange={(e) => setCriterionId(e.target.value)}
              >
                <option value="">None</option>
                {criteria.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </div>

            {/* Resurface Policy */}
            <div className="dialog-field">
              <label
                htmlFor="resurfacePolicy"
                className="dialog-field-label"
              >
                Resurface Policy (Optional)
              </label>
              <Select
                id="resurfacePolicy"
                name="resurfacePolicy"
                value={resurfacePolicy}
                onChange={(e) => setResurfacePolicy(e.target.value)}
              >
                <option value="">Keep current policy</option>
                {RESURFACE_POLICY_VALUES.map((pol) => (
                  <option key={pol} value={pol}>
                    {RESURFACE_POLICY_LABELS[pol]} ({pol})
                  </option>
                ))}
              </Select>
            </div>

            {/* Override Reason */}
            <div className="dialog-field">
              <label
                htmlFor="overrideReason"
                className="dialog-field-label"
              >
                Override Reason (Optional)
              </label>
              <Input
                id="overrideReason"
                name="overrideReason"
                value={overrideReason}
                onChange={(e) => setOverrideReason(e.target.value)}
                placeholder="Optional override justification..."
              />
            </div>
          </>
        )}

        {/* Actions */}
        <div
          className="dialog-actions"
        >
          <Button variant="secondary" onClick={onClose} type="button">
            Cancel
          </Button>
          <Button
            variant="primary"
            type="submit"
            disabled={isSubmitting || validRules.length === 0}
          >
            {isSubmitting ? "Submitting..." : "Apply Transition"}
          </Button>
        </div>
      </FormComponent>
    </Modal>
  );
}
