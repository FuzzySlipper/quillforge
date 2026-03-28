import { useState, useEffect, useRef } from "react";
import type { DiagnosticEntry } from "../types";

interface DiagnosticsPanelProps {
  entries: DiagnosticEntry[];
  enabled: boolean;
}

const levelColors: Record<string, string> = {
  info: "text-text-muted",
  warning: "text-yellow-400",
  error: "text-red-400",
};

const categoryColors: Record<string, string> = {
  stream: "text-blue-400",
  llm: "text-purple-400",
  tool: "text-green-400",
  warning: "text-yellow-400",
};

export default function DiagnosticsPanel({ entries, enabled }: DiagnosticsPanelProps) {
  const [expanded, setExpanded] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (expanded && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [entries, expanded]);

  if (!enabled || entries.length === 0) return null;

  return (
    <div className="mx-3 mb-2">
      <button
        onClick={() => setExpanded(!expanded)}
        className="text-xs font-mono text-text-muted hover:text-text transition-colors px-2 py-1 rounded bg-surface-alt/50 hover:bg-surface-alt"
      >
        {expanded ? "▾" : "▸"} Diagnostics ({entries.length})
      </button>
      {expanded && (
        <div
          ref={scrollRef}
          className="mt-1 max-h-[200px] overflow-y-auto bg-surface-alt/30 border border-border/50 rounded-lg p-2 font-mono text-[11px] leading-relaxed space-y-px"
        >
          {entries.map((entry, i) => (
            <div key={i} className={`${levelColors[entry.level] ?? "text-text-muted"}`}>
              <span className={`${categoryColors[entry.category] ?? "text-text-muted"} opacity-70`}>
                [{entry.category}]
              </span>{" "}
              {entry.message}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
