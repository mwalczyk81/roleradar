import * as React from "react";

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
  children?: React.ReactNode;
}

/** Native dropdown styled to match Input, with a chevron affordance. */
export function Select(props: SelectProps): JSX.Element;
