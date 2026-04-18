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
const openWorkspaceButtons = [
  document.querySelector<HTMLButtonElement>("#open-workspace"),
  document.querySelector<HTMLButtonElement>("#open-workspace-inline"),
].filter(Boolean) as HTMLButtonElement[];

let currentBackendUrl: string | null = null;

function showShellState(next: DesktopShellStatus): void {
  if (!stateBadge || !stateTitle || !stateMessage || !workspacePath || !backendPort || !bindMode || !statusPanel || !iframePanel || !iframe) {
    return;
  }

  workspacePath.textContent = next.workspacePath || "Unavailable";
  backendPort.textContent = next.port ? next.port.toString() : "Unavailable";
  bindMode.textContent = next.bindMode;

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
}

async function restartBackend(): Promise<void> {
  await invoke("restart_backend");
}

async function openWorkspace(): Promise<void> {
  await invoke("open_workspace");
}

retryButton?.addEventListener("click", () => {
  void restartBackend();
});

restartButton?.addEventListener("click", () => {
  void restartBackend();
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
      restartAvailable: true,
    });
  });
