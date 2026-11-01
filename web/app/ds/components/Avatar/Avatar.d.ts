import * as React from "react";

export interface AvatarProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Image URL. Omit to show initials derived from `name`. */
  src?: string;
  /** Full name — used for initials fallback and the tooltip/alt text. */
  name?: string;
  /** Diameter. @default "md" */
  size?: "sm" | "md" | "lg";
}

/** Circular user image with an initials fallback. */
export function Avatar(props: AvatarProps): JSX.Element;
