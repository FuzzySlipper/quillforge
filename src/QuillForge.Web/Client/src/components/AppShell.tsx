import type { ReactNode } from "react";
import ShellIcon from "./ShellIcon";

interface AppShellProps {
  rail: ReactNode;
  inspector: ReactNode;
  footer: ReactNode;
  children: ReactNode;
  inspectorOpen: boolean;
  onToggleInspector: () => void;
  backgroundImage?: string | null;
}

export default function AppShell({
  rail,
  inspector,
  footer,
  children,
  inspectorOpen,
  onToggleInspector,
  backgroundImage,
}: AppShellProps) {
  const workspaceStyle = backgroundImage
    ? {
        backgroundImage: `linear-gradient(180deg, rgba(7, 7, 10, 0.74), rgba(17, 15, 20, 0.62)), url(${encodeURI(backgroundImage)})`,
      }
    : undefined;

  return (
    <div className="qf-theme-shell qf-app-shell" data-theme="dark">
      {rail}

      <div className="qf-app-main">
        <div className="qf-shell-stage" data-inspector-open={inspectorOpen}>
          <main
            className={`qf-shell-workspace${backgroundImage ? " has-background" : ""}`}
            style={workspaceStyle}
          >
            {children}
          </main>

          <aside className="qf-shell-inspector">
            <button
              type="button"
              className="qf-shell-inspector-handle"
              title={inspectorOpen ? "Collapse inspector" : "Open inspector"}
              onClick={onToggleInspector}
            >
              <ShellIcon
                name={inspectorOpen ? "chevron-right" : "chevron-left"}
                className="h-4 w-4"
              />
            </button>

            <div className="qf-shell-inspector-body">
              {inspector}
            </div>
          </aside>
        </div>

        <footer className="qf-shell-footer">
          {footer}
        </footer>
      </div>
    </div>
  );
}
