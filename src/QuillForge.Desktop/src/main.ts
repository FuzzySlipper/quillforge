import "./style.css";

import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

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
};

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
const openWorkspaceButtons = [
  document.querySelector<HTMLButtonElement>("#open-workspace"),
  document.querySelector<HTMLButtonElement>("#open-workspace-inline"),
].filter(Boolean) as HTMLButtonElement[];

let currentBackendUrl: string | null = null;
let currentBindMode = "loopback";

function isLanEnabled(next: DesktopShellStatus): boolean {
  return next.bindMode === "lan";
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

function showShellState(next: DesktopShellStatus): void {
  if (!stateBadge || !stateTitle || !stateMessage || !workspacePath || !backendPort || !bindMode || !statusPanel || !iframePanel || !iframe) {
    return;
  }

  currentBindMode = next.bindMode;
  workspacePath.textContent = next.workspacePath || "Unavailable";
  backendPort.textContent = next.port ? next.port.toString() : "Unavailable";
  bindMode.textContent = isLanEnabled(next) ? "LAN enabled" : "local only";
  renderNetworkInfo(next);

  switch (next.phase) {
    case "ready":
      stateBadge.textContent = "Ready";
      stateTitle.textContent = "QuillForge is running";
      stateMessage.textContent = next.message ?? "The embedded QuillForge window is connected to the local backend.";
      statusPanel.classList.add("hidden");
      iframePanel.classList.remove("hidden");
      if (next.backendUrl && currentBackendUrl !== next.backendUrl) {
        currentBackendUrl = next.backendUrl;
        iframe.src = next.backendUrl;
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
      break;
    case "exited":
      stateBadge.textContent = "Backend Stopped";
      stateTitle.textContent = "The backend stopped unexpectedly";
      stateMessage.textContent = next.message ?? "You can restart the backend and reopen the workspace from here.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      iframe.src = "about:blank";
      currentBackendUrl = null;
      break;
    case "stopped":
      stateBadge.textContent = "Stopped";
      stateTitle.textContent = "QuillForge is shutting down";
      stateMessage.textContent = next.message ?? "The desktop shell is stopping the local backend.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
      break;
    default:
      stateBadge.textContent = "Starting";
      stateTitle.textContent = "Launching QuillForge backend";
      stateMessage.textContent = next.message ?? "Preparing the local QuillForge service and waiting for readiness.";
      iframePanel.classList.add("hidden");
      statusPanel.classList.remove("hidden");
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

retryButton?.addEventListener("click", () => {
  void restartBackend();
});

restartButton?.addEventListener("click", () => {
  void restartBackend();
});

toggleNetworkButton?.addEventListener("click", () => {
  void setLanAccessEnabled(currentBindMode !== "lan");
});

for (const button of openWorkspaceButtons) {
  button.addEventListener("click", () => {
    void openWorkspace();
  });
}

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
    });
  });
