import * as React from "react";

export interface CheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the box. */
  label?: React.ReactNode;
  /** Controlled checked state. */
  checked?: boolean;
}

/** Boolean toggle with a custom-styled box and an optional label. */
export function Checkbox(props: CheckboxProps): JSX.Element;
