import React from "react";
import type { FetcherWithComponents } from "react-router";
import { Modal, Select, Input, Button, Alert, Checkbox } from "../ui.js";
import { RESUME_VARIANT_OPTIONS } from "../lib/application-enums.js";
import { canCreateApplication } from "../lib/dispositions.js";
import type { TransitionActionData } from "../routes/postings.$id.js";

export interface CreateApplicationDialogProps {
  open: boolean;
  onClose: () => void;
  postingId: string;
  postingTitle?: string;
  currentDisposition: string;
  csrfToken: string;
  fetcher: FetcherWithComponents<TransitionActionData>;
}

export function CreateApplicationDialog({
  open,
  onClose,
  postingId,
  postingTitle,
  currentDisposition,
  csrfToken,
  fetcher,
}: CreateApplicationDialogProps) {
  const requiresOverride = canCreateApplication(currentDisposition).requiresOverride;

  const todayIso = new Date().toISOString().slice(0, 10);
  const [submittedDate, setSubmittedDate] = React.useState(todayIso);
  const [resumeVariant, setResumeVariant] = React.useState("tailored");
  const [desiredSalaryMin, setDesiredSalaryMin] = React.useState("");
  const [desiredSalaryMax, setDesiredSalaryMax] = React.useState("");
  const [desiredSalaryCurrency, setDesiredSalaryCurrency] = React.useState("USD");
  const [targetStartDate, setTargetStartDate] = React.useState("");
  const [resumePointer, setResumePointer] = React.useState("");
  const [confirmationCode, setConfirmationCode] = React.useState("");
  const [overrideDecided, setOverrideDecided] = React.useState(requiresOverride);
  const [overrideReason, setOverrideReason] = React.useState("");

  React.useEffect(() => {
    if (open) {
      setSubmittedDate(new Date().toISOString().slice(0, 10));
      setResumeVariant("tailored");
      setDesiredSalaryMin("");
      setDesiredSalaryMax("");
      setDesiredSalaryCurrency("USD");
      setTargetStartDate("");
      setResumePointer("");
      setConfirmationCode("");
      setOverrideDecided(requiresOverride);
      setOverrideReason("");
    }
  }, [open, requiresOverride]);

  React.useEffect(() => {
    if (open && fetcher.data?.ok === true && fetcher.state === "idle") {
      onClose();
    }
  }, [open, fetcher.data, fetcher.state, onClose]);

  const fieldErrors =
    fetcher.data && !fetcher.data.ok ? fetcher.data.fieldErrors || {} : {};
  const isSubmitting = fetcher.state === "submitting" || fetcher.state === "loading";

  return (
    <Modal open={open} onClose={onClose} title="Create Job Application">
      <fetcher.Form method="post">
        <input type="hidden" name="intent" value="create_application" />
        <input type="hidden" name="_csrf" value={csrfToken} />
        <input type="hidden" name="postingId" value={postingId} />
        <input type="hidden" name="currentDisposition" value={currentDisposition} />

        <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-4)" }}>
          {postingTitle ? (
            <p style={{ margin: 0, fontSize: "var(--ds-text-sm)", color: "var(--ds-text-secondary)" }}>
              Applying for: <strong>{postingTitle}</strong>
            </p>
          ) : null}

          {requiresOverride ? (
            <Alert variant="warning" title="Override Required">
              {`This opportunity is currently in '${currentDisposition}' status. Recording an application requires confirming the decision override.`}
            </Alert>
          ) : null}

          {/* Submission Date */}
          <div>
            <label
              htmlFor="app-submitted-date"
              style={{
                display: "block",
                marginBottom: "var(--ds-space-1)",
                fontSize: "var(--ds-text-sm)",
                fontWeight: "var(--ds-weight-medium)",
              }}
            >
              Submission Date *
            </label>
            <Input
              id="app-submitted-date"
              name="submittedDate"
              type="date"
              value={submittedDate}
              onChange={(e) => setSubmittedDate(e.target.value)}
              required
            />
            {fieldErrors.submittedDate ? (
              <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                {fieldErrors.submittedDate}
              </span>
            ) : null}
          </div>

          {/* Resume Variant */}
          <div>
            <label
              htmlFor="app-resume-variant"
              style={{
                display: "block",
                marginBottom: "var(--ds-space-1)",
                fontSize: "var(--ds-text-sm)",
                fontWeight: "var(--ds-weight-medium)",
              }}
            >
              Resume Variant *
            </label>
            <Select
              id="app-resume-variant"
              name="resumeVariant"
              value={resumeVariant}
              onChange={(e) => setResumeVariant(e.target.value)}
            >
              {RESUME_VARIANT_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </Select>
            {fieldErrors.resumeVariant ? (
              <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                {fieldErrors.resumeVariant}
              </span>
            ) : null}
          </div>

          {/* Desired Salary Row */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 5rem", gap: "var(--ds-space-2)" }}>
            <div>
              <label
                htmlFor="app-salary-min"
                style={{
                  display: "block",
                  marginBottom: "var(--ds-space-1)",
                  fontSize: "var(--ds-text-sm)",
                }}
              >
                Desired Min
              </label>
              <Input
                id="app-salary-min"
                name="desiredSalaryMin"
                type="number"
                placeholder="e.g. 175000"
                value={desiredSalaryMin}
                onChange={(e) => setDesiredSalaryMin(e.target.value)}
              />
              {fieldErrors.desiredSalaryMin ? (
                <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                  {fieldErrors.desiredSalaryMin}
                </span>
              ) : null}
            </div>

            <div>
              <label
                htmlFor="app-salary-max"
                style={{
                  display: "block",
                  marginBottom: "var(--ds-space-1)",
                  fontSize: "var(--ds-text-sm)",
                }}
              >
                Desired Max
              </label>
              <Input
                id="app-salary-max"
                name="desiredSalaryMax"
                type="number"
                placeholder="e.g. 210000"
                value={desiredSalaryMax}
                onChange={(e) => setDesiredSalaryMax(e.target.value)}
              />
              {fieldErrors.desiredSalaryMax ? (
                <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                  {fieldErrors.desiredSalaryMax}
                </span>
              ) : null}
            </div>

            <div>
              <label
                htmlFor="app-salary-curr"
                style={{
                  display: "block",
                  marginBottom: "var(--ds-space-1)",
                  fontSize: "var(--ds-text-sm)",
                }}
              >
                Currency
              </label>
              <Input
                id="app-salary-curr"
                name="desiredSalaryCurrency"
                type="text"
                maxLength={3}
                value={desiredSalaryCurrency}
                onChange={(e) => setDesiredSalaryCurrency(e.target.value.toUpperCase())}
              />
            </div>
          </div>

          {/* Target Start Date */}
          <div>
            <label
              htmlFor="app-target-start"
              style={{
                display: "block",
                marginBottom: "var(--ds-space-1)",
                fontSize: "var(--ds-text-sm)",
              }}
            >
              Target Start Date
            </label>
            <Input
              id="app-target-start"
              name="targetStartDate"
              type="date"
              value={targetStartDate}
              onChange={(e) => setTargetStartDate(e.target.value)}
            />
            {fieldErrors.targetStartDate ? (
              <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                {fieldErrors.targetStartDate}
              </span>
            ) : null}
          </div>

          {/* Resume Pointer */}
          <div>
            <label
              htmlFor="app-resume-pointer"
              style={{
                display: "block",
                marginBottom: "var(--ds-space-1)",
                fontSize: "var(--ds-text-sm)",
              }}
            >
              Resume Pointer / URL
            </label>
            <Input
              id="app-resume-pointer"
              name="resumePointer"
              type="text"
              placeholder="e.g. /resumes/matt-walczyk-ic.pdf"
              maxLength={300}
              value={resumePointer}
              onChange={(e) => setResumePointer(e.target.value)}
            />
            {fieldErrors.resumePointer ? (
              <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                {fieldErrors.resumePointer}
              </span>
            ) : null}
          </div>

          {/* Confirmation Code */}
          <div>
            <label
              htmlFor="app-confirmation-code"
              style={{
                display: "block",
                marginBottom: "var(--ds-space-1)",
                fontSize: "var(--ds-text-sm)",
              }}
            >
              Confirmation Code
            </label>
            <Input
              id="app-confirmation-code"
              name="confirmationCode"
              type="text"
              placeholder="e.g. APP-88294"
              maxLength={100}
              value={confirmationCode}
              onChange={(e) => setConfirmationCode(e.target.value)}
            />
            {fieldErrors.confirmationCode ? (
              <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                {fieldErrors.confirmationCode}
              </span>
            ) : null}
          </div>

          {/* Override controls when required */}
          {requiresOverride ? (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "var(--ds-space-2)",
                padding: "var(--ds-space-3)",
                backgroundColor: "var(--ds-surface-hover)",
                borderRadius: "var(--ds-radius-md)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "var(--ds-space-2)" }}>
                <Checkbox
                  id="app-override-decided"
                  name="overrideDecided"
                  checked={overrideDecided}
                  onChange={(e) => setOverrideDecided(e.target.checked)}
                />
                <label
                  htmlFor="app-override-decided"
                  style={{ fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-medium)" }}
                >
                  Override decided disposition *
                </label>
              </div>

              <div>
                <label
                  htmlFor="app-override-reason"
                  style={{
                    display: "block",
                    marginBottom: "var(--ds-space-1)",
                    fontSize: "var(--ds-text-xs)",
                    color: "var(--ds-text-secondary)",
                  }}
                >
                  Override Reason *
                </label>
                <Input
                  id="app-override-reason"
                  name="overrideReason"
                  type="text"
                  placeholder="Explain why this decided opportunity is being reopened and applied to..."
                  value={overrideReason}
                  onChange={(e) => setOverrideReason(e.target.value)}
                  required={requiresOverride}
                />
                {fieldErrors.overrideReason ? (
                  <span style={{ color: "var(--ds-danger)", fontSize: "var(--ds-text-xs)" }}>
                    {fieldErrors.overrideReason}
                  </span>
                ) : null}
              </div>
            </div>
          ) : null}

          {fetcher.data && !fetcher.data.ok && fetcher.data.message ? (
            <Alert variant="danger">{fetcher.data.message}</Alert>
          ) : null}

          {/* Actions */}
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "var(--ds-space-2)",
              marginTop: "var(--ds-space-2)",
            }}
          >
            <Button type="button" variant="ghost" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={isSubmitting}>
              {isSubmitting ? "Submitting..." : "Record Application"}
            </Button>
          </div>
        </div>
      </fetcher.Form>
    </Modal>
  );
}
