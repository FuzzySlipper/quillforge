import ReactMarkdown from "react-markdown";
import type { ReactNode } from "react";
import type { Message, ModeInfo, Status } from "../types";
import { formatStoryTarget, getWriterProseSummary } from "../workspaceSupport";
import type { InspectorSection } from "./AppInspector";
import WorkspaceQuickButton from "./WorkspaceQuickButton";
import WriterControls from "./WriterControls";

interface WriterWorkspaceProps {
  status: Status | null;
  modeInfo: ModeInfo | null;
  messages: Message[];
  hasPending: boolean;
  sending: boolean;
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  onOpenSection: (section: InspectorSection) => void;
  onAccept: () => void;
  onReject: () => void;
  onRegenerate: () => void;
}

export default function WriterWorkspace({
  status,
  modeInfo,
  messages,
  hasPending,
  sending,
  updateBanner,
  conversationPane,
  inputBar,
  onOpenSection,
  onAccept,
  onReject,
  onRegenerate,
}: WriterWorkspaceProps) {
  const { latestProse, proseCount } = getWriterProseSummary(messages);
  const currentTarget = formatStoryTarget(status?.project, status?.file);
  const pendingTarget = formatStoryTarget(modeInfo?.pendingProject, modeInfo?.pendingFile);
  const previewContent = hasPending
    ? modeInfo?.pendingContent ?? latestProse?.content ?? ""
    : latestProse?.content ?? "";

  return (
    <div className="flex h-full min-h-0 flex-col">
      {updateBanner}

      <div className="grid min-h-0 flex-1 gap-4 p-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(340px,0.78fr)]">
        <section className="qf-shell-card qf-shell-card--sunken min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-6 py-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0">
                <div className="qf-shell-folio">Draft Desk</div>
                <h1 className="qf-shell-title mt-1">{status?.file ?? "Choose a writer target"}</h1>
                <p className="qf-shell-subtitle mt-2 max-w-3xl">
                  {status?.project
                    ? `Working inside ${status.project}. Keep the manuscript in focus here while Quill stays available beside it for drafting and revision.`
                    : "Switch Writer into a project to anchor drafting around a concrete story file."}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
                <span className="qf-shell-card px-3 py-1.5">
                  style · <span className="text-text">{status?.writingStyle ?? "default"}</span>
                </span>
                <span className="qf-shell-card px-3 py-1.5">
                  lore set · <span className="text-text">{status?.loreSet ?? "loading"}</span>
                </span>
                <span className="qf-shell-card px-3 py-1.5">
                  drafts · <span className="text-text">{proseCount}</span>
                </span>
              </div>
            </div>

            <div className="mt-4 flex flex-wrap gap-2">
              <WorkspaceQuickButton label="Lore" onClick={() => onOpenSection("lore")} />
              <WorkspaceQuickButton label="Plots" onClick={() => onOpenSection("plots")} />
              <WorkspaceQuickButton label="Prompts" onClick={() => onOpenSection("prompts")} />
              <WorkspaceQuickButton label="Context" onClick={() => onOpenSection("context")} />
            </div>
          </div>

          <div className="border-b border-border/50 px-6 py-5">
            <WriterControls
              hasPending={hasPending}
              currentProject={status?.project ?? null}
              currentFile={status?.file ?? null}
              pendingProject={modeInfo?.pendingProject ?? null}
              pendingFile={modeInfo?.pendingFile ?? null}
              onAccept={onAccept}
              onReject={onReject}
              onRegenerate={onRegenerate}
              disabled={sending}
              canRegenerate={!!latestProse}
            />
          </div>

          <div className="min-h-0 overflow-y-auto px-6 py-6">
            <div className="mb-4 flex flex-wrap items-center gap-2">
              <span className="qf-shell-folio">
                {hasPending ? "Draft Awaiting Review" : latestProse ? "Latest Draft Output" : "Draft Surface"}
              </span>
              {pendingTarget && (
                <span className="qf-shell-card px-3 py-1.5 text-[12px] text-text-muted">
                  pending → <span className="text-text">{pendingTarget}</span>
                </span>
              )}
              {currentTarget && (
                <span className="qf-shell-card px-3 py-1.5 text-[12px] text-text-muted">
                  current → <span className="text-text">{currentTarget}</span>
                </span>
              )}
            </div>

            {previewContent ? (
              <div className="qf-shell-card border-accent/10 bg-[color-mix(in_srgb,var(--qf-surface-card)_96%,transparent)] px-6 py-6">
                <div className="prose prose-invert prose-sm prose-themed max-w-none [&_p]:mb-3 [&_p:last-child]:mb-0">
                  <ReactMarkdown>{previewContent}</ReactMarkdown>
                </div>
              </div>
            ) : (
              <div className="qf-shell-card border-dashed px-6 py-10 text-center text-text-muted">
                <p className="text-base text-text">No prose draft yet.</p>
                <p className="mt-2 text-sm leading-6">
                  Ask Quill for a scene, a rewrite pass, or a revision target from the support pane. When Writer produces pending prose, it will surface here for review.
                </p>
              </div>
            )}
          </div>
        </section>

        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Quill Support</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Keep the conversation, retries, branches, and variants here while the main draft stays visible beside it.
            </p>
          </div>
          <div className="flex min-h-0 flex-1 flex-col">
            {conversationPane}
            {inputBar}
          </div>
        </aside>
      </div>
    </div>
  );
}
