/// <reference types="vite/client" />

import type { DesktopShellStatus } from "./types";

declare global {
  interface Window {
    quillforgeDesktop?: {
      getStatus: () => Promise<DesktopShellStatus>;
      restartBackend: () => Promise<{ success: boolean }>;
      setLanAccess: (enabled: boolean) => Promise<{ success: boolean }>;
      openWorkspace: () => Promise<{ success: boolean }>;
      openUrl: (url: string) => Promise<{ success: boolean; error?: string }>;
      onStatusUpdate: (callback: (status: DesktopShellStatus) => void) => void;
      installUpdate: () => Promise<{ success: boolean }>;
      onUpdateStatus: (callback: (data: { status: string; version: string }) => void) => void;
      onUpdateProgress: (callback: (data: { percent: number; bytesPerSecond: number }) => void) => void;
    };
  }
}

export {};
