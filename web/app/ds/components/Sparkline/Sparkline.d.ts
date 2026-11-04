import * as React from "react";

export interface SparklineProps extends Omit<React.SVGAttributes<SVGSVGElement>, "values" | "width" | "height"> {
  /** The points, oldest first. The last one is drawn as the accent dot. */
  values: number[];
  /** @default 96 */
  width?: number;
  /** @default 28 */
  height?: number;
  /** What the line is, read before the range summary: "Applications per week". */
  label?: string;
  /** Formats numbers in the accessible name. @default formatCount (1,284 / 12.9K) */
  valueFormat?: (value: number) => string;
  /** Colour of the 2px ring around the end dot; match the ground it sits on. @default "var(--ds-surface)" */
  surface?: string;
}

/** Tiny trend line with no axes: de-emphasis gray line, accent end dot. */
export function Sparkline(props: SparklineProps): JSX.Element | null;
