import { MODE_DESCRIPTIONS, MODE_LABELS } from "../modePresentation";
import type { Artifact } from "../artifacts";
import type { Mode, Status } from "../types";

interface AppInspectorProps {
  status: Status | null;
  mode: Mode;
  layoutName: string;
  textThemeName: string;
  artifact: Artifact | null;
  onOpenSessions: () => void;
  onOpenLore: () => void;
  onOpenPlots: () => void;
  onOpenPrompts: () => void;
  onOpenCharacters: () => void;
  onOpenContext: () => void;
  onOpenProfile: () => void;
  onOpenProviders: () => void;
  onOpenTextTheme: () => void;
  onOpenLayout: () => void;
  onOpenCouncilConfig: () => void;
  onOpenResearch: () => void;
}

interface InspectorActionProps {
  label: string;
  meta?: string;
  onClick: () => void;
}

function InspectorAction({ label, meta, onClick }: InspectorActionProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="qf-shell-card w-full px-3 py-2 text-left transition-colors hover:border-accent/40 hover:text-text"
    >
      <div className="text-sm text-text">{label}</div>
      {meta && <div className="mt-1 text-[11px] text-text-muted">{meta}</div>}
    </button>
  );
}

function MetaRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 py-1">
      <span className="qf-shell-folio">{label}</span>
      <span className="text-right text-[13px] leading-5 text-text">
        {value}
      </span>
    </div>
  );
}

export default function AppInspector({
  status,
  mode,
  layoutName,
  textThemeName,
  artifact,
  onOpenSessions,
  onOpenLore,
  onOpenPlots,
  onOpenPrompts,
  onOpenCharacters,
  onOpenContext,
  onOpenProfile,
  onOpenProviders,
  onOpenTextTheme,
  onOpenLayout,
  onOpenCouncilConfig,
  onOpenResearch,
}: AppInspectorProps) {
  return (
    <div className="flex h-full flex-col gap-4 overflow-y-auto px-4 py-5">
      <div>
        <div className="qf-shell-folio">Workspace</div>
        <h2 className="qf-shell-title mt-1">{MODE_LABELS[mode]}</h2>
        <p className="qf-shell-subtitle mt-2">{MODE_DESCRIPTIONS[mode]}</p>
      </div>

      <section className="qf-shell-card px-3 py-3">
        <div className="qf-shell-folio mb-2">Current Session</div>
        <div className="space-y-1">
          <MetaRow label="Mode" value={MODE_LABELS[mode]} />
          <MetaRow label="Profile" value={status?.profile ?? "loading"} />
          <MetaRow label="Project" value={status?.project ?? "not set"} />
          <MetaRow label="File" value={status?.file ?? "none"} />
          <MetaRow label="Lore" value={status ? `${status.loreSet} (${status.loreFiles})` : "loading"} />
          <MetaRow label="Model" value={status?.model ?? "loading"} />
          <MetaRow label="Theme" value={textThemeName} />
          <MetaRow label="Layout" value={layoutName} />
        </div>
      </section>

      <section className="space-y-2">
        <div className="qf-shell-folio">Browse</div>
        <InspectorAction label="Sessions" meta="Load, resume, or clean up saved work" onClick={onOpenSessions} />
        <InspectorAction label="Lore" meta="Inspect lore files and active lore set" onClick={onOpenLore} />
        <InspectorAction label="Plots" meta="Open plot arcs and story structure" onClick={onOpenPlots} />
        <InspectorAction label="Prompts" meta="Browse assistant, rules, and style prompts" onClick={onOpenPrompts} />
        <InspectorAction label="Character Cards" meta="Manage roleplay and story cast" onClick={onOpenCharacters} />
        <InspectorAction
          label="Context & Debug"
          meta="Inspect context usage, runtime status, and conversation debug data"
          onClick={onOpenContext}
        />
        {mode === "research" && (
          <InspectorAction
            label="Research Projects"
            meta="Open the current research project browser"
            onClick={onOpenResearch}
          />
        )}
        {mode === "council" && (
          <InspectorAction
            label="Council Advisors"
            meta="Configure the active council roster"
            onClick={onOpenCouncilConfig}
          />
        )}
      </section>

      {artifact && (
        <section className="qf-shell-card px-3 py-3">
          <div className="qf-shell-folio mb-2">Artifact</div>
          <div className="text-sm text-text">{artifact.format}</div>
          <p className="mt-2 line-clamp-6 text-[13px] leading-5 text-text-muted">
            {artifact.content}
          </p>
        </section>
      )}

      <section className="space-y-2">
        <div className="qf-shell-folio">Settings</div>
        <InspectorAction label="Profile" meta="Switch lore, rules, and writing defaults" onClick={onOpenProfile} />
        <InspectorAction label="Providers" meta="Configure models and provider aliases" onClick={onOpenProviders} />
        <InspectorAction label="Text Theme" meta="Adjust prose coloration in chat messages" onClick={onOpenTextTheme} />
        <InspectorAction label="Legacy Layout & Background" meta="Open the existing layout/background tools" onClick={onOpenLayout} />
      </section>
    </div>
  );
}
