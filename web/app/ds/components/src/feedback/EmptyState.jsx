import React from "react";

/**
 * EmptyState — a calm placeholder for an empty list, a search with no results, a missing page,
 * or a service that is unavailable. `title` is a heading (level 2 by default; set `headingLevel`
 * to fit the page outline). `icon` is a node, `action` and `secondaryAction` are nodes
 * (usually Buttons). Set `role="status"` when the state replaces content after an update.
 */
export function EmptyState({ icon, title, description, action, secondaryAction, headingLevel = 2, style = {}, ...rest }) {
  const Heading = "h" + Math.min(6, Math.max(1, headingLevel));
  return (
    <div
      {...rest}
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        textAlign: "center",
        gap: "var(--ds-space-3)",
        padding: "var(--ds-space-12) var(--ds-space-6)",
        fontFamily: "var(--ds-font-sans)",
        color: "var(--ds-text)",
        ...style,
      }}
    >
      {icon ? (
        <span
          aria-hidden="true"
          style={{
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            width: "3rem",
            height: "3rem",
            borderRadius: "var(--ds-radius-full)",
            background: "var(--ds-surface-hover)",
            color: "var(--ds-text-secondary)",
          }}
        >
          {icon}
        </span>
      ) : null}
      <Heading style={{ margin: 0, fontSize: "var(--ds-text-lg)", fontWeight: "var(--ds-weight-semibold)", lineHeight: "var(--ds-leading-tight)" }}>{title}</Heading>
      {description ? (
        <p style={{ margin: 0, maxWidth: "28rem", fontSize: "var(--ds-text-sm)", lineHeight: "var(--ds-leading-normal)", color: "var(--ds-text-secondary)" }}>{description}</p>
      ) : null}
      {action || secondaryAction ? (
        <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", gap: "var(--ds-space-3)", marginTop: "var(--ds-space-2)" }}>
          {action}
          {secondaryAction}
        </div>
      ) : null}
    </div>
  );
}
