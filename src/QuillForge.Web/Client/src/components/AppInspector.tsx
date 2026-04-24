import type { ReactNode } from "react";
import type { Artifact } from "../artifacts";
import { MODE_DESCRIPTIONS, MODE_LABELS } from "../modePresentation";
import type { Mode, ModeInfo, Status } from "../types";
import { formatStoryTarget } from "../workspaceSupport";

export type InspectorSection =
  | "overview"
  | "sessions"
  | "lore"
  | "plots"
  | "prompts"
  | "characters"
  | "context"
  | "research";

interface AppInspectorProps {
  status: Status | null;
  mode: Mode;
  modeInfo: ModeInfo | null;
  layoutName: string;
  textThemeName: string;
  artifact: Artifact | null;
  section: InspectorSection;
  onSelectSection: (section: InspectorSection) => void;
  onOpenLayout: () => void;
  onOpenCouncilConfig: () => void;
  children?: ReactNode;
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

function SectionButton({
  active,
  label,
  onClick,
}: {
  active: boolean;
  label: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`qf-shell-card px-3 py-2 text-left text-sm transition-colors ${
        active
          ? "border-accent/50 bg-accent/10 text-text"
          : "hover:border-accent/40 hover:text-text"
      }`}
    >
      {label}
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
  modeInfo,
  layoutName,
  textThemeName,
  artifact,
  section,
  onSelectSection,
  onOpenLayout,
  onOpenCouncilConfig,
  children,
}: AppInspectorProps) {
  const sectionButtons: Array<{ id: InspectorSection; label: string }> = [
    { id: "overview", label: "Overview" },
    { id: "sessions", label: "Sessions" },
    { id: "lore", label: "Lore" },
    { id: "plots", label: "Plots" },
    { id: "prompts", label: "Prompts" },
    { id: "characters", label: "Characters" },
    { id: "context", label: "Context" },
  ];

  if (mode === "research") {
    sectionButtons.push({ id: "research", label: "Research" });
  }

  const currentTarget = formatStoryTarget(status?.project, status?.file);
  const pendingTarget = formatStoryTarget(modeInfo?.pendingProject, modeInfo?.pendingFile);

  const modeSnapshot = (() => {
    switch (mode) {
      case "guide":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Guide Focus</div>
            <p className="text-sm leading-6 text-text-muted">
              Use the inspector to pull sessions, lore, and runtime context into view while
              Guide helps you choose the right workflow.
            </p>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Open saved sessions"
                meta="Resume earlier branches or continue unfinished work"
                onClick={() => onSelectSection("sessions")}
              />
              <InspectorAction
                label="Inspect current context"
                meta="See what profile, lore, and runtime state Guide can currently access"
                onClick={() => onSelectSection("context")}
              />
              <InspectorAction
                label="Browse loaded lore"
                meta="Review world material without leaving the workspace"
                onClick={() => onSelectSection("lore")}
              />
            </div>
          </section>
        );
      case "writer":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Draft Focus</div>
            <div className="space-y-1">
              <MetaRow label="Current target" value={currentTarget ?? "not set"} />
              <MetaRow
                label="Pending review"
                value={modeInfo?.pendingContent ? "awaiting decision" : "clear"}
              />
              <MetaRow label="Pending target" value={pendingTarget ?? "none"} />
              <MetaRow
                label="Writing style"
                value={status?.writingStyle ?? "default"}
              />
            </div>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Open plots"
                meta="Keep structure and outline material beside the manuscript"
                onClick={() => onSelectSection("plots")}
              />
              <InspectorAction
                label="Open prompts"
                meta="Reference prompt material without leaving Writer"
                onClick={() => onSelectSection("prompts")}
              />
              <InspectorAction
                label="Inspect draft context"
                meta="Check the current session, file, and token state"
                onClick={() => onSelectSection("context")}
              />
            </div>
          </section>
        );
      case "roleplay":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Scene Focus</div>
            <div className="space-y-1">
              <MetaRow label="Project" value={status?.project ?? "not set"} />
              <MetaRow label="Scene" value={status?.file ?? "none"} />
              <MetaRow label="AI plays" value={status?.aiCharacter || "unassigned"} />
              <MetaRow label="You play" value={status?.userCharacter || "you"} />
            </div>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Open characters"
                meta="Keep cast information close while the scene stays in motion"
                onClick={() => onSelectSection("characters")}
              />
              <InspectorAction
                label="Open lore"
                meta="Check world details and scene references"
                onClick={() => onSelectSection("lore")}
              />
              <InspectorAction
                label="Inspect runtime context"
                meta="Review session state, tokens, and current roleplay bindings"
                onClick={() => onSelectSection("context")}
              />
            </div>
          </section>
        );
      case "forge":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Forge Focus</div>
            <div className="space-y-1">
              <MetaRow label="Project" value={status?.project ?? "not set"} />
              <MetaRow label="Current stage" value={status?.file ?? "pipeline-owned"} />
              <MetaRow label="Lore set" value={status?.loreSet ?? "loading"} />
              <MetaRow label="Model" value={status?.model ?? "loading"} />
            </div>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Inspect runtime context"
                meta="See tokens, session state, and the current Forge environment"
                onClick={() => onSelectSection("context")}
              />
              <InspectorAction
                label="Open saved sessions"
                meta="Review earlier operator conversations and pipeline runs"
                onClick={() => onSelectSection("sessions")}
              />
              <InspectorAction
                label="Browse lore"
                meta="Check the lore material that Forge can lean on for planning"
                onClick={() => onSelectSection("lore")}
              />
            </div>
          </section>
        );
      case "research":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Research Focus</div>
            <div className="space-y-1">
              <MetaRow label="Project" value={status?.project ?? "not set"} />
              <MetaRow label="Profile" value={status?.profile ?? "loading"} />
              <MetaRow label="Lore set" value={status?.loreSet ?? "loading"} />
              <MetaRow label="Model" value={status?.model ?? "loading"} />
            </div>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Open research browser"
                meta="Browse saved findings and project markdown in the inspector"
                onClick={() => onSelectSection("research")}
              />
              <InspectorAction
                label="Inspect runtime context"
                meta="Check token use and the current research-mode runtime state"
                onClick={() => onSelectSection("context")}
              />
              <InspectorAction
                label="Open saved sessions"
                meta="Return to earlier research threads and branches"
                onClick={() => onSelectSection("sessions")}
              />
            </div>
          </section>
        );
      case "council":
        return (
          <section className="qf-shell-card px-3 py-3">
            <div className="qf-shell-folio mb-2">Council Focus</div>
            <p className="text-sm leading-6 text-text-muted">
              Council mode routes substantive requests through the advisor roster first, then synthesizes the result for the user-facing response.
            </p>
            <div className="mt-3 space-y-2">
              <InspectorAction
                label="Configure advisors"
                meta="Open the council roster editor and tune the active perspectives"
                onClick={onOpenCouncilConfig}
              />
              <InspectorAction
                label="Inspect runtime context"
                meta="See tokens, session state, and current council-mode bindings"
                onClick={() => onSelectSection("context")}
              />
              <InspectorAction
                label="Open saved sessions"
                meta="Review earlier council deliberations and forks"
                onClick={() => onSelectSection("sessions")}
              />
            </div>
          </section>
        );
      default:
        return null;
    }
  })();

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/50 px-4 py-5">
        <div>
          <div className="qf-shell-folio">Workspace</div>
          <h2 className="qf-shell-title mt-1">{MODE_LABELS[mode]}</h2>
          <p className="qf-shell-subtitle mt-2">{MODE_DESCRIPTIONS[mode]}</p>
        </div>

        <div className="mt-4">
          <div className="qf-shell-folio mb-2">Library</div>
          <div className="grid grid-cols-2 gap-2">
            {sectionButtons.map((button) => (
              <SectionButton
                key={button.id}
                active={section === button.id}
                label={button.label}
                onClick={() => onSelectSection(button.id)}
              />
            ))}
          </div>
        </div>
      </div>

      {section === "overview" ? (
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5">
          <div className="flex flex-col gap-4">
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

            {modeSnapshot}

            {modeInfo?.notice && (
              <section className="qf-shell-card px-3 py-3">
                <div className="qf-shell-folio mb-2">Mode Notice</div>
                <p className="text-sm leading-6 text-text-muted">{modeInfo.notice}</p>
              </section>
            )}

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
              <div className="qf-shell-folio">Workspace Tools</div>
              <InspectorAction
                label="Layout & Background"
                meta="Open appearance settings for shell layout and background"
                onClick={onOpenLayout}
              />
              {mode === "council" && (
                <InspectorAction
                  label="Council Advisors"
                  meta="Configure the active council roster"
                  onClick={onOpenCouncilConfig}
                />
              )}
            </section>
          </div>
        </div>
      ) : (
        <div className="min-h-0 flex-1">
          {children ?? <p className="px-4 py-5 text-sm text-text-muted">Select a library surface.</p>}
        </div>
      )}
    </div>
  );
}
