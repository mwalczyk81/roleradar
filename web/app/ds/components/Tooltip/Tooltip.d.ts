import * as React from "react";

export interface TooltipProps extends Omit<React.HTMLAttributes<HTMLSpanElement>, "content"> {
  /** The hint text. Keep it short. */
  content: React.ReactNode;
  /** Exactly one element, such as a button. It gets aria-describedby. */
  children: React.ReactElement;
  /** @default "top" */
  placement?: "top" | "bottom";
  /** Milliseconds of hover before it shows (focus shows it at once). @default 300 */
  delay?: number;
}

/** Short hint for one control, shown on hover and keyboard focus. */
export function Tooltip(props: TooltipProps): JSX.Element;
