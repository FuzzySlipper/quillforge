import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import {
  getForgeProjectStatus,
  listForgeProjects,
  type ForgeProjectInfo,
  type ForgeProjectStatus,
} from "../api";
import type { Status } from "../types";
import type { InspectorSection } from "./AppInspector";
import WorkspaceQuickButton from "./WorkspaceQuickButton";

interface ForgeWorkspaceProps {
  status: Status | null;
  sending: boolean;
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  onOpenMode: () => void;
  onOpenSection: (section: InspectorSection) => void;
  onSelectProject: (project: string) => Promise<void>;
  onCreateProject: (name: string) => Promise<void>;
  onRunDesign: (project: string) => Promise<void>;
  onRunStart: (project: string) => Promise<void>;
  onRunApprove: (project: string) => Promise<void>;
  onRunPause: (project: string) => Promise<void>;
}

interface ForgeDocumentLink {
  label: string;
  href: string;
}

const FORGE_STAGES = ["Planning", "Design", "Writing", "Review", "Assembly", "Done"] as const;

function ForgeActionButton({
  label,
  disabled,
  emphasis = "default",
  onClick,
}: {
  label: string;
  disabled?: boolean;
  emphasis?: "default" | "accent" | "subtle";
  onClick: () => void;
}) {
  const className =
    emphasis === "accent"
      ? "rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast transition-colors hover:bg-accent-hover disabled:opacity-50"
      : emphasis === "subtle"
        ? "rounded-lg bg-surface px-4 py-2 text-sm font-medium text-text-muted transition-colors hover:bg-surface-alt hover:text-text disabled:opacity-50"
        : "rounded-lg bg-surface-alt px-4 py-2 text-sm font-medium text-text transition-colors hover:bg-border disabled:opacity-50";

  return (
    <button type="button" disabled={disabled} onClick={onClick} className={className}>
      {label}
    </button>
  );
}

function StatCard({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="qf-shell-card px-4 py-4">
      <div className="qf-shell-folio">{label}</div>
      <div className="mt-2 text-xl font-semibold text-text">{value}</div>
    </div>
  );
}

async function fileExists(href: string): Promise<boolean> {
  try {
    const response = await fetch(href, { method: "HEAD" });
    return response.ok;
  } catch {
    return false;
  }
}

function buildForgeDocumentCandidates(project: string): ForgeDocumentLink[] {
  const base = `/content/forge/${encodeURIComponent(project)}`;

  return [
    { label: "Outline", href: `${base}/plan/outline.md` },
    { label: "Style spec", href: `${base}/plan/style.md` },
    { label: "Run lore", href: `${base}/run-lore.md` },
    { label: "Output story", href: `${base}/output/story.md` },
  ];
}

