import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/_index.tsx"),
  route("auth/callback", "routes/auth.callback.tsx"),
  route("auth/logout", "routes/auth.logout.tsx"),
  route("not-authorized", "routes/not-authorized.tsx"),
  route("service-unavailable", "routes/service-unavailable.tsx"),
  route("postings", "routes/postings._index.tsx"),
  route("postings/:id", "routes/postings.$id.tsx"),
] satisfies RouteConfig;
