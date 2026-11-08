import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import React from "react";
import { renderToString } from "react-dom/server";
import {
  Alert,
  AppShell,
  Avatar,
  Badge,
  BarChart,
  Button,
  Card,
  Checkbox,
  ColumnChart,
  DataTable,
  EmptyState,
  Input,
  Menu,
  Modal,
  Radio,
  Select,
  Skeleton,
  Sparkline,
  StatTile,
  Switch,
  Tabs,
  Textarea,
  Toast,
  ToastRegion,
  Toolbar,
  Tooltip,
} from "../../app/ui.js";
import { styleAttributeElements } from "../e2e/support/style-census.js";

/**
 * 009 K1: no kit component writes a style attribute into server-rendered markup. A page that sets
 * `style-src-attr 'none'` (the Content-Security-Policy that blocks inline style attributes) would otherwise lose
 * every one of them. Dynamic values are classes in bundle.css or are set after mount through the CSSOM, which the
 * policy allows. The source guard below keeps new inline style objects out of the vendored kit.
 */

const noop = (): void => undefined;
const columns = [
  { key: "name", header: "Name", sortable: true, width: "40%" },
  { key: "score", header: "Score", numeric: true, sortable: true, width: "8rem" },
  { key: "note", header: "Note", align: "center" as const },
];
const rows = [
  { id: "1", name: "Acme", score: 92, note: "ok" },
  { id: "2", name: "Globex", score: 71, note: "" },
];
const series = [
  { label: "Jul 6", fullLabel: "Week of 2026-07-06", value: 4 },
  { label: "Jul 13", fullLabel: "Week of 2026-07-13", value: 9 },
  { label: "Jul 20", fullLabel: "Week of 2026-07-20", value: 0 },
];
const stages = [
  { label: "Leads", value: 40 },
  { label: "Applied", value: 12, highlight: true },
  { label: "Interview", value: 3 },
];

const cases: Array<[string, React.ReactElement]> = [
  ["Alert", <Alert key="a" variant="danger" title="Title" onClose={noop}>Body</Alert>],
  ["AppShell", <AppShell key="b" brand="App" title="Page" theme="dark" onThemeToggle={noop} userLabel="owner" navItems={[{ label: "Home", href: "/", active: true, badge: 3 }]}>Main</AppShell>],
  ["Avatar", <Avatar key="c" name="Ada Lovelace" size="lg" />],
  ["Badge", <Badge key="d" tone="success" dot>Open</Badge>],
  ["BarChart", <BarChart key="e" title="Funnel" data={stages} funnel />],
  ["Button", <Button key="f" variant="danger" size="lg" fullWidth disabled>Go</Button>],
  ["Card", <Card key="g" padding="md" interactive>Body</Card>],
  ["Checkbox", <Checkbox key="h" label="Agree" defaultChecked />],
  ["ColumnChart", <ColumnChart key="i" title="Per week" data={series} height={120} />],
  ["DataTable", <DataTable key="j" caption="Rows" columns={columns} data={rows} maxHeight="12rem" pagination={{ total: 20, limit: 2, offset: 0, onPageChange: noop }} />],
  ["DataTable loading", <DataTable key="k" caption="Rows" columns={columns} data={[]} isLoading />],
  ["DataTable empty", <DataTable key="l" caption="Rows" columns={columns} data={[]} />],
  ["EmptyState", <EmptyState key="m" title="Nothing" description="Nothing here" action={<Button>Add</Button>} />],
  ["Input", <Input key="n" aria-label="Min" width="sm" />],
  ["Input with icon", <Input key="o" aria-label="Find" leadingIcon={<span>?</span>} invalid />],
  ["Menu", <Menu key="p" label="More" trigger="More" items={[{ label: "Edit", onClick: noop }, { separator: true }, { label: "Delete", danger: true }]} />],
  ["Modal", <Modal key="q" open onClose={noop} title="Title" footer={<Button>OK</Button>} width={600}>Body</Modal>],
  ["Radio", <Radio key="r" name="g" value="a" label="A" defaultChecked />],
  ["Select", <Select key="s" aria-label="Pick"><option>One</option></Select>],
  ["Skeleton text", <Skeleton key="t" width="60%" />],
  ["Skeleton table", <Skeleton key="u" variant="table" rows={2} columns={5} />],
  ["Skeleton card", <Skeleton key="v" variant="card" />],
  ["Skeleton block", <Skeleton key="w" variant="block" width="12rem" height="3rem" />],
  ["Sparkline", <Sparkline key="x" values={[1, 3, 2, 5]} label="Trend" />],
  ["StatTile", <StatTile key="y" label="Applied" value={12} of="of 40" delta={{ value: "+3", direction: "up", good: true, period: "vs last week" }} trend={[1, 2, 3]} denominator={4} />],
  ["Switch", <Switch key="z" label="On" defaultChecked />],
  ["Tabs", <Tabs key="aa" items={[{ value: "a", label: "A" }, { value: "b", label: "B" }]} defaultValue="a" />],
  ["Textarea", <Textarea key="ab" aria-label="Notes" invalid />],
  ["Toast", <Toast key="ac" variant="warning" title="Saved" message="Done" action={{ label: "Undo", onClick: noop }} onDismiss={noop} />],
  ["ToastRegion", <ToastRegion key="ad"><Toast title="Hi" /></ToastRegion>],
  ["Toolbar", <Toolbar key="ae" defaultSearchValue="acme" filters={[{ id: "1", label: "Status", value: "Open", onRemove: noop }, { id: "2", label: "Owner", value: "Me", onRemove: noop }]} onClearAll={noop} actions={<Button>New</Button>} />],
  ["Tooltip", <Tooltip key="af" content="Hint"><Button>Hover</Button></Tooltip>],
];

describe("vendored kit renders no style attributes (009 K1)", () => {
  it.each(cases)("%s", (_name, element) => {
    expect(styleAttributeElements(renderToString(element))).toEqual([]);
  });

  it("covers every component the app can import", () => {
    const exported = fs
      .readFileSync(path.resolve(process.cwd(), "app", "ui.ts"), "utf8")
      .match(/^export const (\w+) =/gm)
      ?.map((line) => line.replace(/^export const (\w+) =/, "$1"))
      .sort();
    const covered = new Set(cases.map(([name]) => name.split(" ")[0]));
    expect(exported?.filter((name) => !covered.has(name))).toEqual([]);
  });
});

describe("vendored kit source keeps styling in bundle.css (009 K1)", () => {
  const srcDir = path.resolve(process.cwd(), "app", "ds", "components", "src");

  function sources(dir: string, out: string[] = []): string[] {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) sources(full, out);
      else if (entry.name.endsWith(".jsx")) out.push(full);
    }
    return out;
  }

  it("has no inline style object in any component (a caller's own `style` prop is passed through untouched)", () => {
    const offenders = sources(srcDir).flatMap((file) =>
      fs
        .readFileSync(file, "utf8")
        .split("\n")
        .map((line, index) => ({ line, index }))
        .filter(({ line }) => /style=\{\{|style:\s*\{|surfaceStyle|\bstyle\s*=\s*\{\}/.test(line))
        .map(({ index }) => `${path.relative(srcDir, file)}:${index + 1}`),
    );
    expect(offenders).toEqual([]);
  });
});
