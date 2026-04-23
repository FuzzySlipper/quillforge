import type { RefObject, ReactNode } from "react";
import type { DiagnosticEntry, Message, Mode } from "../types";
import DiagnosticsPanel from "./DiagnosticsPanel";
import MessageBubble from "./MessageBubble";

interface ConversationPaneProps {
  messages: Message[];
  mode: Mode;
  diagnosticEntries: DiagnosticEntry[];
  diagnosticsEnabled: boolean;
  sending: boolean;
  streamStatus: string | null;
  elapsed: number;
  messagesEndRef: RefObject<HTMLDivElement | null>;
  emptyState: ReactNode;
  onStop: () => void;
  onEdit?: (id: string, newContent: string) => void;
  onRetry?: (id: string) => void;
  onSwipe?: (id: string, direction: "prev" | "next") => void;
  onDelete?: (id: string) => void;
  onFork?: (id: string) => void;
  className?: string;
  contentClassName?: string;
}

export default function ConversationPane({
  messages,
  mode,
  diagnosticEntries,
  diagnosticsEnabled,
  sending,
  streamStatus,
  elapsed,
  messagesEndRef,
  emptyState,
  onStop,
  onEdit,
  onRetry,
  onSwipe,
  onDelete,
  onFork,
  className = "flex-1 min-h-0 overflow-y-auto",
  contentClassName = "flex min-h-full flex-col gap-3 p-4",
}: ConversationPaneProps) {
  return (
    <div className={className}>
      <div className={contentClassName}>
        {messages.length === 0 ? emptyState : null}

        {messages.map((msg, i) => {
          const msgIndex =
            msg.role === "system"
              ? 0
              : messages.slice(0, i + 1).filter((m) => m.role !== "system").length;

          return (
            <MessageBubble
              key={msg.id}
              message={msg}
              index={msgIndex}
              mode={mode}
              onEdit={msg.role !== "system" ? onEdit : undefined}
              onRetry={msg.role !== "system" ? onRetry : undefined}
              onSwipe={msg.role === "assistant" ? onSwipe : undefined}
              onDelete={msg.role !== "system" ? onDelete : undefined}
              onFork={msg.role !== "system" ? onFork : undefined}
            />
          );
        })}

        <DiagnosticsPanel entries={diagnosticEntries} enabled={diagnosticsEnabled} />

        {sending && (
          <div className="flex items-center gap-2 px-4 py-2 text-sm italic text-text-muted">
            <span className="inline-block h-2 w-2 rounded-full bg-accent animate-pulse" />
            <span>{streamStatus || "Working..."}</span>
            <span className="font-mono text-xs tabular-nums text-text-muted/40">{elapsed}s</span>
            <button
              onClick={onStop}
              className="ml-auto rounded bg-surface-alt px-2 py-1 text-xs text-text-muted transition-colors hover:bg-border hover:text-text"
            >
              Stop
            </button>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>
    </div>
  );
}
