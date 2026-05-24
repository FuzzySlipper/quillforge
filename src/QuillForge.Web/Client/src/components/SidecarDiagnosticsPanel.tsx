import { useCallback, useEffect, useRef, useState } from "react";
import type { DesktopShellDiagnosticEntry } from "../types";

const MAX_ENTRIES = 100;

const levelClasses: Record<string, string> = {
  info: "text-info",
  warning: "text-warning",
  error: "text-danger",
};

function isDesktopBridgeAvailable(): boolean {
  return typeof window !== "undefined" && !!window.quillforgeDesktop;
}

export default function SidecarDiagnosticsPanel() {
  const [entries, setEntries] = useState<DesktopShellDiagnosticEntry[]>([]);
  const [expanded, setExpanded] = useState(false);
  const [available] = useState(() => isDesktopBridgeAvailable());
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!available || !window.quillforgeDesktop) return;

    let mounted = true;

    window.quillforgeDesktop.getStatus().then((status) => {
      if (!mounted) return;
      const diagnostics = status.diagnostics ?? [];
      setEntries(diagnostics.slice(-MAX_ENTRIES));
    }).catch(() => {
      // silently ignore; status will arrive via onStatusUpdate
    });

    window.quillforgeDesktop.onStatusUpdate((status) => {
      if (!mounted) return;
      const diagnostics = status.diagnostics ?? [];
      setEntries(diagnostics.slice(-MAX_ENTRIES));
    });

    return () => {
      mounted = false;
    };
  }, [available]);

  useEffect(() => {
    if (expanded && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [entries, expanded]);

  const handleCopy = useCallback(async () => {
    const text = entries
      .map((e) => `[${e.source}] [${e.level}] ${e.message}`)
      .join("\n");
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // ignore clipboard errors
    }
  }, [entries]);

  if (!available || entries.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col">
      <button
        type="button"
        onClick={() => setExpanded((prev) => !prev)}
        className="inline-flex items-center gap-1.5 text-xs font-mono text-text-muted hover:text-text transition-colors"
        title="Sidecar diagnostics from the desktop shell"
      >
        <span className="inline-block h-1.5 w-1.5 rounded-full bg-accent" />
        {expanded ? "▾" : "▸"} Sidecar ({entries.length})
      </button>

      {expanded && (
        <div className="mt-2 rounded-lg border border-border/50 bg-surface-alt/40 overflow-hidden">
          <div className="flex items-center justify-between px-3 py-1.5 border-b border-border/30 bg-surface-alt/60">
            <span className="text-[11px] font-mono text-text-muted">
              {entries.length} entr{entries.length === 1 ? "y" : "ies"}
            </span>
            <button
              type="button"
              onClick={handleCopy}
              className="text-[11px] font-mono text-text-muted hover:text-text transition-colors"
            >
              Copy
            </button>
          </div>
          <div
            ref={scrollRef}
            className="max-h-[200px] overflow-y-auto px-3 py-2 font-mono text-[11px] leading-relaxed space-y-1"
          >
            {entries.map((entry, i) => (
              <div key={i} className="flex gap-2">
                <span className="shrink-0 rounded bg-surface-alt/80 px-1 text-[10px] uppercase tracking-wide text-text-muted">
                  {entry.source}
                </span>
                <span className={`shrink-0 ${levelClasses[entry.level] ?? "text-text-muted"}`}>
                  {entry.level}
                </span>
                <span className="text-text-muted break-all">{entry.message}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
