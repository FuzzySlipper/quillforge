import type { ReactNode } from "react";
import type { Status } from "../types";
import type { InspectorSection } from "./AppInspector";

interface GuideWorkspaceProps {
  status: Status | null;
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  sending: boolean;
  onOpenMode: () => void;
  onNewSession: () => void;
  onOpenSessions: () => void;
  onOpenSection: (section: InspectorSection) => void;
  onQuickPrompt: (prompt: string) => void;
  onOpenProviders: () => void;
}

function GuideActionCard({
  eyebrow,
  title,
  body,
  disabled,
  onClick,
}: {
  eyebrow: string;
  title: string;
  body: string;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="qf-shell-card flex flex-col items-start gap-2 px-4 py-4 text-left transition-colors hover:border-accent/40 hover:text-text disabled:opacity-60"
    >
      <span className="qf-shell-folio">{eyebrow}</span>
      <span className="text-base font-medium text-text">{title}</span>
      <span className="text-sm leading-6 text-text-muted">{body}</span>
    </button>
  );
}

function GuidePromptChip({
  label,
  disabled,
  onClick,
}: {
  label: string;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="qf-shell-quiet-button disabled:opacity-60"
    >
      {label}
    </button>
  );
}

export default function GuideWorkspace({
  status,
  updateBanner,
  conversationPane,
  inputBar,
  sending,
  onOpenMode,
  onNewSession,
  onOpenSessions,
  onOpenSection,
  onQuickPrompt,
  onOpenProviders,
}: GuideWorkspaceProps) {
  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Guide Desk</div>
            <h1 className="qf-shell-title mt-1">Find the right path through QuillForge.</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Use Guide when you want orientation, troubleshooting, or help choosing the right mode before you dive into drafting or scene work.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              profile · <span className="text-text">{status?.profile ?? "loading"}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              lore · <span className="text-text">{status?.loreFiles ?? 0}</span>
            </span>
            <button
              type="button"
              onClick={onOpenProviders}
              className="qf-shell-card px-3 py-1.5 cursor-pointer transition-colors hover:border-accent/50 hover:text-accent"
            >
              model · <span className="text-text">{status?.model ?? "loading"}</span>
            </button>
          </div>
        </div>

        <div className="mt-5 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <GuideActionCard
            eyebrow="Mode"
            title="Choose the right workflow"
            body="Open the mode menu to switch between Guide, Writer, Roleplay, Lore Builder, Forge, Council, and Research."
            onClick={onOpenMode}
          />
          <GuideActionCard
            eyebrow="Session"
            title="Resume saved work"
            body="Open your recent sessions from the inspector so you can pick up an old branch or continue a project."
            onClick={onOpenSessions}
          />
          <GuideActionCard
            eyebrow="Setup"
            title="Inspect current context"
            body="See what QuillForge currently knows about your profile, lore loadout, and runtime state."
            onClick={() => onOpenSection("context")}
          />
          <GuideActionCard
            eyebrow="Lore"
            title="Browse source material"
            body="Open lore files directly in the inspector if you want Guide to reason about concrete world details."
            onClick={() => onOpenSection("lore")}
          />
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <GuidePromptChip
            label="Help me pick the right mode for outlining a novel."
            disabled={sending}
            onClick={() => onQuickPrompt("Help me pick the right QuillForge mode for outlining a novel.")}
          />
          <GuidePromptChip
            label="Inspect my setup and explain what QuillForge sees."
            disabled={sending}
            onClick={() => onQuickPrompt("Inspect my current QuillForge setup and explain what you see loaded right now.")}
          />
          <GuidePromptChip
            label="Show me how Writer pending review works."
            disabled={sending}
            onClick={() => onQuickPrompt("Explain how Writer pending review works in QuillForge and when I should accept or reject content.")}
          />
          <GuidePromptChip
            label="Start a fresh session."
            disabled={sending}
            onClick={onNewSession}
          />
        </div>
      </div>

      {updateBanner}

      <div className="min-h-0 flex-1">
        {conversationPane}
      </div>

      {inputBar}
    </div>
  );
}
