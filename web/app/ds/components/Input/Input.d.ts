import * as React from "react";

export interface InputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "size"> {
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
  /** Icon node rendered inside, at the start of the field. */
  leadingIcon?: React.ReactNode;
  /** Fixed width: "xs" (5rem), "sm" (7rem) or "md" (12rem). Omit to fill the container. */
  width?: "xs" | "sm" | "md";
}

/** Single-line text field. Always paired with a label. */
export function Input(props: InputProps): JSX.Element;
