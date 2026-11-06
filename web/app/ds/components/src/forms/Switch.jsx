import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Switch — instant on/off toggle (applies immediately, no Save needed).
 * The native input is visually hidden; bundle.css rings the drawn track on
 * keyboard focus via the data-ds-focus-target attribute.
 */
export function Switch({ checked, defaultChecked, disabled = false, label, onChange, className, style, ...rest }) {
  const isControlled = checked !== undefined;
  const [internal, setInternal] = React.useState(!!defaultChecked);
  const on = isControlled ? checked : internal;

  function handle(e) {
    if (!isControlled) setInternal(e.target.checked);
    onChange && onChange(e);
  }

  return (
    <label className={cx("ds-choice", className)} data-disabled={disabled ? "" : undefined} style={style}>
      <span className="ds-switch-track" data-ds-focus-target="" data-on={on ? "" : undefined}>
        <input
          {...rest}
          className="ds-choice-input"
          type="checkbox"
          role="switch"
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
        />
        <span className="ds-switch-thumb" />
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
