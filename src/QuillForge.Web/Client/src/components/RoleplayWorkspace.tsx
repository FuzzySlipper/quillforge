import type { ReactNode } from "react";
import type { Status } from "../types";
import type { InspectorSection } from "./AppInspector";
import WorkspaceQuickButton from "./WorkspaceQuickButton";
import RoleplayControls from "./RoleplayControls";

interface RoleplayWorkspaceProps {
  status: Status | null;
  hasMessages: boolean;
  sending: boolean;
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  onOpenSection: (section: InspectorSection) => void;
  onRegenerate: () => void;
  onDeleteLast: () => void;
}

export default function RoleplayWorkspace({
  status,
  hasMessages,
  sending,
  updateBanner,
  conversationPane,
  inputBar,
  onOpenSection,
  onRegenerate,
  onDeleteLast,
}: RoleplayWorkspaceProps) {
  const sceneLabel = status?.file ?? "No scene loaded";
  const projectLabel = status?.project ?? "unset project";
  const aiCharacter = status?.aiCharacter || "unassigned";
  const userCharacter = status?.userCharacter || "you";
  const needsAiCharacter = !status?.aiCharacter;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Scene Stage</div>
            <h1 className="qf-shell-title mt-1">{sceneLabel}</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Keep the scene transcript in motion here while cast, lore, and director context stay a click away in the inspector.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              project · <span className="text-text">{projectLabel}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              AI plays · <span className="text-text">{aiCharacter}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              you play · <span className="text-text">{userCharacter}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              lore set · <span className="text-text">{status?.loreSet ?? "loading"}</span>
            </span>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <WorkspaceQuickButton label="Characters" onClick={() => onOpenSection("characters")} />
          <WorkspaceQuickButton label="Lore" onClick={() => onOpenSection("lore")} />
          <WorkspaceQuickButton label="Context" onClick={() => onOpenSection("context")} />
          <WorkspaceQuickButton label="Sessions" onClick={() => onOpenSection("sessions")} />
          <div className="ml-auto">
            <RoleplayControls
              hasMessages={hasMessages}
              onRegenerate={onRegenerate}
              onDeleteLast={onDeleteLast}
              disabled={sending}
            />
          </div>
        </div>

        {needsAiCharacter && (
          <div role="alert" className="mt-4 rounded-lg border border-warning/40 bg-warning-soft px-4 py-3 text-sm text-warning-text">
            Choose an AI character in Characters before starting roleplay.
          </div>
        )}
      </div>

      {updateBanner}

      <div className="min-h-0 flex-1 px-4 py-4">
        <div className="qf-shell-card qf-shell-card--sunken flex h-full min-h-0 flex-col overflow-hidden">
          {conversationPane}
          {inputBar}
        </div>
      </div>
    </div>
  );
}
