import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Radio — single choice within a group (share the same `name`).
 * The native input is visually hidden; bundle.css rings the drawn circle on
 * keyboard focus via the data-ds-focus-target attribute.
 */
export function Radio({ checked, defaultChecked, disabled = false, label, name, value, onChange, className, style, ...rest }) {
  const isControlled = checked !== undefined;
  const [internal, setInternal] = React.useState(!!defaultChecked);
  const on = isControlled ? checked : internal;

  function handle(e) {
    if (!isControlled) setInternal(e.target.checked);
    onChange && onChange(e);
  }

  return (
    <label className={cx("ds-choice", className)} data-disabled={disabled ? "" : undefined} style={style}>
      <span className="ds-radio-box" data-ds-focus-target="" data-on={on ? "" : undefined}>
        <input
          {...rest}
          className="ds-choice-input"
          type="radio"
          name={name}
          value={value}
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
        />
        {on && <span className="ds-radio-dot" />}
      </span>
      {label && <span>{label}</span>}
    </label>
  );
}