export default function ForgeWorkspace({
  status,
  sending,
  updateBanner,
  conversationPane,
  inputBar,
  onOpenMode,
  onOpenSection,
  onSelectProject,
  onCreateProject,
  onRunDesign,
  onRunStart,
  onRunApprove,
  onRunPause,
}: ForgeWorkspaceProps) {
  const activeProject = status?.project ?? null;
  const [projects, setProjects] = useState<ForgeProjectInfo[]>([]);
  const [forgeStatus, setForgeStatus] = useState<ForgeProjectStatus | null>(null);
  const [documents, setDocuments] = useState<ForgeDocumentLink[]>([]);
  const [newProjectName, setNewProjectName] = useState("");
  const [loadingProjects, setLoadingProjects] = useState(false);
  const [loadingStatus, setLoadingStatus] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function refreshProjects() {
      setLoadingProjects(true);
      try {
        const items = await listForgeProjects();
        if (!cancelled) {
          setProjects(items);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load forge projects");
        }
      } finally {
        if (!cancelled) {
          setLoadingProjects(false);
        }
      }
    }

    void refreshProjects();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function refreshSelectedProject() {
      if (!activeProject) {
        setForgeStatus(null);
        setDocuments([]);
        return;
      }

      setLoadingStatus(true);
      setError(null);
      try {
        const [projectStatus, availableDocuments] = await Promise.all([
          getForgeProjectStatus(activeProject),
          Promise.all(
            buildForgeDocumentCandidates(activeProject).map(async (document) =>
              (await fileExists(document.href)) ? document : null),
          ),
        ]);

        if (!cancelled) {
          setForgeStatus(projectStatus);
          setDocuments(availableDocuments.filter((item): item is ForgeDocumentLink => item !== null));
        }
      } catch (err) {
        if (!cancelled) {
          setForgeStatus(null);
          setDocuments([]);
          setError(err instanceof Error ? err.message : "Failed to load forge status");
        }
      } finally {
        if (!cancelled) {
          setLoadingStatus(false);
        }
      }
    }

    void refreshSelectedProject();

    return () => {
      cancelled = true;
    };
  }, [activeProject]);

  const chapterEntries = Object.entries(forgeStatus?.chapters ?? {}).sort(([left], [right]) =>
    left.localeCompare(right),
  );
  const stageIndex = FORGE_STAGES.findIndex((stage) => stage === forgeStatus?.stage);
  const completedChapters = chapterEntries.filter(([, chapter]) => chapter.state === "Done").length;
  const flaggedChapters = chapterEntries.filter(([, chapter]) => chapter.state === "Flagged").length;
  const totalTokens = forgeStatus
    ? forgeStatus.stats.totalInputTokens + forgeStatus.stats.totalOutputTokens
    : 0;
  const activeProjectSummary = activeProject ?? "No forge project selected";

  async function refreshWorkspaceData(projectName = activeProject) {
    try {
      const items = await listForgeProjects();
      setProjects(items);
      if (projectName) {
        const [projectStatus, availableDocuments] = await Promise.all([
          getForgeProjectStatus(projectName),
          Promise.all(
            buildForgeDocumentCandidates(projectName).map(async (document) =>
              (await fileExists(document.href)) ? document : null),
          ),
        ]);
        setForgeStatus(projectStatus);
        setDocuments(availableDocuments.filter((item): item is ForgeDocumentLink => item !== null));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Refresh failed");
    }
  }

  async function handleCreateProject() {
    const trimmed = newProjectName.trim();
    if (!trimmed) {
      return;
    }

    setError(null);
    try {
      await onCreateProject(trimmed);
      setNewProjectName("");
      await refreshWorkspaceData(trimmed.replace(/\s+/g, "-"));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create forge project");
    }
  }

  async function handleSelectProject(project: string) {
    setError(null);
    try {
      await onSelectProject(project);
      await refreshWorkspaceData(project);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to switch forge project");
    }
  }

  async function handleProjectAction(
    action: (project: string) => Promise<void>,
  ) {
    if (!activeProject) {
      return;
    }

    setError(null);
    try {
      await action(activeProject);
      await refreshWorkspaceData(activeProject);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Forge action failed");
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Forge Bench</div>
            <h1 className="qf-shell-title mt-1">{activeProjectSummary}</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Forge is a pipeline workspace. Use it to inspect project state, run explicit stages, and watch chapter progress without collapsing the whole mode back into generic chat.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              stage · <span className="text-text">{forgeStatus?.stage ?? "idle"}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              chapters · <span className="text-text">{forgeStatus?.chapterCount ?? 0}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              status · <span className="text-text">{forgeStatus?.paused ? "paused" : "ready"}</span>
            </span>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <WorkspaceQuickButton label="Mode Menu" onClick={onOpenMode} />
          <WorkspaceQuickButton label="Sessions" onClick={() => onOpenSection("sessions")} />
          <WorkspaceQuickButton label="Context" onClick={() => onOpenSection("context")} />
          <WorkspaceQuickButton label="Lore" onClick={() => onOpenSection("lore")} />
        </div>
      </div>

      {updateBanner}

      <div className="grid min-h-0 flex-1 gap-4 p-4 xl:grid-cols-[minmax(260px,0.66fr)_minmax(0,1.15fr)_minmax(340px,0.88fr)]">
        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Projects</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Pick the active forge project here or create a new pipeline root without leaving the workspace.
            </p>
          </div>

          <div className="border-b border-border/50 px-4 py-4">
            <div className="flex gap-2">
              <input
                type="text"
                value={newProjectName}
                onChange={(event) => setNewProjectName(event.target.value)}
                placeholder="new-project-name"
                className="min-w-0 flex-1 rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text focus:border-accent focus:outline-none"
              />
              <ForgeActionButton
                label="Create"
                disabled={sending || !newProjectName.trim()}
                emphasis="accent"
                onClick={() => {
                  void handleCreateProject();
                }}
              />
            </div>
          </div>

          <div className="min-h-0 overflow-y-auto px-4 py-4">
            <div className="flex flex-col gap-2">
              {loadingProjects && (
                <div className="text-sm text-text-muted">Loading forge projects...</div>
              )}

              {!loadingProjects && projects.length === 0 && (
                <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                  No forge projects yet. Create one here or use the mode menu to jump into a selected project.
                </div>
              )}

              {projects.map((project) => {
                const isActive = project.name === activeProject;

                return (
                  <button
                    key={project.name}
                    type="button"
                    onClick={() => {
                      void handleSelectProject(project.name);
                    }}
                    className={`qf-shell-card flex flex-col items-start gap-2 px-4 py-4 text-left transition-colors ${
                      isActive
                        ? "border-accent/50 bg-accent/10 text-text"
                        : "hover:border-accent/40 hover:text-text"
                    }`}
                  >
                    <div className="flex w-full items-center justify-between gap-3">
                      <span className="text-sm font-medium text-text">{project.name}</span>
                      {isActive && <span className="qf-shell-folio">active</span>}
                    </div>
                    <div className="text-xs text-text-muted">
                      {project.stage} · {project.chapterCount} chapters{project.paused ? " · paused" : ""}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>
        </aside>

        <section className="qf-shell-card qf-shell-card--sunken min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-6 py-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0">
                <div className="qf-shell-folio">Pipeline State</div>
                <h2 className="mt-1 text-2xl font-semibold text-text">
                  {activeProject ?? "Select a forge project"}
                </h2>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-text-muted">
                  The action bar drives explicit pipeline stages. The stage strip, chapter ledger, and document shelf make the current forge state visible without relying on slash commands or guesswork.
                </p>
              </div>

              <div className="flex flex-wrap gap-2">
                <ForgeActionButton
                  label="Design"
                  disabled={sending || !activeProject}
                  onClick={() => {
                    void handleProjectAction(onRunDesign);
                  }}
                />
                <ForgeActionButton
                  label="Start"
                  disabled={sending || !activeProject}
                  emphasis="accent"
                  onClick={() => {
                    void handleProjectAction(onRunStart);
                  }}
                />
                <ForgeActionButton
                  label="Approve"
                  disabled={sending || !activeProject || !forgeStatus?.paused}
                  onClick={() => {
                    void handleProjectAction(onRunApprove);
                  }}
                />
                <ForgeActionButton
                  label="Pause"
                  disabled={sending || !activeProject || !!forgeStatus?.paused}
                  emphasis="subtle"
                  onClick={() => {
                    void handleProjectAction(onRunPause);
                  }}
                />
                <ForgeActionButton
                  label="Refresh"
                  disabled={loadingStatus}
                  emphasis="subtle"
                  onClick={() => {
                    void refreshWorkspaceData(activeProject);
                  }}
                />
              </div>
            </div>

            <div className="mt-5 grid gap-2 md:grid-cols-3 xl:grid-cols-6">
              {FORGE_STAGES.map((stage, index) => {
                const isCurrent = stage === forgeStatus?.stage;
                const isComplete = stageIndex >= 0 && index < stageIndex;

                return (
                  <div
                    key={stage}
                    className={`qf-shell-card px-3 py-3 text-sm ${
                      isCurrent
                        ? "border-accent/50 bg-accent/10 text-text"
                        : isComplete
                          ? "border-accent/30 bg-surface-alt text-text"
                          : "text-text-muted"
                    }`}
                  >
                    <div className="qf-shell-folio">{String(index + 1).padStart(2, "0")}</div>
                    <div className="mt-2 font-medium">{stage}</div>
                  </div>
                );
              })}
            </div>
          </div>

          <div className="min-h-0 overflow-y-auto px-6 py-6">
            {loadingStatus && activeProject && (
              <div className="mb-4 text-sm text-text-muted">Loading forge status...</div>
            )}

            {error && (
              <div className="mb-4 rounded-lg border border-danger-border bg-danger-soft px-4 py-3 text-sm text-danger-text">
                {error}
              </div>
            )}

            {!activeProject ? (
              <div className="qf-shell-card border-dashed px-6 py-10 text-center text-text-muted">
                <p className="text-base text-text">No forge project selected.</p>
                <p className="mt-2 text-sm leading-6">
                  Choose a project from the list or create a new one to bring the pipeline, chapter ledger, and output shelf into view.
                </p>
              </div>
            ) : (
              <div className="flex flex-col gap-6">
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <StatCard label="Completed" value={`${completedChapters}`} />
                  <StatCard label="Flagged" value={`${flaggedChapters}`} />
                  <StatCard label="Agent Calls" value={`${forgeStatus?.stats.agentCalls ?? 0}`} />
                  <StatCard label="Tokens" value={totalTokens.toLocaleString()} />
                </div>

                <section>
                  <div className="qf-shell-folio mb-3">Document Shelf</div>
                  {documents.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      No plan or output documents are visible yet. Run design or writing stages to populate the forge project.
                    </div>
                  ) : (
                    <div className="grid gap-3 md:grid-cols-2">
                      {documents.map((document) => (
                        <a
                          key={document.href}
                          href={document.href}
                          target="_blank"
                          rel="noreferrer"
                          className="qf-shell-card px-4 py-4 text-sm text-text transition-colors hover:border-accent/40"
                        >
                          <div className="qf-shell-folio">Artifact</div>
                          <div className="mt-2 font-medium">{document.label}</div>
                          <div className="mt-2 break-all text-xs text-text-muted">
                            {document.href.replace("/content/", "")}
                          </div>
                        </a>
                      ))}
                    </div>
                  )}
                </section>

                <section>
                  <div className="qf-shell-folio mb-3">Chapter Ledger</div>
                  {chapterEntries.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      Chapters will appear here once design has created briefs or the writing pipeline has started.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      {chapterEntries.map(([chapterId, chapter]) => (
                        <div
                          key={chapterId}
                          className="qf-shell-card grid gap-3 px-4 py-4 text-sm text-text md:grid-cols-[minmax(0,1fr)_auto_auto_auto]"
                        >
                          <div className="min-w-0">
                            <div className="font-medium">{chapterId}</div>
                            <div className="mt-1 text-xs text-text-muted">{chapter.state}</div>
                          </div>
                          <div className="text-xs text-text-muted md:text-right">
                            <span className="text-text">{chapter.wordCount.toLocaleString()}</span> words
                          </div>
                          <div className="text-xs text-text-muted md:text-right">
                            <span className="text-text">{chapter.revisionCount}</span> revisions
                          </div>
                          <div className="text-xs text-text-muted md:text-right">
                            state · <span className="text-text">{chapter.state}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </section>
              </div>
            )}
          </div>
        </section>

        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Forge Console</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Use the sidecar to ask what command to run next, inspect pipeline output, or keep the operation log visible while the project state stays front and center.
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
