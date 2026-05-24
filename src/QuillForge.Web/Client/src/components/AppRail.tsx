import { MODE_ICON_PATHS, MODE_LABELS } from "../modePresentation";
import type { Mode, Status } from "../types";
import ShellIcon from "./ShellIcon";

interface AppRailProps {
  status: Status | null;
  mode: Mode;
  inspectorOpen: boolean;
  onOpenMode: () => void;
  onNewSession: () => void;
  onOpenSessions: () => void;
  onToggleInspector: () => void;
  onOpenProfile: () => void;
  onOpenProviders: () => void;
  onOpenAppSettings: () => void;
  onOpenTextTheme: () => void;
  onOpenDocs: () => void;
  onOpenTour: () => void;
}

interface RailButtonProps {
  active?: boolean;
  title: string;
  onClick: () => void;
  children: React.ReactNode;
}

function RailButton({ active, title, onClick, children }: RailButtonProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={`qf-shell-rail-button${active ? " is-active" : ""}`}
    >
      {children}
    </button>
  );
}

export default function AppRail({
  status,
  mode,
  inspectorOpen,
  onOpenMode,
  onNewSession,
  onOpenSessions,
  onToggleInspector,
  onOpenProfile,
  onOpenProviders,
  onOpenAppSettings,
  onOpenTextTheme,
  onOpenDocs,
  onOpenTour,
}: AppRailProps) {
  const modeLabel = MODE_LABELS[mode];

  return (
    <aside className="qf-shell-rail">
      <div className="qf-shell-rail-group">
        <div className="qf-shell-rail-brand" title="QuillForge">
          Q
        </div>

        <RailButton
          active
          title={`Mode menu · ${modeLabel}${status?.project ? ` / ${status.project}` : ""}`}
          onClick={onOpenMode}
        >
          <img src={MODE_ICON_PATHS[mode]} alt="" />
        </RailButton>

        <RailButton title="New session" onClick={onNewSession}>
          <ShellIcon name="plus" />
        </RailButton>

        <RailButton title="Saved sessions" onClick={onOpenSessions}>
          <ShellIcon name="stack" />
        </RailButton>

        <RailButton
          active={inspectorOpen}
          title={inspectorOpen ? "Close workspace inspector" : "Open workspace inspector"}
          onClick={onToggleInspector}
        >
          <ShellIcon name="panel" />
        </RailButton>
      </div>

      <div className="qf-shell-rail-group">
        <RailButton title="Profile" onClick={onOpenProfile}>
          <ShellIcon name="user" />
        </RailButton>

        <RailButton title="Providers" onClick={onOpenProviders}>
          <ShellIcon name="settings" />
        </RailButton>

        <RailButton title="App settings" onClick={onOpenAppSettings}>
          <ShellIcon name="sliders" />
        </RailButton>

        <RailButton title="Text theme" onClick={onOpenTextTheme}>
          <ShellIcon name="palette" />
        </RailButton>

        <RailButton title="Documentation" onClick={onOpenDocs}>
          <ShellIcon name="book" />
        </RailButton>

        <RailButton title="Interactive Tour" onClick={onOpenTour}>
          <ShellIcon name="compass" />
        </RailButton>
      </div>
    </aside>
  );
}
