import "./style.css";

import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

type DesktopDiagnosticEntry = {
  level: string;
  source: string;
  message: string;
};

type DesktopShellStatus = {
  phase: "starting" | "ready" | "failed" | "exited" | "stopped";
  message: string | null;
  backendUrl: string | null;
  workspacePath: string;
  port: number | null;
  bindMode: string;
  loopbackUrl: string | null;
  lanUrl: string | null;
  restartAvailable: boolean;
  diagnostics: DesktopDiagnosticEntry[];
};

type DesktopShellBridgePayload = {
  version: string | null;
  build: string | null;
  layoutName: string;
  textThemeName: string;
  cssVariables: Record<string, string>;
};

type DesktopShellBridgeMessage = {
  type: "quillforge:desktop-shell";
  payload: DesktopShellBridgePayload;
};

const DESKTOP_SHELL_QUERY_PARAM = "desktop-shell";
const DESKTOP_SHELL_BRIDGE_TYPE = "quillforge:desktop-shell";
const DESKTOP_CONTROLS_VISIBILITY_KEY = "quillforge-desktop-top-bar-visible";
const CONSOLE_VISIBILITY_KEY = "quillforge-desktop-console-visible";
const MIRRORED_THEME_VARIABLES = [
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

const versionBadge = document.querySelector<HTMLElement>("#desktop-version");
const buildBadge = document.querySelector<HTMLElement>("#desktop-build");
const toggleTopChromeButton = document.querySelector<HTMLButtonElement>("#toggle-top-chrome");
const toggleConsoleButton = document.querySelector<HTMLButtonElement>("#toggle-console");
const topChromePanel = document.querySelector<HTMLElement>("#desktop-chrome-panel");
const stateBadge = document.querySelector<HTMLParagraphElement>("#status-badge");
const stateTitle = document.querySelector<HTMLHeadingElement>("#status-title");
const stateMessage = document.querySelector<HTMLParagraphElement>("#status-message");
const workspacePath = document.querySelector<HTMLElement>("#workspace-path");
const backendPort = document.querySelector<HTMLElement>("#backend-port");
const bindMode = document.querySelector<HTMLElement>("#bind-mode");
const statusPanel = document.querySelector<HTMLElement>("#status-panel");
const iframePanel = document.querySelector<HTMLElement>("#iframe-panel");
const iframe = document.querySelector<HTMLIFrameElement>("#backend-frame");
const retryButton = document.querySelector<HTMLButtonElement>("#retry-backend");
const restartButton = document.querySelector<HTMLButtonElement>("#restart-backend");
const toggleNetworkButton = document.querySelector<HTMLButtonElement>("#toggle-network-access");
const networkTitle = document.querySelector<HTMLHeadingElement>("#network-title");
const networkMessage = document.querySelector<HTMLParagraphElement>("#network-message");
const loopbackUrl = document.querySelector<HTMLAnchorElement>("#loopback-url");
const lanUrlCard = document.querySelector<HTMLElement>("#lan-url-card");
const lanUrl = document.querySelector<HTMLAnchorElement>("#lan-url");
const statusDiagnosticsBlock = document.querySelector<HTMLElement>("#status-diagnostics-block");
const iframeDiagnosticsDock = document.querySelector<HTMLElement>("#iframe-diagnostics-dock");
const diagnosticsOutputs = [
  document.querySelector<HTMLElement>("#status-diagnostics-output"),
  document.querySelector<HTMLElement>("#iframe-diagnostics-output"),
].filter(Boolean) as HTMLElement[];
const openWorkspaceButtons = [
  document.querySelector<HTMLButtonElement>("#open-workspace"),
  document.querySelector<HTMLButtonElement>("#open-workspace-inline"),
].filter(Boolean) as HTMLButtonElement[];

let currentBackendUrl: string | null = null;
let currentBindMode = "loopback";
let desktopControlsVisible = readBooleanPreference(DESKTOP_CONTROLS_VISIBILITY_KEY, true);
let consoleVisible = readBooleanPreference(CONSOLE_VISIBILITY_KEY, true);
let embeddedShellPayload: DesktopShellBridgePayload | null = null;

function readBooleanPreference(key: string, fallback: boolean): boolean {
  const rawValue = window.localStorage.getItem(key);
  if (rawValue === null) {
    return fallback;
  }

  return rawValue === "true";
}

function writeBooleanPreference(key: string, value: boolean): void {
  window.localStorage.setItem(key, String(value));
}

function isLanEnabled(next: DesktopShellStatus): boolean {
  return next.bindMode === "lan";
}

function extractBuildLabel(build: string | null, version: string | null): string | null {
  if (!build || (version && build === version)) {
    return null;
  }

  const [, suffix] = build.split("+", 2);
  return suffix ?? build;
}

function renderEmbeddedMetadata(): void {
  if (versionBadge) {
    versionBadge.textContent = embeddedShellPayload?.version
      ? `v${embeddedShellPayload.version}`
      : "Version pending";
  }

  if (!buildBadge) {
    return;
  }

  const buildLabel = extractBuildLabel(
    embeddedShellPayload?.build ?? null,
    embeddedShellPayload?.version ?? null,
  );

  if (!buildLabel) {
    buildBadge.classList.add("hidden");
    buildBadge.textContent = "Waiting for backend";
    return;
  }

  buildBadge.textContent = buildLabel;
  buildBadge.classList.remove("hidden");
}

function applyEmbeddedTheme(payload: DesktopShellBridgePayload | null): void {
  const root = document.documentElement;

  for (const variableName of MIRRORED_THEME_VARIABLES) {
    const nextValue = payload?.cssVariables?.[variableName]?.trim();
    if (nextValue) {
      root.style.setProperty(variableName, nextValue);
    } else {
      root.style.removeProperty(variableName);
    }
  }
}

function resetEmbeddedShellContext(): void {
  embeddedShellPayload = null;
  renderEmbeddedMetadata();
  applyEmbeddedTheme(null);
}

function renderVisibilityControls(): void {
  if (toggleTopChromeButton) {
    toggleTopChromeButton.textContent = desktopControlsVisible
      ? "Hide Desktop Controls"
      : "Show Desktop Controls";
    toggleTopChromeButton.setAttribute("aria-pressed", String(desktopControlsVisible));
  }

  if (toggleConsoleButton) {
    toggleConsoleButton.textContent = consoleVisible ? "Hide Console" : "Show Console";
    toggleConsoleButton.setAttribute("aria-pressed", String(consoleVisible));
  }

  topChromePanel?.classList.toggle("hidden", !desktopControlsVisible);
  statusDiagnosticsBlock?.classList.toggle("hidden", !consoleVisible);
  iframeDiagnosticsDock?.classList.toggle("hidden", !consoleVisible);
}

function renderNetworkInfo(next: DesktopShellStatus): void {
  if (!networkTitle || !networkMessage || !loopbackUrl || !lanUrlCard || !lanUrl) {
    return;
  }

  const lanEnabled = isLanEnabled(next);
  const resolvedLoopbackUrl = next.loopbackUrl ?? (next.port ? `http://127.0.0.1:${next.port}` : "http://127.0.0.1");
  loopbackUrl.href = resolvedLoopbackUrl;
  loopbackUrl.textContent = resolvedLoopbackUrl;

  if (lanEnabled) {
    networkTitle.textContent = "LAN/mobile access is enabled";
    if (next.lanUrl) {
      networkMessage.textContent = "Anyone on your trusted local network can open QuillForge with the address below while this stays enabled.";
      lanUrl.href = next.lanUrl;
      lanUrl.textContent = next.lanUrl;
      lanUrlCard.classList.remove("hidden");
    } else {
      networkMessage.textContent = "LAN/mobile access is enabled, but QuillForge could not detect a non-loopback address yet.";
      lanUrl.removeAttribute("href");
      lanUrl.textContent = "No LAN address detected";
      lanUrlCard.classList.remove("hidden");
    }
  } else {
    networkTitle.textContent = "Local-only mode";
    networkMessage.textContent = "Only this computer can open QuillForge right now. Enable LAN access when you want to use a phone or tablet on the same network.";
    lanUrlCard.classList.add("hidden");
    lanUrl.removeAttribute("href");
    lanUrl.textContent = "";
  }
}

function renderDiagnostics(next: DesktopShellStatus): void {
  for (const output of diagnosticsOutputs) {
    output.replaceChildren();

    if (next.diagnostics.length === 0) {
      const empty = document.createElement("p");
      empty.className = "diagnostics-empty";
      empty.textContent = "No diagnostics yet. Backend output will appear here.";
      output.append(empty);
      continue;
    }

    for (const entry of next.diagnostics) {
      const line = document.createElement("div");
      line.className = `diagnostic-line diagnostic-${entry.level}`;

      const meta = document.createElement("span");
      meta.className = "diagnostic-meta";
      meta.textContent = `[${entry.source}]`;

      const message = document.createElement("span");
      message.className = "diagnostic-message";
      message.textContent = entry.message;

      line.append(meta, message);
      output.append(line);
    }

    output.scrollTop = output.scrollHeight;
  }
}

function buildEmbeddedUrl(backendUrl: string): string {
  const url = new URL(backendUrl);
  url.searchParams.set(DESKTOP_SHELL_QUERY_PARAM, "1");
  return url.toString();
}

function getExpectedBackendOrigin(): string | null {
  if (!currentBackendUrl) {
    return null;
  }

  try {
    return new URL(currentBackendUrl).origin;
  } catch {
    return null;
  }
}

function isDesktopShellBridgeMessage(value: unknown): value is DesktopShellBridgeMessage {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Partial<DesktopShellBridgeMessage>;
  return candidate.type === DESKTOP_SHELL_BRIDGE_TYPE && !!candidate.payload;
}

function handleEmbeddedBridgeMessage(event: MessageEvent): void {
  const expectedOrigin = getExpectedBackendOrigin();
  if (!expectedOrigin || event.origin !== expectedOrigin) {
    return;
  }

  if (!isDesktopShellBridgeMessage(event.data)) {
    return;
  }

  embeddedShellPayload = event.data.payload;
  renderEmbeddedMetadata();
  applyEmbeddedTheme(embeddedShellPayload);
}

function showShellState(next: DesktopShellStatus): void {
  if (!stateBadge || !stateTitle || !stateMessage || !workspacePath || !backendPort || !bindMode || !statusPanel || !iframePanel || !iframe) {
    return;
  }

  currentBindMode = next.bindMode;
  workspacePath.textContent = next.workspacePath || "Unavailable";
  backendPort.textContent = next.port ? next.port.toString() : "Unavailable";
  bindMode.textContent = isLanEnabled(next) ? "LAN enabled" : "local only";
  renderNetworkInfo(next);
  renderDiagnostics(next);

  switch (next.phase) {
    case "ready":
      stateBadge.textContent = "Ready";
      stateTitle.textContent = "QuillForge is running";
      stateMessage.textContent = next.message ?? "The embedded QuillForge window is connected to the local backend.";
      statusPanel.classList.add("hidden");
      iframePanel.classList.remove("hidden");
      if (next.backendUrl && currentBackendUrl !== next.backendUrl) {
        currentBackendUrl = next.backendUrl;
        resetEmbeddedShellContext();
        iframe.src = buildEmbeddedUrl(next.backendUrl);
      }
      break;
    case "failed":
      stateBadge.textContent = "Startup Failed";
      stateTitle.textContent = "QuillForge could not start";
      stateMessage.textContent = next.message ?? "The desktop shell could not start the local backend.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      iframe.src = "about:blank";
      currentBackendUrl = null;
      resetEmbeddedShellContext();
      break;
    case "exited":
      stateBadge.textContent = "Backend Stopped";
      stateTitle.textContent = "The backend stopped unexpectedly";
      stateMessage.textContent = next.message ?? "You can restart the backend and reopen the workspace from here.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      iframe.src = "about:blank";
      currentBackendUrl = null;
      resetEmbeddedShellContext();
      break;
    case "stopped":
      stateBadge.textContent = "Stopped";
      stateTitle.textContent = "QuillForge is shutting down";
      stateMessage.textContent = next.message ?? "The desktop shell is stopping the local backend.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      iframe.src = "about:blank";
      currentBackendUrl = null;
      resetEmbeddedShellContext();
      break;
    default:
      stateBadge.textContent = "Starting";
      stateTitle.textContent = "Launching QuillForge backend";
      stateMessage.textContent = next.message ?? "Preparing the local QuillForge service and waiting for readiness.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      iframe.src = "about:blank";
      currentBackendUrl = null;
      resetEmbeddedShellContext();
      break;
  }

  const canRestart = next.restartAvailable;
  if (retryButton) {
    retryButton.disabled = !canRestart;
  }
  if (restartButton) {
    restartButton.disabled = !canRestart;
  }
  if (toggleNetworkButton) {
    toggleNetworkButton.disabled = !canRestart;
    toggleNetworkButton.textContent = isLanEnabled(next) ? "Disable LAN Access" : "Enable LAN Access";
    toggleNetworkButton.setAttribute("aria-pressed", String(isLanEnabled(next)));
  }
}

async function restartBackend(): Promise<void> {
  await invoke("restart_backend");
}

async function openWorkspace(): Promise<void> {
  await invoke("open_workspace");
}

async function setLanAccessEnabled(enableLan: boolean): Promise<void> {
  await invoke("set_lan_access_enabled", { enableLan });
}

async function openExternalUrl(url: string): Promise<void> {
  await invoke("open_external_url", { url });
}

function attachExternalLink(anchor: HTMLAnchorElement | null): void {
  anchor?.addEventListener("click", (event) => {
    const url = anchor.getAttribute("href");
    if (!url) {
      return;
    }

    event.preventDefault();
    void openExternalUrl(url);
  });
}

window.addEventListener("message", handleEmbeddedBridgeMessage);
renderEmbeddedMetadata();
renderVisibilityControls();

retryButton?.addEventListener("click", () => {
  void restartBackend();
});

restartButton?.addEventListener("click", () => {
  void restartBackend();
});

toggleNetworkButton?.addEventListener("click", () => {
  void setLanAccessEnabled(currentBindMode !== "lan");
});

toggleTopChromeButton?.addEventListener("click", () => {
  desktopControlsVisible = !desktopControlsVisible;
  writeBooleanPreference(DESKTOP_CONTROLS_VISIBILITY_KEY, desktopControlsVisible);
  renderVisibilityControls();
});

toggleConsoleButton?.addEventListener("click", () => {
  consoleVisible = !consoleVisible;
  writeBooleanPreference(CONSOLE_VISIBILITY_KEY, consoleVisible);
  renderVisibilityControls();
});

for (const button of openWorkspaceButtons) {
  button.addEventListener("click", () => {
    void openWorkspace();
  });
}

attachExternalLink(loopbackUrl);
attachExternalLink(lanUrl);

void listen<DesktopShellStatus>("desktop://status", (event) => {
  showShellState(event.payload);
});

void invoke<DesktopShellStatus>("get_shell_status")
  .then(showShellState)
  .catch((error) => {
    showShellState({
      phase: "failed",
      message: `Unable to read desktop shell state: ${String(error)}`,
      backendUrl: null,
      workspacePath: "Unavailable",
      port: null,
      bindMode: "loopback",
      loopbackUrl: null,
      lanUrl: null,
      restartAvailable: true,
      diagnostics: [],
    });
  });
