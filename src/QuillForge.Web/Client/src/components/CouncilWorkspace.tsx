import { useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import type { ReactNode } from "react";
import { listCouncilMembers, type CouncilMemberInfo } from "../api";
import type { Message, Status } from "../types";
import type { InspectorSection } from "./AppInspector";

interface CouncilWorkspaceProps {
  status: Status | null;
  messages: Message[];
  updateBanner?: ReactNode;
  conversationPane: ReactNode;
  inputBar: ReactNode;
  onOpenSection: (section: InspectorSection) => void;
  onOpenCouncilConfig: () => void;
  onQuickPrompt: (prompt: string) => void;
}

function CouncilQuickButton({
  label,
  onClick,
}: {
  label: string;
  onClick: () => void;
}) {
  return (
    <button type="button" onClick={onClick} className="qf-shell-quiet-button">
      {label}
    </button>
  );
}

export default function CouncilWorkspace({
  status,
  messages,
  updateBanner,
  conversationPane,
  inputBar,
  onOpenSection,
  onOpenCouncilConfig,
  onQuickPrompt,
}: CouncilWorkspaceProps) {
  const [members, setMembers] = useState<CouncilMemberInfo[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadMembers() {
      try {
        const data = await listCouncilMembers();
        if (!cancelled) {
          setMembers(data.members);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load council members");
        }
      }
    }

    void loadMembers();

    return () => {
      cancelled = true;
    };
  }, []);

  const latestUserMessage =
    [...messages].reverse().find((message) => message.role === "user" && message.responseType !== "command")
    ?? [...messages].reverse().find((message) => message.role === "user")
    ?? null;
  const latestAssistantMessage =
    [...messages].reverse().find((message) => message.role === "assistant")
    ?? null;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Council Chamber</div>
            <h1 className="qf-shell-title mt-1">Multi-voice advisory synthesis</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Council should feel like a room of distinct perspectives, not a normal single-thread chat. Keep the roster and latest synthesis visible while the transcript stays available as supporting record.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              advisors · <span className="text-text">{members.length}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              profile · <span className="text-text">{status?.profile ?? "loading"}</span>
            </span>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <CouncilQuickButton label="Advisor Settings" onClick={onOpenCouncilConfig} />
          <CouncilQuickButton label="Sessions" onClick={() => onOpenSection("sessions")} />
          <CouncilQuickButton label="Context" onClick={() => onOpenSection("context")} />
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <CouncilQuickButton
            label="Stress-test this premise."
            onClick={() => onQuickPrompt("Stress-test this premise and tell me where the biggest creative risks are.")}
          />
          <CouncilQuickButton
            label="Argue both sides of this choice."
            onClick={() => onQuickPrompt("Argue both sides of this story choice and tell me what each side gains or loses.")}
          />
          <CouncilQuickButton
            label="Give me three contradictory options."
            onClick={() => onQuickPrompt("Give me three strong contradictory options for what to do next and explain why each one works.")}
          />
        </div>
      </div>

      {updateBanner}

      <div className="grid min-h-0 flex-1 gap-4 p-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(340px,0.82fr)]">
        <section className="qf-shell-card qf-shell-card--sunken min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-6 py-5">
            <div className="qf-shell-folio">Active Roster</div>
            {members.length === 0 ? (
              <p className="mt-2 text-sm leading-6 text-text-muted">
                No council members are configured yet. Open Advisor Settings to create the panel.
              </p>
            ) : (
              <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {members.map((member) => (
                  <div key={member.name} className="qf-shell-card px-4 py-4">
                    <div className="text-sm font-medium text-text">{member.name}</div>
                    <div className="mt-2 text-xs text-text-muted">
                      {(member.providerAlias || "default")}
                      {member.model ? ` / ${member.model}` : ""}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {error && (
              <div className="mt-4 rounded-lg border border-red-400/30 bg-red-400/10 px-4 py-3 text-sm text-red-200">
                {error}
              </div>
            )}
          </div>

          <div className="min-h-0 overflow-y-auto px-6 py-6">
            <div className="grid gap-4 xl:grid-cols-[minmax(260px,0.75fr)_minmax(0,1.15fr)]">
              <article className="qf-shell-card px-5 py-5">
                <div className="qf-shell-folio">Latest Question</div>
                {latestUserMessage ? (
                  <p className="mt-3 text-sm leading-7 text-text">{latestUserMessage.content}</p>
                ) : (
                  <p className="mt-3 text-sm leading-6 text-text-muted">
                    Ask the council a question from the sidecar to pin the current advisory prompt here.
                  </p>
                )}
              </article>

              <article className="qf-shell-card border-accent/10 bg-[color-mix(in_srgb,var(--qf-paper-soft)_96%,transparent)] px-5 py-5">
                <div className="qf-shell-folio">Latest Synthesis</div>
                {latestAssistantMessage ? (
                  <div className="prose prose-invert prose-sm prose-themed mt-3 max-w-none [&_p]:mb-3 [&_p:last-child]:mb-0">
                    <ReactMarkdown>{latestAssistantMessage.content}</ReactMarkdown>
                  </div>
                ) : (
                  <p className="mt-3 text-sm leading-6 text-text-muted">
                    Council answers will surface here as a report-like synthesis instead of being buried as just another bubble in the transcript.
                  </p>
                )}
              </article>
            </div>
          </div>
        </section>

        <aside className="qf-shell-card min-h-0 overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Chamber Transcript</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              The full back-and-forth still matters, but it now supports the roster and synthesis instead of owning the whole page.
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
