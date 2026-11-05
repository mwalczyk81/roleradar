import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/_index.tsx"),
  route("auth/callback", "routes/auth.callback.tsx"),
  route("auth/logout", "routes/auth.logout.tsx"),
  route("not-authorized", "routes/not-authorized.tsx"),
  route("service-unavailable", "routes/service-unavailable.tsx"),
  route("postings", "routes/postings._index.tsx"),
  route("postings/:id", "routes/postings.$id.tsx"),
  route("companies", "routes/companies._index.tsx"),
  route("companies/:id", "routes/companies.$id.tsx"),
  route("applications", "routes/applications._index.tsx"),
  route("applications/:id", "routes/applications.$id.tsx"),
  route("criteria", "routes/criteria.tsx"),
  route("notes", "routes/notes._index.tsx"),
  route("notes/:id", "routes/notes.$id.tsx"),
  route("search-runs", "routes/search-runs.tsx"),
  route("insights", "routes/insights.tsx"),
] satisfies RouteConfig;
