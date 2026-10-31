import React from "react";

/**
 * Switch — instant on/off toggle (applies immediately, no Save needed).
 * The native input is visually hidden; base.css rings the drawn track on
 * keyboard focus via the data-ds-focus-target attribute.
 */
export function Switch({ checked, defaultChecked, disabled = false, label, onChange, style = {}, ...rest }) {
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
          width: "2.25rem",
          height: "1.25rem",
          flexShrink: 0,
          borderRadius: "var(--ds-radius-full)",
          background: on ? "var(--ds-primary)" : "var(--ds-border-control)",
          transition: "background var(--ds-duration-base) var(--ds-ease-standard)",
        }}
      >
        <input
          {...rest}
          type="checkbox"
          role="switch"
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
          style={{ position: "absolute", opacity: 0, width: "100%", height: "100%", margin: 0, cursor: "inherit" }}
        />
        <span
          style={{
            position: "absolute",
            top: "0.125rem",
            left: on ? "1.125rem" : "0.125rem",
            width: "1rem",
            height: "1rem",
            borderRadius: "var(--ds-radius-full)",
            background: "var(--ds-neutral-0)",
            boxShadow: "var(--ds-shadow-sm)",
            transition: "left var(--ds-duration-base) var(--ds-ease-standard)",
          }}
        />
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
