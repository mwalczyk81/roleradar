**AppShell** — page frame with sidebar navigation, a top bar carrying the page title, and the main area.

```jsx
<AppShell
  brand="RoleRadar"
  title="Postings"
  linkComponent={NavLink}
  navItems={[
    { label: "Postings", href: "/postings", active: true, icon: <ListIcon /> },
    { label: "Companies", href: "/companies" },
  ]}
  theme={theme}
  onThemeToggle={toggleTheme}
  userLabel="owner"
  signOut={<form method="post" action="/auth/logout"><Button variant="ghost" type="submit">Sign out</Button></form>}
>
  {page}
</AppShell>
```

`title` becomes the page's `h1`. The kit imports no icon package and no router: icons arrive as nodes, and `linkComponent` takes a router Link or NavLink (it receives `href` and `to`); the default is a plain anchor. Mark the current page with `active` (sets `aria-current="page"`).

A skip link jumps to the main area. Below 48rem the sidebar folds behind a menu button (Escape closes it). The theme button appears when `onThemeToggle` is set; it only reports the click, so the app owns the theme. `signOut` is a node so the app decides how sign-out is submitted (a form with a CSRF token, for example).

Props: `brand`, `navItems`, `linkComponent`, `title`, `userLabel`, `signOut`, `theme`, `onThemeToggle`, `headerActions`, `children`.
