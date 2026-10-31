import * as React from "react";

export interface SkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Shape of the placeholder. @default "text" */
  variant?: "text" | "table" | "card" | "block";
  /** Lines (text) or body rows (table). @default 3 */
  rows?: number;
  /** Column count for the table variant. @default 4 */
  columns?: number;
  /** Width of the block variant, or of the whole skeleton. */
  width?: string;
  /** Height of the block variant. */
  height?: string;
  /** Text announced to screen readers. @default "Loading" */
  label?: string;
}

/** Loading placeholder shapes, announced once as "Loading". */
export function Skeleton(props: SkeletonProps): JSX.Element;
