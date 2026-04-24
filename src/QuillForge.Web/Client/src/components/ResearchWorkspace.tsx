import { useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import type { ReactNode } from "react";
import {
  deleteResearchFile,
  deleteResearchProject,
  listResearchFiles,
  listResearchProjects,
  readResearchFile,
} from "../api";
import type { Status } from "../types";
import type { InspectorSection } from "./AppInspector";
import WorkspaceQuickButton from "./WorkspaceQuickButton";

interface ResearchWorkspaceProps {
  status: Status | null;
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  onOpenMode: () => void;
  onOpenSection: (section: InspectorSection) => void;
  onSelectProject: (project: string) => Promise<void>;
  onQuickPrompt: (prompt: string) => void;
}

interface ResearchFileEntry {
  name: string;
  path: string;
}

export default function ResearchWorkspace({
  status,
  updateBanner,
  conversationPane,
  inputBar,
  onOpenMode,
  onOpenSection,
  onSelectProject,
  onQuickPrompt,
}: ResearchWorkspaceProps) {
  const activeModeProject = status?.project ?? null;
  const [projects, setProjects] = useState<string[]>([]);
  const [selectedProject, setSelectedProject] = useState<string | null>(null);
  const [files, setFiles] = useState<ResearchFileEntry[]>([]);
  const [viewingFile, setViewingFile] = useState<{ name: string; content: string } | null>(null);
  const [loadingProjects, setLoadingProjects] = useState(false);
  const [loadingFiles, setLoadingFiles] = useState(false);
  const [loadingFile, setLoadingFile] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const preferredFileNameRef = useRef<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function refreshProjects() {
      setLoadingProjects(true);
      try {
        const data = await listResearchProjects();
        if (cancelled) {
          return;
        }

        setProjects(data.projects);
        setSelectedProject((previous) => {
          if (activeModeProject && data.projects.includes(activeModeProject)) {
            return activeModeProject;
          }

          if (previous && data.projects.includes(previous)) {
            return previous;
          }

          return data.projects[0] ?? null;
        });
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load research projects");
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
  }, [activeModeProject]);

  useEffect(() => {
    let cancelled = false;

    async function loadProjectFiles() {
      if (!selectedProject) {
        setFiles([]);
        setViewingFile(null);
        preferredFileNameRef.current = null;
        setLoadingFile(false);
        return;
      }

      setLoadingFiles(true);
      setError(null);
      try {
        const data = await listResearchFiles(selectedProject);
        if (cancelled) {
          return;
        }

        setFiles(data.files);
        const preferredFileName = preferredFileNameRef.current;
        const preferredFile = preferredFileName && data.files.some((file) => file.name === preferredFileName)
          ? preferredFileName
          : data.files[0]?.name ?? null;

        if (preferredFile) {
          setLoadingFile(true);
          const fileData = await readResearchFile(selectedProject, preferredFile);
          if (!cancelled) {
            preferredFileNameRef.current = preferredFile;
            setViewingFile({ name: preferredFile, content: fileData.content });
            setLoadingFile(false);
          }
        } else {
          preferredFileNameRef.current = null;
          setViewingFile(null);
          setLoadingFile(false);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load research files");
          setFiles([]);
          setViewingFile(null);
          preferredFileNameRef.current = null;
          setLoadingFile(false);
        }
      } finally {
        if (!cancelled) {
          setLoadingFiles(false);
        }
      }
    }

    void loadProjectFiles();

    return () => {
      cancelled = true;
    };
  }, [selectedProject]);

  async function openFile(name: string) {
    if (!selectedProject) {
      return;
    }

    setLoadingFile(true);
    setError(null);
    try {
      const data = await readResearchFile(selectedProject, name);
      preferredFileNameRef.current = name;
      setViewingFile({ name, content: data.content });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to read research file");
    } finally {
      setLoadingFile(false);
    }
  }

  async function handleDeleteFile(name: string) {
    if (!selectedProject) {
      return;
    }

    try {
      await deleteResearchFile(selectedProject, name);
      const nextFiles = files.filter((file) => file.name !== name);
      setFiles(nextFiles);

      if (viewingFile?.name === name) {
        if (nextFiles[0]) {
          await openFile(nextFiles[0].name);
        } else {
          preferredFileNameRef.current = null;
          setViewingFile(null);
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  async function handleDeleteProject(project: string) {
    try {
      await deleteResearchProject(project);
      const nextProjects = projects.filter((entry) => entry !== project);
      setProjects(nextProjects);

      if (selectedProject === project) {
        setSelectedProject(nextProjects[0] ?? null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  async function handleUseProject(project: string) {
    setError(null);
    try {
      await onSelectProject(project);
      setSelectedProject(project);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to activate research project");
    }
  }

  const fileHref = selectedProject && viewingFile
    ? `/content/research/${encodeURIComponent(selectedProject)}/${encodeURIComponent(viewingFile.name)}`
    : null;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Research Desk</div>
            <h1 className="qf-shell-title mt-1">{activeModeProject ?? "Research workspace"}</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Research mode should feel like a briefing room. Keep sources and saved findings in the main workspace while the sidecar handles synthesis and follow-up questions.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              active project · <span className="text-text">{activeModeProject ?? "none"}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              saved files · <span className="text-text">{files.length}</span>
            </span>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <WorkspaceQuickButton label="Mode Menu" onClick={onOpenMode} />
          <WorkspaceQuickButton label="Sessions" onClick={() => onOpenSection("sessions")} />
          <WorkspaceQuickButton label="Context" onClick={() => onOpenSection("context")} />
          <WorkspaceQuickButton label="Project browser" onClick={() => onOpenSection("research")} />
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <WorkspaceQuickButton
            label="Break this topic into 3 research angles."
            onClick={() => onQuickPrompt("Break my next research question into three focused angles and tell me why each one matters.")}
          />
          <WorkspaceQuickButton
            label="Summarize the saved findings in this project."
            onClick={() => onQuickPrompt("Summarize the saved findings in the active research project and call out any open questions.")}
          />
          <WorkspaceQuickButton
            label="What should I research next?"
            onClick={() => onQuickPrompt("Based on the active research project, what should I investigate next?")}
          />
        </div>
      </div>

      {updateBanner}

      <div className="grid min-h-0 flex-1 gap-4 p-4 xl:grid-cols-[minmax(280px,0.75fr)_minmax(0,1.15fr)_minmax(340px,0.82fr)]">
        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Projects & Files</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Browse stored findings here. Selecting a project updates the source workspace and lets the assistant save future research into the same folder.
            </p>
          </div>

          <div className="min-h-0 overflow-y-auto px-4 py-4">
            <div className="qf-shell-folio mb-3">Projects</div>
            <div className="flex flex-col gap-2">
              {loadingProjects && (
                <div className="text-sm text-text-muted">Loading research projects...</div>
              )}

              {!loadingProjects && projects.length === 0 && (
                <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                  No research projects yet. Start a research run from the sidecar to create one.
                </div>
              )}

              {projects.map((project) => {
                const isSelected = selectedProject === project;
                const isActive = activeModeProject === project;

                return (
                  <div key={project} className="qf-shell-card px-4 py-4">
                    <button
                      type="button"
                      onClick={() => setSelectedProject(project)}
                      className="w-full text-left"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <span className="text-sm font-medium text-text">{project}</span>
                        {isSelected && <span className="qf-shell-folio">open</span>}
                      </div>
                      <div className="mt-2 text-xs text-text-muted">
                        {isActive ? "active research target" : "stored findings"}
                      </div>
                    </button>

                    <div className="mt-3 flex flex-wrap gap-2">
                      {!isActive && (
                        <button
                          type="button"
                          onClick={() => {
                            void handleUseProject(project);
                          }}
                          className="rounded-lg bg-surface-alt px-3 py-1.5 text-xs text-text transition-colors hover:bg-border"
                        >
                          Use project
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => {
                          void handleDeleteProject(project);
                        }}
                        className="rounded-lg bg-surface px-3 py-1.5 text-xs text-text-muted transition-colors hover:bg-surface-alt hover:text-red-300"
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="qf-shell-folio mb-3 mt-6">Files</div>
            <div className="flex flex-col gap-2">
              {loadingFiles && selectedProject && (
                <div className="text-sm text-text-muted">Loading project files...</div>
              )}

              {!selectedProject && (
                <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                  Pick a project to browse saved findings.
                </div>
              )}

              {selectedProject && !loadingFiles && files.length === 0 && (
                <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                  No markdown findings saved in this project yet.
                </div>
              )}

              {files.map((file) => {
                const isViewing = viewingFile?.name === file.name;

                return (
                  <div key={file.name} className="qf-shell-card px-4 py-4">
                    <button
                      type="button"
                      onClick={() => {
                        void openFile(file.name);
                      }}
                      className="w-full text-left"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <span className="text-sm font-medium text-text">{file.name}</span>
                        {isViewing && <span className="qf-shell-folio">open</span>}
                      </div>
                      <div className="mt-2 break-all text-xs text-text-muted">{file.path}</div>
                    </button>

                    <div className="mt-3">
                      <button
                        type="button"
                        onClick={() => {
                          void handleDeleteFile(file.name);
                        }}
                        className="rounded-lg bg-surface px-3 py-1.5 text-xs text-text-muted transition-colors hover:bg-surface-alt hover:text-red-300"
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </aside>

        <section className="qf-shell-card qf-shell-card--sunken min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-6 py-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0">
                <div className="qf-shell-folio">Saved Findings</div>
                <h2 className="mt-1 text-2xl font-semibold text-text">
                  {viewingFile?.name ?? (selectedProject ? "Choose a file" : "Select a project")}
                </h2>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-text-muted">
                  Keep the markdown artifacts front and center here so research mode feels like a working archive instead of a modal browser bolted onto chat.
                </p>
              </div>

              {fileHref && (
                <a
                  href={fileHref}
                  target="_blank"
                  rel="noreferrer"
                  className="rounded-lg bg-surface-alt px-3 py-2 text-xs text-text-muted transition-colors hover:bg-border hover:text-text"
                >
                  Open raw file
                </a>
              )}
            </div>
          </div>

          <div className="min-h-0 overflow-y-auto px-6 py-6">
            {error && (
              <div className="mb-4 rounded-lg border border-red-400/30 bg-red-400/10 px-4 py-3 text-sm text-red-200">
                {error}
              </div>
            )}

            {loadingFile && (
              <div className="text-sm text-text-muted">Loading file content...</div>
            )}

            {!loadingFile && !selectedProject && (
              <div className="qf-shell-card border-dashed px-6 py-10 text-center text-text-muted">
                <p className="text-base text-text">No research project selected.</p>
                <p className="mt-2 text-sm leading-6">
                  Choose a project from the left or use the mode menu to set the active project for your next research run.
                </p>
              </div>
            )}

            {!loadingFile && selectedProject && !viewingFile && (
              <div className="qf-shell-card border-dashed px-6 py-10 text-center text-text-muted">
                <p className="text-base text-text">No saved file selected.</p>
                <p className="mt-2 text-sm leading-6">
                  Pick a markdown file from the list to read the detailed findings.
                </p>
              </div>
            )}

            {!loadingFile && viewingFile && (
              <article className="qf-shell-card border-accent/10 bg-[color-mix(in_srgb,var(--qf-surface-card)_96%,transparent)] px-6 py-6">
                <div className="prose prose-invert prose-sm prose-themed max-w-none [&_p]:mb-3 [&_p:last-child]:mb-0">
                  <ReactMarkdown>{viewingFile.content}</ReactMarkdown>
                </div>
              </article>
            )}
          </div>
        </section>

        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Research Briefing</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Keep the live synthesis and follow-up questions here while the main workspace stays anchored on saved sources and notes.
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
