import React from "react";

/**
 * Checkbox — boolean toggle with a label. Controlled via `checked`/`onChange`.
 * The native input is visually hidden; bundle.css rings the drawn box on keyboard
 * focus via the data-ds-focus-target attribute.
 */
export function Checkbox({ checked, defaultChecked, disabled = false, label, onChange, style = {}, ...rest }) {
  const isControlled = checked !== undefined;
  const [internal, setInternal] = React.useState(!!defaultChecked);
  const on = isControlled ? checked : internal;

  function handle(e) {
    if (!isControlled) setInternal(e.target.checked);
    onChange && onChange(e);
  }

  return (
    <label
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "var(--ds-space-2)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-sm)",
        color: "var(--ds-text)",
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.5 : 1,
        ...style,
      }}
    >
      <span
        data-ds-focus-target=""
        style={{
          position: "relative",
          width: "1.125rem",
          height: "1.125rem",
          flexShrink: 0,
          borderRadius: "var(--ds-radius-sm)",
          border: `var(--ds-border-thin) solid ${on ? "var(--ds-primary)" : "var(--ds-border-control)"}`,
          background: on ? "var(--ds-primary)" : "var(--ds-surface)",
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          transition: "background var(--ds-duration-fast) var(--ds-ease-standard), border-color var(--ds-duration-fast) var(--ds-ease-standard)",
        }}
      >
        <input
          {...rest}
          type="checkbox"
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
          style={{ position: "absolute", opacity: 0, width: "100%", height: "100%", margin: 0, cursor: "inherit" }}
        />
        {on && (
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="var(--ds-text-on-accent)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M20 6 9 17l-5-5" />
          </svg>
        )}
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
