import { useEffect, useState } from "react";
import Overlay from "./Overlay";
import {
  listResearchProjects,
  listResearchFiles,
  readResearchFile,
  deleteResearchFile,
  deleteResearchProject,
} from "../api";

interface ResearchPanelProps {
  open: boolean;
  onClose: () => void;
}

export default function ResearchPanel({ open, onClose }: ResearchPanelProps) {
  const [projects, setProjects] = useState<string[]>([]);
  const [activeProject, setActiveProject] = useState<string | null>(null);
  const [files, setFiles] = useState<{ name: string; path: string }[]>([]);
  const [viewingFile, setViewingFile] = useState<{ name: string; content: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    refreshProjects();
    setActiveProject(null);
    setFiles([]);
    setViewingFile(null);
  }, [open]);

  async function refreshProjects() {
    try {
      const data = await listResearchProjects();
      setProjects(data.projects);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load projects");
    }
  }

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
      <Overlay open={open} onClose={onClose} title={viewingFile.name}>
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
      </Overlay>
    );
  }

  // ── Project files view ──
  if (activeProject) {
    return (
      <Overlay open={open} onClose={onClose} title={`Research: ${activeProject}`}>
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
                className="text-xs text-text-muted hover:text-red-400 px-2 py-1 rounded bg-surface-alt shrink-0"
              >
                Del
              </button>
            </div>
          ))}

          {files.length === 0 && (
            <p className="text-sm text-text-muted">No findings yet.</p>
          )}

          {error && <p className="text-xs text-red-400">{error}</p>}
        </div>
      </Overlay>
    );
  }

  // ── Projects list view ──
  return (
    <Overlay open={open} onClose={onClose} title="Research Projects">
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
                className="text-xs text-text-muted hover:text-red-400 px-2 py-1 rounded bg-surface-alt shrink-0"
              >
                Del
              </button>
            </div>
          ))
        )}

        {error && <p className="text-xs text-red-400">{error}</p>}
      </div>
    </Overlay>
  );
}
