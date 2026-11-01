import type { LoaderFunctionArgs } from "react-router";
import { requireUserSession } from "../lib/auth-guard.server.js";

export async function loader({ request }: LoaderFunctionArgs) {
  await requireUserSession(request);

  return new Response(null, {
    status: 302,
    headers: {
      Location: "/postings",
    },
  });
}

export default function Index() {
  return null;
}
