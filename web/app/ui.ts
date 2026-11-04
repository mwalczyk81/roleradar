import type * as React from "react";

// Types from vendored component declarations
import type { AlertProps } from "./ds/components/Alert/Alert.d.js";
import type { AppShellProps } from "./ds/components/AppShell/AppShell.d.js";
import type { AvatarProps } from "./ds/components/Avatar/Avatar.d.js";
import type { BarChartProps, BarChartItem } from "./ds/components/BarChart/BarChart.d.js";
import type { BadgeProps } from "./ds/components/Badge/Badge.d.js";
import type { ButtonProps } from "./ds/components/Button/Button.d.js";
import type { CardProps } from "./ds/components/Card/Card.d.js";
import type { CheckboxProps } from "./ds/components/Checkbox/Checkbox.d.js";
import type { ColumnChartProps, ColumnChartItem } from "./ds/components/ColumnChart/ColumnChart.d.js";
import type { DataTableProps, DataTableColumn } from "./ds/components/DataTable/DataTable.d.js";
import type { EmptyStateProps } from "./ds/components/EmptyState/EmptyState.d.js";
import type { InputProps } from "./ds/components/Input/Input.d.js";
import type { MenuProps, MenuItem } from "./ds/components/Menu/Menu.d.js";
import type { ModalProps } from "./ds/components/Modal/Modal.d.js";
import type { RadioProps } from "./ds/components/Radio/Radio.d.js";
import type { SelectProps } from "./ds/components/Select/Select.d.js";
import type { SkeletonProps } from "./ds/components/Skeleton/Skeleton.d.js";
import type { SparklineProps } from "./ds/components/Sparkline/Sparkline.d.js";
import type { StatTileProps } from "./ds/components/StatTile/StatTile.d.js";
import type { SwitchProps } from "./ds/components/Switch/Switch.d.js";
import type { TabsProps, TabItem } from "./ds/components/Tabs/Tabs.d.js";
import type { TextareaProps } from "./ds/components/Textarea/Textarea.d.js";
import type { ToastProps } from "./ds/components/Toast/Toast.d.js";
import type { ToastRegionProps } from "./ds/components/ToastRegion/ToastRegion.d.js";
import type { ToolbarProps, ToolbarFilter } from "./ds/components/Toolbar/Toolbar.d.js";
import type { TooltipProps } from "./ds/components/Tooltip/Tooltip.d.js";

// Raw JSX implementations from vendored source
import { Alert as AlertRaw } from "./ds/components/src/feedback/Alert.jsx";
import { AppShell as AppShellRaw } from "./ds/components/src/layout/AppShell.jsx";
import { Avatar as AvatarRaw } from "./ds/components/src/feedback/Avatar.jsx";
import { Badge as BadgeRaw } from "./ds/components/src/feedback/Badge.jsx";
import { BarChart as BarChartRaw } from "./ds/components/src/data/BarChart.jsx";
import { Button as ButtonRaw } from "./ds/components/src/forms/Button.jsx";
import { Card as CardRaw } from "./ds/components/src/feedback/Card.jsx";
import { Checkbox as CheckboxRaw } from "./ds/components/src/forms/Checkbox.jsx";
import { ColumnChart as ColumnChartRaw } from "./ds/components/src/data/ColumnChart.jsx";
import { DataTable as DataTableRaw } from "./ds/components/src/data/DataTable.jsx";
import { EmptyState as EmptyStateRaw } from "./ds/components/src/feedback/EmptyState.jsx";
import { Input as InputRaw } from "./ds/components/src/forms/Input.jsx";
import { Menu as MenuRaw } from "./ds/components/src/overlay/Menu.jsx";
import { Modal as ModalRaw } from "./ds/components/src/overlay/Modal.jsx";
import { Radio as RadioRaw } from "./ds/components/src/forms/Radio.jsx";
import { Select as SelectRaw } from "./ds/components/src/forms/Select.jsx";
import { Skeleton as SkeletonRaw } from "./ds/components/src/feedback/Skeleton.jsx";
import { Sparkline as SparklineRaw } from "./ds/components/src/data/Sparkline.jsx";
import { StatTile as StatTileRaw } from "./ds/components/src/data/StatTile.jsx";
import { Switch as SwitchRaw } from "./ds/components/src/forms/Switch.jsx";
import { Tabs as TabsRaw } from "./ds/components/src/navigation/Tabs.jsx";
import { Textarea as TextareaRaw } from "./ds/components/src/forms/Textarea.jsx";
import { Toast as ToastRaw } from "./ds/components/src/feedback/Toast.jsx";
import { ToastRegion as ToastRegionRaw } from "./ds/components/src/feedback/ToastRegion.jsx";
import { Toolbar as ToolbarRaw } from "./ds/components/src/data/Toolbar.jsx";
import { Tooltip as TooltipRaw } from "./ds/components/src/overlay/Tooltip.jsx";

// Cast to typed React components ensuring TypeScript verifies all props
export const Alert = AlertRaw as React.FC<AlertProps>;
export const AppShell = AppShellRaw as React.FC<AppShellProps>;
export const Avatar = AvatarRaw as React.FC<AvatarProps>;
export const Badge = BadgeRaw as React.FC<BadgeProps>;
export const BarChart = BarChartRaw as React.FC<BarChartProps>;
export const Button = ButtonRaw as React.FC<ButtonProps>;
export const Card = CardRaw as React.FC<CardProps>;
export const Checkbox = CheckboxRaw as React.FC<CheckboxProps>;
export const ColumnChart = ColumnChartRaw as React.FC<ColumnChartProps>;
export const DataTable = DataTableRaw as <T = Record<string, unknown>>(
  props: Omit<DataTableProps<T>, "keyField"> & { keyField?: string }
) => React.ReactElement;
export const EmptyState = EmptyStateRaw as React.FC<EmptyStateProps>;
export const Input = InputRaw as React.FC<InputProps>;
export const Menu = MenuRaw as React.FC<MenuProps>;
export const Modal = ModalRaw as React.FC<ModalProps>;
export const Radio = RadioRaw as React.FC<RadioProps>;
export const Select = SelectRaw as React.FC<SelectProps>;
export const Skeleton = SkeletonRaw as React.FC<SkeletonProps>;
export const Sparkline = SparklineRaw as React.FC<SparklineProps>;
export const StatTile = StatTileRaw as React.FC<StatTileProps>;
export const Switch = SwitchRaw as React.FC<SwitchProps>;
export const Tabs = TabsRaw as React.FC<TabsProps>;
export const Textarea = TextareaRaw as React.FC<TextareaProps>;
export const Toast = ToastRaw as React.FC<ToastProps>;
export const ToastRegion = ToastRegionRaw as React.FC<ToastRegionProps>;
export const Toolbar = ToolbarRaw as React.FC<ToolbarProps>;
export const Tooltip = TooltipRaw as React.FC<TooltipProps>;

export type {
  AlertProps,
  AppShellProps,
  AvatarProps,
  BadgeProps,
  BarChartItem,
  BarChartProps,
  ButtonProps,
  CardProps,
  CheckboxProps,
  ColumnChartItem,
  ColumnChartProps,
  DataTableProps,
  DataTableColumn,
  EmptyStateProps,
  InputProps,
  MenuProps,
  MenuItem,
  ModalProps,
  RadioProps,
  SelectProps,
  SkeletonProps,
  SparklineProps,
  StatTileProps,
  SwitchProps,
  TabsProps,
  TabItem,
  TextareaProps,
  ToastProps,
  ToastRegionProps,
  ToolbarProps,
  ToolbarFilter,
  TooltipProps,
};
