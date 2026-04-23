import type { Message } from "../types";
import Overlay from "./Overlay";
import ReasoningDetails from "./ReasoningDetails";

interface ShellReasoningOverlayProps {
  open: boolean;
  onClose: () => void;
  messages: Message[];
}

export default function ShellReasoningOverlay({
  open,
  onClose,
  messages,
}: ShellReasoningOverlayProps) {
  const reasoningMessages = messages
    .filter(
      (message) =>
        message.role === "assistant"
        && (message.reasoning || (message.reasoningArtifacts?.length ?? 0) > 0),
    )
    .slice(-8)
    .reverse();

  return (
    <Overlay open={open} onClose={onClose} title="Reasoning">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-text-muted">
          Recent assistant responses that exposed provider or multi-agent reasoning.
        </p>

        {reasoningMessages.length === 0 ? (
          <p className="text-sm text-text-muted">
            No reasoning has been captured in this session.
          </p>
        ) : (
          reasoningMessages.map((message, index) => (
            <div key={message.id} className="rounded-lg border border-border/60 bg-input-bg/40 p-3">
              <div className="mb-2 flex items-center justify-between gap-3 text-[11px] uppercase tracking-wider text-text-muted/70">
                <span>Response {reasoningMessages.length - index}</span>
                <span className="font-mono normal-case tracking-normal">
                  {new Date(message.timestamp).toLocaleTimeString()}
                </span>
              </div>

              <div className="mb-3 line-clamp-3 text-sm text-text/80">
                {message.content}
              </div>

              <ReasoningDetails
                reasoning={message.reasoning}
                artifacts={message.reasoningArtifacts}
                summaryClassName="cursor-pointer text-xs text-accent hover:text-accent-hover"
                panelClassName="mt-2 rounded-lg border border-border/40 bg-surface-alt/40 px-3 py-3"
                preClassName="whitespace-pre-wrap text-[12px] leading-relaxed text-text/80"
                selectClassName="text-[11px] bg-surface border border-border/50 rounded px-2 py-1 text-text focus:outline-none focus:border-accent"
              />
            </div>
          ))
        )}
      </div>
    </Overlay>
  );
}
