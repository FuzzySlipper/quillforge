import type { Message, SessionUsage, Status } from "../types";
import ShellIcon from "./ShellIcon";

interface AppStatusFooterProps {
  status: Status | null;
  usage: SessionUsage | null;
  messages: Message[];
  inspectorOpen: boolean;
  onToggleInspector: () => void;
  onOpenContext: () => void;
  onOpenReasoning: () => void;
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`;
  return String(n);
}

function FooterAction({
  icon,
  label,
  value,
  onClick,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="qf-shell-quiet-button inline-flex items-center gap-2 px-3 py-2"
    >
      <span className="text-text-muted">{icon}</span>
      <span className="qf-shell-folio">{label}</span>
      <span className="font-mono text-[11px] text-text">{value}</span>
    </button>
  );
}

export default function AppStatusFooter({
  status,
  usage,
  messages,
  inspectorOpen,
  onToggleInspector,
  onOpenContext,
  onOpenReasoning,
}: AppStatusFooterProps) {
  const totalTokens = usage ? usage.totalInput + usage.totalOutput : 0;
  const contextUsed = status ? status.loreTokens + status.historyTokens : 0;
  const contextPercent = status?.contextLimit
    ? Math.max(0, Math.min(100, Math.round((contextUsed / status.contextLimit) * 100)))
    : 0;
  const reasoningCount = messages.filter(
    (message) => message.role === "assistant" && (message.reasoning || (message.reasoningArtifacts?.length ?? 0) > 0),
  ).length;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 py-3 text-[13px] text-text-muted">
      <div className="flex min-w-0 flex-wrap items-center gap-3">
        <span className="qf-shell-folio">Session</span>
        <span className="truncate text-text">
          {status?.project ?? "unspecified"}
          {status?.file ? ` / ${status.file}` : ""}
        </span>
        <span className="font-mono text-[11px]">
          {status?.conversationTurns ?? 0} turns
        </span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <FooterAction
          icon={<ShellIcon name="context" className="h-4 w-4" />}
          label="Context"
          value={status?.contextLimit ? `${contextPercent}%` : "—"}
          onClick={onOpenContext}
        />
        <FooterAction
          icon={<ShellIcon name="spark" className="h-4 w-4" />}
          label="Reasoning"
          value={String(reasoningCount)}
          onClick={onOpenReasoning}
        />
        <FooterAction
          icon={<ShellIcon name="panel" className="h-4 w-4" />}
          label="Inspector"
          value={inspectorOpen ? "open" : "closed"}
          onClick={onToggleInspector}
        />
      </div>

      <div className="flex flex-wrap items-center gap-3 font-mono text-[11px]">
        <span>
          in <span className="text-text">{formatTokens(usage?.totalInput ?? 0)}</span>
        </span>
        <span>
          out <span className="text-text">{formatTokens(usage?.totalOutput ?? 0)}</span>
        </span>
        <span>
          total <span className="text-text">{formatTokens(totalTokens)}</span>
        </span>
        <span>
          req <span className="text-text">{usage?.totalRequests ?? 0}</span>
        </span>
      </div>
    </div>
  );
}
