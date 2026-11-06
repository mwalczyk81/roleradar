import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * EmptyState — a calm placeholder for an empty list, a search with no results, a missing page,
 * or a service that is unavailable. `title` is a heading (level 2 by default; set `headingLevel`
 * to fit the page outline). `icon` is a node, `action` and `secondaryAction` are nodes
 * (usually Buttons). Set `role="status"` when the state replaces content after an update.
 * Styled by `.ds-empty*` in bundle.css.
 */
export function EmptyState({ icon, title, description, action, secondaryAction, headingLevel = 2, className, style, ...rest }) {
  const Heading = "h" + Math.min(6, Math.max(1, headingLevel));
  return (
    <div {...rest} className={cx("ds-empty", className)} style={style}>
      {icon ? (
        <span className="ds-empty-icon" aria-hidden="true">
          {icon}
        </span>
      ) : null}
      <Heading className="ds-empty-title">{title}</Heading>
      {description ? <p className="ds-empty-description">{description}</p> : null}
      {action || secondaryAction ? (
        <div className="ds-empty-actions">
          {action}
          {secondaryAction}
        </div>
      ) : null}
    </div>
  );
}
