import { useEffect, useState, type ReactNode } from "react";
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

type ShellTheme = "dark" | "light";

function getPreferredShellTheme(): ShellTheme {
  if (typeof window === "undefined") {
    return "dark";
  }

  return window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
}

function resolveBackgroundImage(backgroundImage?: string | null): string | null {
  if (!backgroundImage || typeof window === "undefined") {
    return null;
  }

  try {
    return new URL(backgroundImage, window.location.origin).href;
  } catch {
    return null;
  }
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
  const [theme, setTheme] = useState<ShellTheme>(getPreferredShellTheme);
  const safeBackgroundImage = resolveBackgroundImage(backgroundImage);
  const workspaceStyle = safeBackgroundImage
    ? {
        backgroundImage: `linear-gradient(180deg, color-mix(in srgb, var(--qf-surface-overlay) 86%, transparent), color-mix(in srgb, var(--qf-surface-overlay) 62%, transparent)), url(${JSON.stringify(safeBackgroundImage)})`,
      }
    : undefined;

  useEffect(() => {
    if (typeof window === "undefined") {
      return undefined;
    }

    const media = window.matchMedia("(prefers-color-scheme: light)");
    const handleChange = (event: MediaQueryListEvent) => {
      setTheme(event.matches ? "light" : "dark");
    };

    if (typeof media.addEventListener === "function") {
      media.addEventListener("change", handleChange);
      return () => media.removeEventListener("change", handleChange);
    }

    media.addListener(handleChange);
    return () => media.removeListener(handleChange);
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  return (
    <div className="qf-theme-shell qf-app-shell" data-theme={theme}>
      {rail}

      <div className="qf-app-main">
        <div className="qf-shell-stage" data-inspector-open={inspectorOpen}>
          <main
            className={`qf-shell-workspace${safeBackgroundImage ? " has-background" : ""}`}
            style={workspaceStyle}
          >
            {children}
          </main>

          <aside className="qf-shell-inspector">
            <button
              type="button"
              className="qf-shell-inspector-handle"
              title={inspectorOpen ? "Collapse inspector" : "Open inspector"}
              aria-label={inspectorOpen ? "Collapse inspector" : "Open inspector"}
              aria-expanded={inspectorOpen}
              onClick={onToggleInspector}
            >
              <ShellIcon
                name={inspectorOpen ? "chevron-right" : "chevron-left"}
                className="h-4 w-4"
              />
              <span className="qf-shell-inspector-handle-label">Inspector</span>
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
