import * as React from "react";

export interface StatTileDelta {
  /** The signed change, such as "+3" or "-12%". */
  value: string;
  /** Which way it went; picks the arrow. @default "flat" */
  direction?: "up" | "down" | "flat";
  /** Whether the change is good news: true is green, false is red, omitted is neutral. */
  good?: boolean;
  /** The period compared with, such as "vs previous 30 days". */
  period?: string;
}

export interface StatTileProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "children"> {
  /** What is measured, in sentence case with no trailing colon. */
  label: string;
  /** The figure. Numbers are formatted with `valueFormat`; strings are shown as given ("22%"). */
  value: number | string;
  /** Keeps the count next to a percentage: "4 of 18". */
  of?: string;
  delta?: StatTileDelta;
  /** Points for a Sparkline beside the value, oldest first. */
  trend?: number[];
  /** What the trend is, for its accessible name. */
  trendLabel?: string;
  /** Your own note under the value, shown instead of the automatic small-sample note. */
  note?: string;
  /** The count the figure is out of. Under `smallSampleBelow` the tile shows "Small sample". */
  denominator?: number;
  /** @default 10 */
  smallSampleBelow?: number;
  /** Formats a numeric `value` and the trend summary. @default formatCount (1,284 / 12.9K) */
  valueFormat?: (value: number) => string;
}

/** One headline number with its denominator, change and trend. */
export function StatTile(props: StatTileProps): JSX.Element;
