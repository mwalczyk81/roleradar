import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Checkbox — boolean toggle with a label. Controlled via `checked`/`onChange`.
 * The native input is visually hidden; bundle.css rings the drawn box on keyboard
 * focus via the data-ds-focus-target attribute.
 */
export function Checkbox({ checked, defaultChecked, disabled = false, label, onChange, className, style, ...rest }) {
  const isControlled = checked !== undefined;
  const [internal, setInternal] = React.useState(!!defaultChecked);
  const on = isControlled ? checked : internal;

  function handle(e) {
    if (!isControlled) setInternal(e.target.checked);
    onChange && onChange(e);
  }

  return (
    <label className={cx("ds-choice", className)} data-disabled={disabled ? "" : undefined} style={style}>
      <span className="ds-checkbox-box" data-ds-focus-target="" data-on={on ? "" : undefined}>
        <input
          {...rest}
          className="ds-choice-input"
          type="checkbox"
          checked={isControlled ? checked : undefined}
          defaultChecked={isControlled ? undefined : defaultChecked}
          disabled={disabled}
          onChange={handle}
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
