import type { Status, Mode } from "../types";

interface HeaderBarProps {
  status: Status | null;
  layoutName: string;
  mode?: Mode;
  onOpenProfile: () => void;
  onOpenMode: () => void;
  onOpenContext: () => void;
  onOpenLore: () => void;
  onOpenPlots: () => void;
  onOpenPrompts: () => void;
  onOpenLayout: () => void;
  onOpenProviders: () => void;
  onOpenCouncilConfig?: () => void;
  onOpenResearch?: () => void;
  onNewSession: () => void;
  onOpenSessions: () => void;
  onOpenCharacters: () => void;
  onOpenTextTheme: () => void;
  textThemeName: string;
}

function LabeledBtn({ label, onClick, title, children }: {
  label: string; onClick: () => void; title?: string; children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col items-center gap-0.5">
      {label && <span className="text-[9px] uppercase tracking-wider text-text-muted/50 leading-none">{label}</span>}
      <button
        onClick={onClick}
        title={title}
        className="text-xs px-2 py-1 rounded-md bg-surface-alt text-text-muted hover:text-text transition-colors"
      >
        {children}
      </button>
    </div>
  );
}

export default function HeaderBar({ status, layoutName, mode, onOpenProfile, onOpenMode, onOpenContext, onOpenLore, onOpenPlots, onOpenPrompts, onOpenLayout, onOpenProviders, onOpenCouncilConfig, onOpenResearch, onNewSession, onOpenSessions, onOpenCharacters, onOpenTextTheme, textThemeName }: HeaderBarProps) {
  const ready = status?.status === "ready";

  return (
    <header className="flex items-center justify-between px-4 py-3 bg-surface border-b border-border shrink-0">
      <div className="flex items-center gap-3">
        <h1 className="text-base font-semibold">Quill Forge</h1>
        {ready && (
          <button
            onClick={onOpenMode}
            className="text-xs px-2 py-1 rounded-md bg-surface-alt text-text-muted hover:text-text transition-colors"
          >
            {status.mode}
            {status.project ? ` / ${status.project}` : ""}
          </button>
        )}
        {ready && (
          <LabeledBtn label="profile" onClick={onOpenProfile} title="Active profile and conductor">{status.conductor}</LabeledBtn>
        )}
      </div>

      <div className="flex items-end gap-2">
        {ready && (
          <>
            <LabeledBtn label="session" onClick={onNewSession} title="Start new session">+</LabeledBtn>
            <LabeledBtn label="" onClick={onOpenSessions} title="Browse saved sessions">sessions</LabeledBtn>
            <LabeledBtn label="lore" onClick={onOpenLore} title="Browse lore files">{status.loreSet !== "(default)" ? status.loreSet : ""} ({status.loreFiles})</LabeledBtn>
            <LabeledBtn label="plot" onClick={onOpenPlots} title="Browse plot arcs">plots</LabeledBtn>
            <LabeledBtn label="context" onClick={onOpenContext} title="Context usage">ctx</LabeledBtn>
            <LabeledBtn label="prompts" onClick={onOpenPrompts} title="Browse conductor prompts">prompts</LabeledBtn>
            <LabeledBtn label="characters" onClick={onOpenCharacters} title="Character cards">chars</LabeledBtn>
            <LabeledBtn label="text" onClick={onOpenTextTheme} title="Text color theme">{textThemeName.toLowerCase()}</LabeledBtn>
            <LabeledBtn label="layout" onClick={onOpenLayout} title="Switch layout">{layoutName}</LabeledBtn>
            {mode === "council" && onOpenCouncilConfig && (
              <LabeledBtn label="council" onClick={onOpenCouncilConfig} title="Configure council members">
                advisors
              </LabeledBtn>
            )}
            {mode === "research" && onOpenResearch && (
              <LabeledBtn label="research" onClick={onOpenResearch} title="Browse research projects">
                projects
              </LabeledBtn>
            )}
            <LabeledBtn label="model" onClick={onOpenProviders} title="Configure AI providers">
              {status.model.split("-").slice(0, 2).join("-")}
            </LabeledBtn>
          </>
        )}
        {!ready && (
          <span className="text-xs text-text-muted">
            {status ? status.status : "connecting..."}
          </span>
        )}
      </div>
    </header>
  );
}
