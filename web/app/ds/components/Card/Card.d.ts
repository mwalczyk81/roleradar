import * as React from "react";

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Apply the standard space-6 padding. Set false to lay out edge-to-edge content (e.g. a table). @default true */
  padded?: boolean;
  /** Padding step: "none", "sm" (space-4), "md" (space-5) or "lg" (space-6). Wins over `padded`. @default "lg" */
  padding?: "none" | "sm" | "md" | "lg";
  /** Lift to shadow-md on hover and show a pointer cursor. @default false */
  interactive?: boolean;
  children?: React.ReactNode;
}

/** The default surface container. */
export function Card(props: CardProps): JSX.Element;
