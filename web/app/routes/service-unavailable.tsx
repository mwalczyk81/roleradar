import { EmptyState } from "../ui.js";
import { ServerOff } from "lucide-react";

export default function ServiceUnavailable() {
  return (
    <main
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "100vh",
        backgroundColor: "var(--ds-surface)",
        padding: "var(--ds-space-6)",
      }}
    >
      <EmptyState
        icon={<ServerOff size={28} />}
        title="Service Unavailable"
        description="The backend database or API services are offline or unreachable. Start or restart the local development stack with the following command:"
        action={
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              gap: "var(--ds-space-3)",
            }}
          >
            <code
              style={{
                display: "inline-block",
                padding: "var(--ds-space-2) var(--ds-space-4)",
                backgroundColor: "var(--ds-surface-hover)",
                color: "var(--ds-text)",
                borderRadius: "var(--ds-radius-md)",
                fontFamily: "var(--ds-font-mono)",
                fontSize: "var(--ds-text-sm)",
                border: "thin solid var(--ds-border)",
              }}
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
