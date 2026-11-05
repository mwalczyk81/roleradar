export interface AppShellNavItem {
  label: string;
  href: string;
  active: boolean;
}

export function getNavItems(pathname: string): AppShellNavItem[] {
  return [
    { label: "Postings", href: "/postings", active: pathname.startsWith("/postings") },
    { label: "Companies", href: "/companies", active: pathname.startsWith("/companies") },
    { label: "Applications", href: "/applications", active: pathname.startsWith("/applications") },
    { label: "Criteria", href: "/criteria", active: pathname.startsWith("/criteria") },
    { label: "Notes", href: "/notes", active: pathname.startsWith("/notes") },
    { label: "Search Runs", href: "/search-runs", active: pathname.startsWith("/search-runs") },
    { label: "Insights", href: "/insights", active: pathname.startsWith("/insights") },
  ];
}
