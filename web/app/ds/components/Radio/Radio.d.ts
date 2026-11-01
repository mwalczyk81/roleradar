import * as React from "react";

export interface RadioProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the dot. */
  label?: React.ReactNode;
  /** Controlled checked state. */
  checked?: boolean;
}

/** Single choice within a group — give every option in the group the same `name`. */
export function Radio(props: RadioProps): JSX.Element;
