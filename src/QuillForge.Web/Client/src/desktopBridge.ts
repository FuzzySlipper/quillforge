import type { LayoutConfig } from "./layout";
import type { TextTheme } from "./textTheme";
import type { Status } from "./types";

export const DESKTOP_SHELL_BRIDGE_TYPE = "quillforge:desktop-shell";

const THEME_VARIABLES = [
  "--qf-rail",
  "--qf-rail-edge",
  "--qf-paper",
  "--qf-paper-deep",
  "--qf-paper-soft",
  "--qf-rule",
  "--qf-rule-soft",
  "--qf-ink",
  "--qf-ink-2",
  "--qf-ink-3",
  "--qf-ink-4",
  "--qf-ochre",
  "--qf-ochre-deep",
  "--qf-leaf",
  "--qf-claret",
  "--qf-sky",
  "--qf-surface-app",
  "--qf-surface-workspace",
  "--qf-surface-workspace-deep",
  "--qf-surface-chrome",
  "--qf-surface-chrome-strong",
  "--qf-surface-card",
  "--qf-surface-card-muted",
  "--qf-surface-card-sunken",
  "--qf-surface-input",
  "--qf-surface-input-hover",
  "--qf-surface-overlay",
  "--qf-overlay-contrast",
  "--qf-surface-scrim-start",
  "--qf-surface-scrim-end",
  "--qf-surface-elevated",
  "--qf-text-primary",
  "--qf-text-secondary",
  "--qf-text-muted",
  "--qf-text-disabled",
  "--qf-border",
  "--qf-border-muted",
  "--qf-border-strong",
  "--qf-focus-ring",
  "--qf-focus-shadow",
  "--qf-accent",
  "--qf-accent-hover",
  "--qf-accent-soft",
  "--qf-accent-border",
  "--qf-accent-contrast",
  "--qf-action-primary-bg",
  "--qf-action-primary-bg-hover",
  "--qf-action-primary-text",
  "--qf-action-secondary-bg",
  "--qf-action-secondary-bg-hover",
  "--qf-danger",
  "--qf-danger-soft",
  "--qf-danger-border",
  "--qf-danger-text",
  "--qf-success",
  "--qf-success-soft",
  "--qf-warning",
  "--qf-warning-soft",
  "--qf-warning-text",
  "--qf-info",
  "--qf-info-soft",
  "--qf-info-border",
  "--qf-info-text",
  "--qf-disabled-opacity",
  "--qf-panel-shadow",
  "--qf-button-shadow",
  "--qf-inset-highlight",
  "--color-bg",
  "--color-surface",
  "--color-surface-alt",
  "--color-text",
  "--color-text-muted",
  "--color-accent",
  "--color-accent-hover",
  "--color-accent-contrast",
  "--color-input-bg",
  "--color-border",
  "--color-overlay",
  "--color-overlay-contrast",
  "--color-danger",
  "--color-danger-soft",
  "--color-danger-border",
  "--color-danger-text",
  "--color-danger-strong",
  "--color-danger-strong-hover",
  "--color-success",
  "--color-success-soft",
  "--color-success-strong",
  "--color-success-strong-hover",
  "--color-warning",
  "--color-warning-soft",
  "--color-warning-text",
  "--color-info",
  "--color-info-soft",
  "--color-info-border",
  "--color-info-text",
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

  const shellElement = document.querySelector<HTMLElement>(".qf-theme-shell") ?? document.documentElement;
  const shellStyles = getComputedStyle(shellElement);
  const cssVariables = Object.fromEntries(
    THEME_VARIABLES.map((name) => [name, shellStyles.getPropertyValue(name).trim()]),
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
