# Interface Contract: Design System Components & Composites

**Feature**: `005b-management-ui`  
**Date**: 2026-10-31  
**Status**: Completed  

All UI screens must be composed from Matt's Design System tokens and components vendored under `web/app/ds`. The vendored copy is strictly copy-only and never edited by hand.

---

## 1. Design System Structure & TypeScript Integration (`web/app/ds`)

The layout under `web/app/ds` mirrors the upstream design kit artifact exactly. There is **no `base.css`** (global focus rings, reduced motion, and control focus styles are compiled directly into `components/bundle.css`), and there are **no separate `primitives/` or `composites/` folders**. Both primitives and composites live alongside each other in `components/src/<group>/*.jsx` with matching TypeScript declarations in `components/<Name>/<Name>.d.ts` and `components/index.d.ts`.

```text
web/app/ds/
├── SYNC.md                    # Upstream artifact SHA, license, copy-only expectations
├── README.md                  # Brand book & design kit guide
├── tokens.css                 # CSS custom properties (--ds-*) for light and dark themes
├── tokens.json                # Token source of truth (colors, typography, spacing, radius)
├── fonts/                     # Font assets referenced by tokens.css
│   ├── inter-latin-wght-normal.woff2
│   └── jetbrains-mono-latin-wght-normal.woff2
└── components/
    ├── bundle.css             # Focus ring, reduced motion, component structural CSS
    ├── index.d.ts             # Unified exported prop types
    ├── <Name>/                # Per-component types and READMEs (e.g. Button, DataTable)
    │   ├── <Name>.d.ts        # TypeScript interface definitions
    │   └── README.md          # Component usage documentation
    └── src/                   # Component sources (React JSX) grouped by category
        ├── data/              # DataTable.jsx, Toolbar.jsx
        ├── feedback/          # Alert.jsx, Avatar.jsx, Badge.jsx, Card.jsx, EmptyState.jsx, Skeleton.jsx, Toast.jsx, ToastRegion.jsx
        ├── forms/             # Button.jsx, Checkbox.jsx, Input.jsx, Radio.jsx, Select.jsx, Switch.jsx, Textarea.jsx
        ├── layout/            # AppShell.jsx
        ├── navigation/        # Tabs.jsx
        └── overlay/           # Menu.jsx, Modal.jsx, Tooltip.jsx
```

### TypeScript & Linter Configuration Rules
1. **`allowJs: true`**: In `web/tsconfig.json`, `allowJs: true` and `checkJs: false` are configured so that the vendored `.jsx` files are ingested via their corresponding `.d.ts` declarations without erroring on internal untyped JavaScript.
2. **Strict App Typing**: All application code under `web/app/**` outside `web/app/ds/**` is authored in strictly typed TypeScript (`strict: true`, `noImplicitAny: true`).
3. **Lint Exclusion**: Stylelint and ESLint rules forbidding raw hex colors (`#[0-9a-fA-F]{3,6}`) and explicit pixel dimensions (`px`) explicitly exclude `web/app/ds/**`. The application code outside `web/app/ds` must consume `--ds-*` CSS custom properties exclusively.

---

## 2. Part C Composite Component Specifications

Composites are published in the kit as `.jsx` with `.d.ts` types and arrive via re-sync; the web app imports them directly from `web/app/ds/components/src`.

> **Key Invariants for Composites**:
> - **Zero Icon Library Dependencies**: Composite components do **not** import `lucide-react`. Any icons arrive strictly as React node props (e.g. `icon?: React.ReactNode`) or child elements passed by the consuming application.
> - **Event Handler Composition**: Interactive components (`Toast`, `Menu`, etc.) follow the kit's existing handler-composition rules: consumer handlers passed in props are composed with internal handlers (consumer handler executes first, followed by internal state updates), ensuring `{...rest}` never clobbers active handlers.

### 1. `DataTable<T>`
- **Source**: `web/app/ds/components/src/data/DataTable.jsx` (types: `DataTable.d.ts`)
- **Props**:
  - `columns`: Array of `{ key: string; header: string; sortable?: boolean; align?: 'left' | 'right' | 'center'; render?: (row: T) => React.ReactNode }`
  - `data`: `T[]`
  - `keyField`: keyof `T` (e.g. `'id'`)
  - `onRowClick?: (row: T) => void`
  - `isLoading?: boolean`
  - `emptyMessage?: string`
  - `sortColumn?: string`
  - `sortDirection?: 'asc' | 'desc'`
  - `onSortChange?: (column: string, direction: 'asc' | 'desc') => void`
  - `pagination?: { total: number; limit: number; offset: number; onPageChange: (offset: number) => void }`
- **Invariants**:
  - Column sorting is explicitly page-local only and clearly labelled as such.
  - Numbers and scores are monospace (`var(--ds-font-mono)`) and right-aligned.
  - Table headers are sticky.
  - Rows have visible keyboard focus indicators (`:focus-visible`) and hover highlighting.

