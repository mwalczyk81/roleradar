import { EmptyState } from "../ui.js";
import { ServerOff } from "lucide-react";

export default function ServiceUnavailable() {
  return (
    <main
      className="status-page status-page-empty"
    >
      <EmptyState
        icon={<ServerOff size={28} />}
        title="Service Unavailable"
        description="The backend database or API services are offline or unreachable. Start or restart the local development stack with the following command:"
        action={
          <div
            className="stack-centered"
          >
            <code
              className="command-snippet"
            >
              pwsh ./scripts/start-services.ps1
            </code>
          </div>
        }
        secondaryAction={null}
      />
    </main>
  );
}
