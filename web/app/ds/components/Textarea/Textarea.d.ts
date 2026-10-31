import * as React from "react";

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
}

/** Multi-line text field. Always paired with a label. */
export function Textarea(props: TextareaProps): JSX.Element;