### 2. `Toolbar`
- **Source**: `web/app/ds/components/src/data/Toolbar.jsx` (types: `Toolbar.d.ts`)
- **Props**:
  - `searchQuery?: string`
  - `onSearchChange?: (q: string) => void`
  - `searchPlaceholder?: string`
  - `filters?: Array<{ id: string; label: string; value: string; onRemove: () => void }>`
  - `actions?: React.ReactNode`
- **Invariants**:
  - Search input debounces input events before notifying `onSearchChange`.
  - Filter chips are dismissible via keyboard (`Backspace` or `Delete`) or click.
  - Zero icon library imports; search and clear icons arrive via props or child slots.

### 3. `AppShell`
- **Source**: `web/app/ds/components/src/layout/AppShell.jsx` (types: `AppShell.d.ts`)
- **Props**:
  - `brand?: React.ReactNode`
  - `title?: React.ReactNode`
  - `userLabel?: string`
  - `signOut?: React.ReactNode`
  - `theme?: 'light' | 'dark'`
  - `onThemeToggle?: () => void`
  - `headerActions?: React.ReactNode`
  - `navItems?: Array<{ label: string; href: string; icon?: React.ReactNode; active?: boolean; badge?: React.ReactNode }>`
  - `linkComponent?: React.ElementType`
  - `children?: React.ReactNode`
- **Invariants**:
  - Does not hardcode route paths; navigation links are supplied via the `navItems` array.
  - Defaults `linkComponent` to a plain anchor (`<a>`), but accepts React Router's `<NavLink>` or `<Link>` from the consuming application.
  - Displays `title` as page `<h1>`, `userLabel` as user badge, `signOut` slot, and `headerActions` in top bar.
  - Responsive sidebar with collapse toggle.
  - Skip-to-main-content link for keyboard accessibility (WCAG 2.4.1).

### 4. `EmptyState`
- **Source**: `web/app/ds/components/src/feedback/EmptyState.jsx` (types: `EmptyState.d.ts`)
- **Props**:
  - `icon?: React.ReactNode`
  - `title: string`
  - `description: string`
  - `action?: { label: string; onClick?: () => void; href?: string }`
- **Invariants**:
  - Clear heading hierarchy (`h2` or `h3`).
  - High contrast (>= 4.5:1 text on background).
  - Accepts icon as a React node; does not import `lucide-react`.

### 5. `Skeleton`
- **Source**: `web/app/ds/components/src/feedback/Skeleton.jsx` (types: `Skeleton.d.ts`)
- **Props**:
  - `variant: 'table' | 'card' | 'text'`
  - `rows?: number`
- **Invariants**:
  - Respects `prefers-reduced-motion` (disables shimmer animation if requested).
  - Maintains semantic layout dimensions to prevent cumulative layout shift (CLS).

### 6. `Toast` & `ToastRegion`
- **Source**: `web/app/ds/components/src/feedback/Toast.jsx` and `ToastRegion.jsx` (types: `Toast.d.ts`, `ToastRegion.d.ts`)
- **Props**:
  - `variant: 'success' | 'warning' | 'danger' | 'info'`
  - `title: string`
  - `message?: string`
  - `action?: { label: string; onClick: () => void }`
  - `onDismiss: () => void`
- **Invariants**:
  - Follows kit handler-composition rules: custom `onClick` or `onDismiss` handlers compose cleanly with internal timers.
  - Auto-dismissible after 5 seconds unless containing an action (such as 409 reload).
  - Includes `role="alert"` for danger/warning and `role="status"` for success/info.

### 7. `Menu` (Dropdown)
- **Source**: `web/app/ds/components/src/overlay/Menu.jsx` (types: `Menu.d.ts`)
- **Props**:
  - `trigger: React.ReactNode`
  - `items: Array<{ label: string; onClick?: () => void; href?: string; icon?: React.ReactNode; danger?: boolean; disabled?: boolean }>`
- **Invariants**:
  - Follows kit handler-composition rules (`...rest` before internal click handlers).
  - Arrow key navigation (`ArrowDown`, `ArrowUp`, `Home`, `End`).
  - Escape closes menu and restores focus to trigger element.

### 8. `Tooltip`
- **Source**: `web/app/ds/components/src/overlay/Tooltip.jsx` (types: `Tooltip.d.ts`)
- **Props**:
  - `content: string`
  - `children: React.ReactElement`
- **Invariants**:
  - Appears on both hover and keyboard `:focus-visible`.
  - Dismissible via `Escape`.

---

## 3. Token & Accessibility Invariants

1. **Tokens Only**: Components consume CSS variables exclusively (`var(--ds-surface)`, `var(--ds-text)`, `var(--ds-focus-ring)`, `var(--ds-primary-solid)`, `var(--ds-danger-solid)`, etc.).
2. **Focus Visibility**: All interactive elements display a 2px solid ring (`var(--ds-focus-ring)`) with 2px offset on keyboard navigation.
3. **Contrast**: Text elements satisfy >= 4.5:1; UI control boundaries satisfy >= 3:1 in both light and dark themes.
4. **Reduced Motion**: All animations and transitions disable or simplify under `@media (prefers-reduced-motion: reduce)`.
