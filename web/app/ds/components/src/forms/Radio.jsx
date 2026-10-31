import React from "react";

/**
 * Radio — single choice within a group (share the same `name`).
 * The native input is visually hidden; base.css rings the drawn circle on
 * keyboard focus via the data-ds-focus-target attribute.
 */
export function Radio({ checked, defaultChecked, disabled = false, label, name, value, onChange, style = {}, ...rest }) {
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
          borderRadius: "var(--ds-radius-full)",
          border: `var(--ds-border-thin) solid ${on ? "var(--ds-primary)" : "var(--ds-border-control)"}`,
          background: "var(--ds-surface)",
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          transition: "border-color var(--ds-duration-fast) var(--ds-ease-standard)",
        }}
      >
        <input
          {...rest}
          type="radio"
          name={name}
          value={value}
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
          style={{ position: "absolute", opacity: 0, width: "100%", height: "100%", margin: 0, cursor: "inherit" }}
        />
        {on && (
          <span
            style={{
              width: "0.5rem",
              height: "0.5rem",
              borderRadius: "var(--ds-radius-full)",
              background: "var(--ds-primary)",
            }}
          />
        )}
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
