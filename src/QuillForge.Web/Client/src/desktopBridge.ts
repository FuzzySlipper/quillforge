import type { LayoutConfig } from "./layout";
import type { TextTheme } from "./textTheme";
import type { Status } from "./types";

export const DESKTOP_SHELL_BRIDGE_TYPE = "quillforge:desktop-shell";

const THEME_VARIABLES = [
  "--color-bg",
  "--color-surface",
  "--color-surface-alt",
  "--color-text",
  "--color-text-muted",
  "--color-accent",
  "--color-accent-hover",
  "--color-input-bg",
  "--color-border",
] as const;

export interface DesktopShellBridgePayload {
  version: string | null;
  build: string | null;
  layoutName: string;
  textThemeName: string;
  cssVariables: Record<string, string>;
}

export function publishDesktopShellBridge(
  status: Status | null,
  layout: LayoutConfig,
  textTheme: TextTheme,
): void {
  if (typeof window === "undefined" || window.parent === window) {
    return;
  }

  const rootStyles = getComputedStyle(document.documentElement);
  const cssVariables = Object.fromEntries(
    THEME_VARIABLES.map((name) => [name, rootStyles.getPropertyValue(name).trim()]),
  );

  const payload: DesktopShellBridgePayload = {
    version: status?.version ?? null,
    build: status?.build ?? null,
    layoutName: layout.name,
    textThemeName: textTheme.name,
    cssVariables,
  };

  window.parent.postMessage(
    {
      type: DESKTOP_SHELL_BRIDGE_TYPE,
      payload,
    },
    "*",
  );
}
