import { useEffect, useState } from "react";
import MDEditor from "@uiw/react-md-editor";
import Overlay from "./Overlay";
import {
  generatePlot,
  listPlots,
  loadPlot,
  readPlot,
  unloadPlot,
  type PlotInfo,
  type PlotProgress,
} from "../api";

interface PlotBrowserProps {
  open: boolean;
  onClose: () => void;
  onChanged: () => void;
  sessionId: string | null;
}

export default function PlotBrowser({ open, onClose, onChanged, sessionId }: PlotBrowserProps) {
  const [files, setFiles] = useState<PlotInfo[]>([]);
  const [activePlotFile, setActivePlotFile] = useState<string | null>(null);
  const [plotProgress, setPlotProgress] = useState<PlotProgress | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [content, setContent] = useState("");
  const [loading, setLoading] = useState(false);
  const [working, setWorking] = useState(false);
  const [generatePrompt, setGeneratePrompt] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    refresh();
  }, [open, sessionId]);

  async function refresh() {
    setError(null);
    const data = await listPlots(sessionId);
    setFiles(data.files);
    setActivePlotFile(data.activePlotFile);
    setPlotProgress(data.plotProgress);
  }

  async function handleSelect(name: string) {
    setLoading(true);
    setError(null);
    try {
      const data = await readPlot(name);
      setSelected(name);
      setContent(data.content);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load plot.");
    } finally {
      setLoading(false);
    }
  }

  async function handleGenerate() {
    setWorking(true);
    setError(null);
    try {
      const result = await generatePlot(generatePrompt || null, sessionId);
      setGeneratePrompt("");
      await refresh();
      await handleSelect(result.name);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate plot.");
    } finally {
      setWorking(false);
    }
  }

  async function handleLoad(name: string) {
    if (!sessionId) {
      setError("Start or load a session before attaching a plot.");
      return;
    }

    setWorking(true);
    setError(null);
    try {
      await loadPlot(name, sessionId);
      await refresh();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load plot into the session.");
    } finally {
      setWorking(false);
    }
  }

  async function handleUnload() {
    if (!sessionId) {
      setError("Start or load a session before unloading a plot.");
      return;
    }

    setWorking(true);
    setError(null);
    try {
      await unloadPlot(sessionId);
      await refresh();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to unload plot.");
    } finally {
      setWorking(false);
    }
  }

  function handleBack() {
    setSelected(null);
    setContent("");
  }

  if (selected) {
    return (
      <Overlay open={open} onClose={onClose} title={selected}>
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <button
              onClick={handleBack}
              className="text-sm text-text-muted hover:text-text"
            >
              &larr; Back to plots
            </button>
            <div className="flex items-center gap-2">
              {selected === activePlotFile && (
                <span className="text-xs text-accent">Active in session</span>
              )}
              <button
                onClick={() => handleLoad(selected)}
                disabled={working || !sessionId}
                className="text-sm bg-accent text-white rounded-lg px-3 py-1.5 disabled:opacity-50"
              >
                Load
              </button>
            </div>
          </div>
          {loading ? (
            <p className="text-text-muted">Loading...</p>
          ) : (
            <div data-color-mode="dark">
              <MDEditor value={content} preview="preview" hideToolbar height={420} />
            </div>
          )}
        </div>
      </Overlay>
    );
  }

  return (
    <Overlay open={open} onClose={onClose} title="Plots">
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-2 rounded-lg bg-input-bg/70 p-3">
          <div className="text-xs uppercase tracking-wider text-text-muted">Generate Plot</div>
          <input
            value={generatePrompt}
            onChange={(e) => setGeneratePrompt(e.target.value)}
            placeholder="Optional prompt, e.g. dark fantasy romance"
            className="bg-input-bg text-text border border-border rounded-lg px-3 py-2 text-sm"
          />
          <button
            onClick={handleGenerate}
            disabled={working}
            className="self-end text-sm bg-accent text-white rounded-lg px-4 py-2 disabled:opacity-50"
          >
            {working ? "Generating..." : "Generate"}
          </button>
        </div>

        <div className="rounded-lg bg-input-bg/50 p-3 text-sm">
          <div className="flex items-center justify-between">
            <div>
              <div className="text-xs uppercase tracking-wider text-text-muted">Session Plot</div>
              <div className="mt-1">
                {activePlotFile ? activePlotFile : "No plot loaded for this session."}
              </div>
            </div>
            <button
              onClick={handleUnload}
              disabled={working || !sessionId || !activePlotFile}
              className="text-xs text-text-muted hover:text-text px-2 py-1 rounded-md bg-surface-alt disabled:opacity-50"
            >
              Unload
            </button>
          </div>

          {plotProgress && (plotProgress.currentBeat || plotProgress.completedBeats.length > 0 || plotProgress.deviations.length > 0) && (
            <div className="mt-3 space-y-2 text-xs text-text-muted">
              {plotProgress.currentBeat && <div>Current beat: {plotProgress.currentBeat}</div>}
              {plotProgress.completedBeats.length > 0 && (
                <div>Completed: {plotProgress.completedBeats.join(", ")}</div>
              )}
              {plotProgress.deviations.length > 0 && (
                <div>Deviations: {plotProgress.deviations.join(", ")}</div>
              )}
            </div>
          )}
        </div>

        {error && (
          <div className="text-sm text-red-300 bg-red-950/30 border border-red-900/50 rounded-lg px-3 py-2">
            {error}
          </div>
        )}

        <div className="text-xs text-text-muted">
          {files.length} files
        </div>

        {files.length === 0 ? (
          <p className="text-sm text-text-muted">No plot files yet.</p>
        ) : (
          <div className="flex flex-col gap-1">
            {files.map((file) => {
              const isActive = file.name === activePlotFile;
              return (
                <div
                  key={file.name}
                  className="flex items-center gap-2 rounded-lg px-3 py-2 hover:bg-input-bg transition-colors"
                >
                  <button
                    onClick={() => handleSelect(file.name)}
                    className="flex-1 text-left"
                  >
                    <div className="text-sm text-text">
                      {file.name}
                      {isActive ? <span className="ml-2 text-xs text-accent">active</span> : null}
                    </div>
                    <div className="text-xs text-text-muted">~{file.tokens} tok</div>
                  </button>
                  <button
                    onClick={() => handleLoad(file.name)}
                    disabled={working || !sessionId}
                    className="text-xs bg-surface-alt text-text-muted hover:text-text rounded-md px-2 py-1 disabled:opacity-50"
                  >
                    Load
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </Overlay>
  );
}
