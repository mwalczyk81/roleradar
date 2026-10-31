import * as React from "react";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
  /** Icon node rendered inside, at the start of the field. */
  leadingIcon?: React.ReactNode;
}

/** Single-line text field. Always paired with a label. */
export function Input(props: InputProps): JSX.Element;
