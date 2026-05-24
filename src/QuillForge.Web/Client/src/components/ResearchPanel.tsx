import { useEffect, useState } from "react";
import {
  listResearchProjects,
  listResearchFiles,
  readResearchFile,
  deleteResearchFile,
  deleteResearchProject,
} from "../api";
import SurfaceFrame, { type SurfaceVariant } from "./SurfaceFrame";

interface ResearchPanelProps {
  open: boolean;
  onClose: () => void;
  variant?: SurfaceVariant;
}

export default function ResearchPanel({ open, onClose, variant = "overlay" }: ResearchPanelProps) {
  const [projects, setProjects] = useState<string[]>([]);
  const [activeProject, setActiveProject] = useState<string | null>(null);
  const [files, setFiles] = useState<{ name: string; path: string }[]>([]);
  const [viewingFile, setViewingFile] = useState<{ name: string; content: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    (async () => {
      setActiveProject(null);
      setFiles([]);
      setViewingFile(null);
      try {
        const data = await listResearchProjects();
        if (!cancelled) setProjects(data.projects);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : "Failed to load projects");
      }
    })();
    return () => { cancelled = true; };
  }, [open]);

  async function openProject(project: string) {
    setActiveProject(project);
    setViewingFile(null);
    setError(null);
    try {
      const data = await listResearchFiles(project);
      setFiles(data.files);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load files");
    }
  }

  async function openFile(name: string) {
    if (!activeProject) return;
    setError(null);
    try {
      const data = await readResearchFile(activeProject, name);
      setViewingFile({ name, content: data.content });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to read file");
    }
  }

  async function handleDeleteFile(name: string) {
    if (!activeProject) return;
    try {
      await deleteResearchFile(activeProject, name);
      setFiles(files.filter((f) => f.name !== name));
      if (viewingFile?.name === name) setViewingFile(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  async function handleDeleteProject(project: string) {
    try {
      await deleteResearchProject(project);
      setProjects(projects.filter((p) => p !== project));
      if (activeProject === project) {
        setActiveProject(null);
        setFiles([]);
        setViewingFile(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  // ── File content view ──
  if (viewingFile) {
    return (
      <SurfaceFrame open={open} onClose={onClose} title={viewingFile.name} variant={variant}>
        <div className="flex flex-col gap-3">
          <button
            onClick={() => setViewingFile(null)}
            className="text-xs text-accent self-start"
          >
            &larr; Back to files
          </button>
          <pre className="text-sm text-text bg-bg border border-border rounded-lg p-3 overflow-auto max-h-[50vh] whitespace-pre-wrap">
            {viewingFile.content}
          </pre>
        </div>
      </SurfaceFrame>
    );
  }

  // ── Project files view ──
  if (activeProject) {
    return (
      <SurfaceFrame open={open} onClose={onClose} title={`Research: ${activeProject}`} variant={variant}>
        <div className="flex flex-col gap-3">
          <button
            onClick={() => { setActiveProject(null); setFiles([]); }}
            className="text-xs text-accent self-start"
          >
            &larr; Back to projects
          </button>
          <p className="text-xs text-text-muted">
            {files.length} file{files.length !== 1 ? "s" : ""}
          </p>

          {files.map((f) => (
            <div
              key={f.name}
              className="flex items-center justify-between gap-2 p-2 rounded-lg bg-bg border border-border"
            >
              <button
                onClick={() => openFile(f.name)}
                className="text-sm text-text hover:text-accent truncate text-left flex-1"
              >
                {f.name}
              </button>
              <button
                onClick={() => handleDeleteFile(f.name)}
                className="text-xs text-text-muted hover:text-danger px-2 py-1 rounded bg-surface-alt shrink-0"
              >
                Del
              </button>
            </div>
          ))}

          {files.length === 0 && (
            <p className="text-sm text-text-muted">No findings yet.</p>
          )}

          {error && <p className="text-xs text-danger">{error}</p>}
        </div>
      </SurfaceFrame>
    );
  }

  // ── Projects list view ──
  return (
    <SurfaceFrame open={open} onClose={onClose} title="Research Projects" variant={variant}>
      <div className="flex flex-col gap-3">
        {projects.length === 0 ? (
          <p className="text-sm text-text-muted">
            No research projects yet. Start researching to create one.
          </p>
        ) : (
          projects.map((p) => (
            <div
              key={p}
              className="flex items-center justify-between gap-2 p-2 rounded-lg bg-bg border border-border"
            >
              <button
                onClick={() => openProject(p)}
                className="text-sm font-medium text-text hover:text-accent truncate text-left flex-1"
              >
                {p}
              </button>
              <button
                onClick={() => handleDeleteProject(p)}
                className="text-xs text-text-muted hover:text-danger px-2 py-1 rounded bg-surface-alt shrink-0"
              >
                Del
              </button>
            </div>
          ))
        )}

        {error && <p className="text-xs text-danger">{error}</p>}
      </div>
    </SurfaceFrame>
  );
}
