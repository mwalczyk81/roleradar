import * as React from "react";

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Color tone. Status tones carry meaning — don't use them decoratively. @default "primary" */
  tone?: "neutral" | "primary" | "success" | "warning" | "danger";
  /** Show a leading status dot. @default false */
  dot?: boolean;
  children?: React.ReactNode;
}

/** Small pill for status labels or counts. */
export function Badge(props: BadgeProps): JSX.Element;
