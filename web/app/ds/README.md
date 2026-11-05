# Matt's Design System

A small, clean & modern design system: **warm stone grays, one bronze
accent, generous spacing, subtle shadows, light + dark mode.** Think Linear /
Stripe / Vercel — calm, professional, content-first. It is product-agnostic: the
same tokens and components dress any web UI (and map to native platforms).

> **For Claude / designers:** Build everything from the tokens by name. Never
> hardcode a hex, pixel, or font size that isn't in the system. Use *semantic*
> color tokens (`--ds-primary`, `--ds-text`, `--ds-surface`) — not raw palette —
> so dark mode and rebrands "just work."

## Sources

This system was authored from a token specification provided as a mounted
codebase (`design-system/`), containing:
- `tokens.json` — the source-of-truth design tokens (colors, type, spacing, radius, shadow, motion).
- `tokens.css` — the generated CSS-variable form.
- `design-system.md` — the written rulebook (color roles, component patterns, do/don't).
- `cards/*.html`, `preview.html` — specimen pages.

No brand logos or product codebase or Figma file were provided. Three brand
**webfonts** are self-hosted from `fonts/` — **IBM Plex Sans** (sans), **IBM Plex Serif** (display serif) and **IBM Plex
Mono** (mono). The fictional "Meridian" billing app in `ui_kits/console/` is an original demo built
to exercise the components, not a real product.

---

## CONTENT FUNDAMENTALS

How copy reads in this system — UI text, labels, and microcopy.

- **Voice:** plain, calm, professional. Short and concrete over clever. "Save
  changes," "Delete project?," "Something went wrong." No exclamation marks, no
  hype, no jargon.
- **Person:** address the user as **you** ("Used for sign-in and receipts").
  System speaks plainly about itself in third person ("This can't be undone").
- **Casing:** **Sentence case** everywhere — buttons, titles, menu items. Table
  headers are typed in normal sentence case and rendered uppercase in CSS
  (`text-transform: uppercase` + `letter-spacing: .04em`), never typed in caps.
  ("New invoice," not "New Invoice" or "NEW INVOICE.")
- **Buttons:** verb-first and specific — "Save changes," "Create invoice,"
  "Update card." Avoid bare "OK" / "Submit." Destructive actions name the verb
  ("Delete").
- **Labels & hints:** field labels are nouns ("Full name," "Amount"); helper text
  is a short sentence with a period ("Used for sign-in and receipts.").
- **Status:** terse and factual — "Paid," "Pending," "Overdue," "Active." One word
  where possible.
- **Numbers:** mono font, right-aligned in tables; currency with full precision
  (`$1,200.00`); percentages with a sign of change in prose.
- **Emoji:** **not used** in product UI. (The legacy `preview.html` used a 🌙/☀️
  theme toggle; new work uses an icon instead.)
- **Tone test:** if a sentence could appear in a Stripe dashboard, it fits. If it
  sounds like marketing, cut it.

---

## VISUAL FOUNDATIONS

- **Color vibe:** warm stone neutrals with one bronze accent. Structure is carried entirely by **stone
  grays** (12-step neutral ramp, `#fafaf9`→`#0c0a09`); **one bronze accent** (the ramp from mattwalczyk.com)
  (`--ds-primary` = bronze-600 light / bronze-400 dark) for primary actions, links,
  active state, focus, and selection. Green / yellow / red appear **only** as
  status (success / warning / danger) — never decoration. No second brand color,
  no gradients.
- **Backgrounds:** flat solid fills. Page = `--ds-bg` (white / near-black);
  subtle zones and table headers = `--ds-bg-subtle`. **No** images, textures,
  patterns, or gradient washes. Depth comes from borders + a faint shadow, not
  color.
- **Type:** **IBM Plex Sans** (variable, self-hosted) for body and UI, **IBM Plex Serif** (400, 500, 600, self-hosted) for page titles, the app name and hero numbers; **IBM Plex Mono**
  (400, 500, self-hosted) for code / IDs / table numbers. System stacks
  (`system-ui`, `ui-monospace`) remain as fallbacks behind each. Scale ~1.25 ratio (12 → 36px). Weights: 400 body,
  500 labels/buttons, 600 headings, 700 hero/numbers. Tight leading (1.25) on
  headings, 1.5 on body, 1.7 on long prose. Line length capped ~65ch.
- **Spacing:** strict **4px grid** (`--ds-space-*`). Card padding `space-6` (24),
  field gaps `space-4` (16), icon/label gaps `space-2` (8), section spacing
  `space-8`–`space-12`. Whitespace is a feature — when unsure, add space.
- **Corner radii:** moderate, consistent. Inputs / buttons / badges `md` (8px);
  cards / panels / modals `lg` (12px); pills / avatars / switches `full`. Never
  mix radii on one screen.
- **Borders:** `1px` `--ds-border` for dividers and resting card edges; `1px`
  `--ds-border-control` for the outline of every form control (input, select,
  textarea, checkbox, radio, switch track, secondary button) so it reaches 3:1
  against its surface; `--ds-border-strong` is for emphasis dividers, not
  controls; `2px` reserved for focus rings only. Borders do most of the
  separation work in this style.
- **Shadows (elevation, never decoration):** resting cards `shadow-sm`;
  dropdowns / popovers `shadow-md`; modals / dialogs `shadow-lg`. Subtle,
  cool-tinted (`rgb(15 23 42 / …)`), low opacity. Don't stack shadows on flat
  layouts.
- **Cards:** `--ds-surface` fill, `1px --ds-border`, radius `lg`, `shadow-sm`,
  `space-6` padding. Interactive cards lift to `shadow-md` on hover.
- **Hover states:** surfaces / list items / ghost buttons → `--ds-surface-hover`
  (a step up the neutral ramp). Primary button → `--ds-primary-solid-hover`
  (bronze-700 in both themes). Danger → slight `brightness(0.95)`. No scale on
  hover.
- **Press / active:** color shift only (the `-hover` token), no shrink/scale.
  Active nav item = `--ds-primary-subtle` background + `--ds-primary` text.
- **Focus:** always visible — `2px` `--ds-focus-ring` outline, `offset 2px` (0 on
  inputs, plus border recolor). Non-negotiable for accessibility. `components/bundle.css`
  (loaded after `tokens.css`) draws it for every `:focus-visible` element and,
  through `data-ds-focus-target`, around the drawn box of Checkbox, Radio and
  Switch, whose native input is hidden. Always link `tokens.css` and
  `components/bundle.css` together, never a lone token file, or the rings disappear.
- **Accessibility:** body and UI text must meet **WCAG AA contrast** — ≥4.5:1
  against its background (≥3:1 for large text ≥24px and for UI / graphical
  elements such as borders, icons, and control outlines). Every form control must
  have a **real associated label** — a visible `<label>` (or `aria-label` when the
  label is visually implicit); never use placeholder text as the label, since it
  disappears on input and fails contrast. Verified pairings: `--ds-text`
  and `--ds-text-muted` on `--ds-bg` / `--ds-surface`. `--ds-text-muted` does
  **not** clear 4.5:1 on `--ds-surface-hover` or any tinted ground in light
  mode (4.34:1), so use `--ds-text-secondary` there. Status text on a `-subtle`
  ground uses the `-text` tokens, never the bare status color. Filled buttons use
  `--ds-primary-solid` / `--ds-danger-solid`, never `--ds-primary` or
  `--ds-danger`, behind white text. If you tint a surface yourself, re-check the
  text on it.
- **Transparency / blur:** essentially none, except the modal backdrop
  (`rgb(2 6 23 / 0.55)`, no blur). Surfaces are opaque.
- **Motion:** restrained. Durations `fast 120ms` (hovers), `base 200ms` (most),
  `slow 320ms` (modals/drawers). Easing `standard` cubic-bezier(.4,0,.2,1),
  `out` for entrances. Animate **opacity / transform only** — never layout.
  Respect `prefers-reduced-motion`. No bounces, no infinite loops.
- **Layout:** capped content widths (`container-md 768` for text/forms,
  `lg 1024` / `xl 1280` for app layouts), centered. Grids use `gap` from the
  space scale. Mobile-first; collapse multi-column to single column when narrow.
- **Density:** comfortable by default; data-heavy screens may drop to
  `control-sm` + `space-2/3` but keep the same colors, radii, and fonts.

---

## ACCESSIBILITY TOKENS

Added 2026-10-17 after a contrast and keyboard audit. Existing tokens are
unchanged; these are new roles. Use them instead of the bare token where the
role applies.

| Token | Light | Dark | Use | Contrast |
|---|---|---|---|---|
| `--ds-text-secondary` | neutral-600 | neutral-300 | muted text on tinted grounds, neutral Badge, info Alert | 7.6:1 / 10.2:1 |
| `--ds-border-control` | neutral-450 `#908985` | neutral-500 `#78716c` | outline of any form control, switch off track | 3.4:1 on white, 3.2:1 on dark surface |
| `--ds-primary-solid` (+`-hover`) | bronze-600 (bronze-700) | bronze-600 (bronze-700) | filled primary button behind white text | 5.2:1 (8.0:1) |
| `--ds-danger-solid` | red-600 | red-600 | filled danger button behind white text | 4.8:1 |
| `--ds-primary-subtle-text` | bronze-700 | bronze-300 | accent text on `--ds-primary-subtle` (Badge, Avatar) | 7.5:1 / 8.8:1 |
| `--ds-success-text` | green-700 | green-500 | success text on `--ds-success-subtle` | 4.8:1 / 6.8:1 |
| `--ds-warning-text` | yellow-700 | yellow-500 | warning text on `--ds-warning-subtle` | 4.8:1 / 8.1:1 |
| `--ds-danger-text` | red-700 | red-500 | danger text on `--ds-danger-subtle` | 5.9:1 / 4.7:1 |
| `--ds-chart-primary` (+`-hover`) | bronze-600 (bronze-700) | bronze-500 (bronze-400) | data marks: bars, columns, sparkline end dot | 5.2:1 (8.0:1) / 4.2:1 (6.0:1) on surface |
| `--ds-chart-muted` (+`-hover`) | neutral-450 (neutral-500) | neutral-500 (neutral-400) | de-emphasis marks: non-highlighted bars, sparkline line | 3.4:1 (4.8:1) / 3.2:1 (6.0:1) on surface |

Components behavior: handlers you pass (`onFocus`, `onBlur`, `onMouseEnter`,
`onMouseLeave`) run in addition to the built-in ones and never replace them;
Tabs, the Modal close button and the Alert dismiss button are `type="button"`
and cannot submit a form; Modal moves focus in, traps Tab, restores focus and
locks page scroll; Tabs supports Left/Right/Home/End.

---

## ICONOGRAPHY

- **No icon set ships with the source tokens.** The system adopts
  **[Lucide](https://lucide.dev)** as its recommended icon library — it matches
  the aesthetic exactly: outline style, **2px stroke, round caps and joins, 24×24
  grid**, drawn with `currentColor` so icons inherit text color.
- **⚠️ Substitution flagged:** because no brand icons were provided, the
  `ui_kits/console/Icons.jsx` set is a small hand-built collection of
  **Lucide-accurate paths** (LayoutGrid, FileText, Settings, Search, Plus, Bell,
  Users, CreditCard, TrendingUp, Download). For production, install the real
  `lucide-react` package or pull from the Lucide CDN rather than copying paths.
- **Sizing:** 16px inside `sm` controls and inline with `sm` text; 18–20px in nav
  and topbar; align to the 4px grid. Icons take `--ds-text-muted` when decorative,
  `currentColor`/`--ds-primary` when active.
- **No emoji** as icons in product UI. No filled/duotone icon styles — outline
  only, to stay consistent with the 1px-border, content-first look.
- **No PNG icons.** Everything is inline SVG (or the Lucide component), so icons
  recolor with theme and never blur.

---

## INDEX — what's in this project

**Foundations**
- `tokens.json` — every design value as a `--ds-*` token (raw palette +
  semantic aliases, light + dark; type, spacing, radius, shadow, control,
  z-index). `tokens.css` is generated from it: link `tokens.css`, then
  `components/bundle.css`.
- `components/bundle.css` — global `:focus-visible` ring, the Checkbox/Radio/Switch focus
  target, and the reduced-motion rule, plus the motion tokens (`--ds-duration-*`,
  `--ds-ease-*`) and the `--ds-font-sans` / `--ds-font-mono` names.
- Dark mode: set `data-theme="dark"` on `<html>` or any container.

**Components** (sources in `components/src/<group>/` — React, compiled into
`components/bundle.js`, exposed on `window.MattSDesignSystem_522a7a`)
- `forms/` — **Button, Input, Textarea, Select, Checkbox, Radio, Switch**
- `feedback/` — **Card, Badge, Alert, Avatar, EmptyState, Skeleton, Toast, ToastRegion**
- `navigation/` — **Tabs**
- `overlay/` — **Modal, Menu, Tooltip**
- `data/` — **DataTable, Toolbar, StatTile, BarChart, ColumnChart, Sparkline** (the charts share `chartParts.jsx`, which is not a component and has no .d.ts; import the four components, not it)
- `layout/` — **AppShell**

**Composites** (added 2026-10-31 for the RoleRadar management UI) are built from the same
tokens and follow the same rules: handlers you pass compose with the built-in ones, nothing
imports an icon package or a router (icons arrive as nodes; `AppShell` and `Menu` take a
`linkComponent`), and every colour pair was checked against WCAG AA in both themes. Their
layout, hover and animation rules live in `components/bundle.css`, so link it after
`tokens.css` as always. `DataTable` sorts only the page of rows it is given and says so;
numbers go in `numeric` columns (monospace, right-aligned).

**Chart components** (added 2026-11-01 for the RoleRadar insights dashboard): StatTile, BarChart (with a funnel mode), ColumnChart and Sparkline. They use one accent hue plus a de-emphasis gray (`--ds-chart-*`), 20 to 24px marks with 4px rounded data ends, hairline recessive gridlines, labels in text tokens (never the data colour), a hover and focus readout per mark, and a "Table view" button that swaps the plot for a native table. A denominator under 10 gets a visible "Small sample" note, and counts sit next to percentages.

Each component ships `components/src/<group>/<Name>.jsx` + `components/<Name>/<Name>.d.ts`
(props; all of them in `components/index.d.ts`) + `components/<Name>/README.md`
(usage) + `components/<Name>/preview.html` (live example).

**UI kit**
- `ui_kits/console/` — "Meridian," a SaaS billing console (dashboard, invoices,
  settings) demonstrating the primitives in a real layout. See its `README.md`.

**Template** (`templates/console/` — a starting point consuming projects can copy)
- `Console.dc.html` — the Meridian console as a Design Component, mounting
  `ConsoleApp.jsx` over the design system (loaded via `ds-base.js`). Shows in the
  "Templates" group of the starting-point picker.

**Specimen cards** — the Colors, Type and Spacing views now show these from
`tokens.json`; the old pages are in `archived/guidelines/*.card.html`.

**Meta**
- `assets/notes/SKILL.from-standalone.md` — makes this folder usable as a downloadable Claude skill.
- `README.md` — this file.

**Identity pass (kit version 25):** the accent ramp follows the owner's personal site (mattwalczyk.com): `--ds-bronze-50` to `--ds-bronze-900` replace blue as the accent. The neutrals are warm stone (Tailwind's stone ramp with 450, 550 and 850 half steps), the dark theme is layered one step lighter than the first dark draft (page `neutral-900`, zones `neutral-850`, cards `neutral-800`, hover and borders `neutral-700`), and the warning tone is yellow so it stays apart from the bronze accent; every status keeps an icon and a label. Type is the IBM Plex family: Plex Serif is `--ds-font-display` (used by `.ds-hero`, `.ds-page-title`, the AppShell brand and title, and the StatTile figure), Plex Sans is the body face and Plex Mono the code and number face. The info Alert and the AppShell current-page marker no longer use the accent as a wash: info is a neutral surface, and the current page carries a 2px accent rule at its left edge.
