import React from "react";
import { Modal } from "../ds/components/src/overlay/Modal.jsx";
import { Select } from "../ds/components/src/forms/Select.jsx";
import { Input } from "../ds/components/src/forms/Input.jsx";
import { Button } from "../ds/components/src/forms/Button.jsx";

export interface TransitionPostingDialogProps {
  open: boolean;
  onClose: () => void;
  postingId: string;
  currentDisposition: string;
  versionToken: string;
  csrfToken: string;
  criteria?: Array<{ id: string; name: string }>;
  error?: string | null;
  fieldErrors?: Record<string, string>;
  isSubmitting?: boolean;
}

// Available target dispositions excluding 'applied'
const TARGET_DISPOSITIONS = [
  { value: "screened_in", label: "Screen In (screened_in)" },
  { value: "screened_out", label: "Screen Out (screened_out)" },
  { value: "interviewing", label: "Interviewing (interviewing)" },
  { value: "offer", label: "Offer (offer)" },
  { value: "rejected", label: "Rejected (rejected)" },
  { value: "withdrawn", label: "Withdrawn (withdrawn)" },
  { value: "archived", label: "Archived (archived)" },
];

export function TransitionPostingDialog({
  open,
  onClose,
  postingId,
  currentDisposition,
  versionToken,
  csrfToken,
  criteria = [],
  error,
  fieldErrors,
  isSubmitting = false,
}: TransitionPostingDialogProps) {
  const [selectedDisposition, setSelectedDisposition] = React.useState("screened_in");
  const [reason, setReason] = React.useState("");
  const [criterionId, setCriterionId] = React.useState("");
  const [resurfacePolicy, setResurfacePolicy] = React.useState("");
  const [overrideReason, setOverrideReason] = React.useState("");

  if (!open) return null;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Change Posting Disposition"
      ariaLabel="Change Posting Disposition"
      width={520}
    >
      <form
        method="post"
        action={`/postings/${postingId}`}
        style={{
          display: "flex",
          flexDirection: "column",
          gap: "var(--ds-space-4)",
          fontFamily: "var(--ds-font-sans)",
        }}
      >
        <input type="hidden" name="_csrf" value={csrfToken} />
        <input type="hidden" name="versionToken" value={versionToken} />

        {error ? (
          <div
            role="alert"
            style={{
              padding: "var(--ds-space-3)",
              backgroundColor: "var(--ds-danger-subtle)",
              color: "var(--ds-danger)",
              borderRadius: "var(--ds-radius-md)",
              fontSize: "var(--ds-text-sm)",
              fontWeight: "var(--ds-weight-medium)",
            }}
          >
            {error}
          </div>
        ) : null}

        <div style={{ fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}>
          Current disposition: <strong>{currentDisposition}</strong>
        </div>

        {/* Target disposition */}
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-1)" }}>
          <label
            htmlFor="toDisposition"
            style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
          >
            Target Disposition *
          </label>
          <Select
            id="toDisposition"
            name="toDisposition"
            value={selectedDisposition}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => setSelectedDisposition(e.target.value)}
            required
            invalid={Boolean(fieldErrors?.toDisposition)}
          >
            {TARGET_DISPOSITIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </Select>
          {fieldErrors?.toDisposition ? (
            <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
              {fieldErrors.toDisposition}
            </span>
          ) : null}
        </div>

        {/* Reason */}
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-1)" }}>
          <label
            htmlFor="reason"
            style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
          >
            Reason *
          </label>
          <Input
            id="reason"
            name="reason"
            value={reason}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setReason(e.target.value)}
            placeholder="Reason for changing disposition..."
            required
            invalid={Boolean(fieldErrors?.reason)}
          />
          {fieldErrors?.reason ? (
            <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
              {fieldErrors.reason}
            </span>
          ) : null}
        </div>

        {/* Criterion */}
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-1)" }}>
          <label
            htmlFor="criterionId"
            style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
          >
            Associated Criterion (Optional)
          </label>
          <Select
            id="criterionId"
            name="criterionId"
            value={criterionId}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => setCriterionId(e.target.value)}
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
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-1)" }}>
          <label
            htmlFor="resurfacePolicy"
            style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
          >
            Resurface Policy (Optional)
          </label>
          <Select
            id="resurfacePolicy"
            name="resurfacePolicy"
            value={resurfacePolicy}
            onChange={(e: React.ChangeEvent<HTMLSelectElement>) => setResurfacePolicy(e.target.value)}
          >
            <option value="">Keep current policy</option>
            <option value="auto_resurface">Auto Resurface</option>
            <option value="manual_only">Manual Only</option>
            <option value="never">Never</option>
          </Select>
        </div>

        {/* Override Reason */}
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-1)" }}>
          <label
            htmlFor="overrideReason"
            style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
          >
            Override Reason (Optional)
          </label>
          <Input
            id="overrideReason"
            name="overrideReason"
            value={overrideReason}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setOverrideReason(e.target.value)}
            placeholder="Optional override justification..."
          />
        </div>

        {/* Actions */}
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "var(--ds-space-3)",
            marginTop: "var(--ds-space-3)",
          }}
        >
          <Button variant="secondary" onClick={onClose} type="button">
            Cancel
          </Button>
          <Button variant="primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Submitting..." : "Apply Transition"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
