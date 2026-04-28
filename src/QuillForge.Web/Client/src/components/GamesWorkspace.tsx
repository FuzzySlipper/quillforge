import { useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import {
  abortGame,
  endGame,
  getGameView,
  listGameTemplates,
  postGamePublicMessage,
  startGameFromTemplate,
  submitGameAction,
} from "../api";
import type {
  GameBridgeView,
  GameTemplateSummary,
  ParticipantFeedEntry,
  PendingInputState,
  VisibleGameEvent,
} from "../types";
import type { InspectorSection } from "./AppInspector";
import WorkspaceQuickButton from "./WorkspaceQuickButton";

interface GamesWorkspaceProps {
  sessionId?: string | null;
  sending: boolean;
  updateBanner?: ReactNode;
  onOpenMode: () => void;
  onOpenSection: (section: InspectorSection) => void;
  onRefresh: (sessionId?: string | null) => void;
  onNewSession: () => Promise<void>;
}

function ActionButton({
  label,
  disabled,
  emphasis = "default",
  onClick,
}: {
  label: string;
  disabled?: boolean;
  emphasis?: "default" | "accent" | "subtle" | "danger";
  onClick: () => void;
}) {
  const className =
    emphasis === "accent"
      ? "rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast transition-colors hover:bg-accent-hover disabled:opacity-50"
      : emphasis === "danger"
        ? "rounded-lg bg-danger-strong px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-danger-strong-hover disabled:opacity-50"
        : emphasis === "subtle"
          ? "rounded-lg bg-surface px-4 py-2 text-sm font-medium text-text-muted transition-colors hover:bg-surface-alt hover:text-text disabled:opacity-50"
          : "rounded-lg bg-surface-alt px-4 py-2 text-sm font-medium text-text transition-colors hover:bg-border disabled:opacity-50";

  return (
    <button type="button" disabled={disabled} onClick={onClick} className={className}>
      {label}
    </button>
  );
}

function valueOf(identifier: { value: string } | string | null | undefined): string {
  if (!identifier) return "";
  return typeof identifier === "string" ? identifier : identifier.value;
}

function formatTime(value: string): string {
  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) return value;
  return new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" }).format(parsed);
}

function feedKindLabel(kind: ParticipantFeedEntry["kind"]): string {
  if (kind === 0 || kind === "PublicChannelMessage") return "PublicChannelMessage";
  if (kind === 1 || kind === "DirectMessage") return "DirectMessage";
  if (kind === 2 || kind === "GameEventLink") return "GameEventLink";
  return String(kind);
}

function FeedEntry({ entry }: { entry: ParticipantFeedEntry }) {
  const author = entry.author ? valueOf(entry.author.participantId) : "system";
  const text = entry.text ?? entry.summary ?? entry.gameEventId ?? "Game event";
  const recipients = entry.recipientParticipantIds.map(valueOf).filter(Boolean);
  const kind = feedKindLabel(entry.kind);

  return (
    <div className="qf-shell-card px-4 py-3 text-sm">
      <div className="flex items-center justify-between gap-3 text-[11px] text-text-muted">
        <span className="qf-shell-folio">{kind}</span>
        <span>{formatTime(entry.createdAt)} · seq {entry.sequence}</span>
      </div>
      <div className="mt-2 leading-6 text-text">{text}</div>
      <div className="mt-2 text-xs text-text-muted">
        {kind === "DirectMessage" ? `DM from ${author} to ${recipients.join(", ") || "recipient"}` : `from ${author}`}
      </div>
    </div>
  );
}

function EngineEventEntry({ event }: { event: VisibleGameEvent }) {
  return (
    <div className="qf-shell-card px-4 py-3 text-sm">
      <div className="flex items-center justify-between gap-3 text-[11px] text-text-muted">
        <span className="qf-shell-folio">Engine fact</span>
        <span>seq {event.sequence}</span>
      </div>
      <div className="mt-2 text-text">{event.eventType}</div>
      <div className="mt-1 break-all text-xs text-text-muted">{valueOf(event.eventId)}</div>
    </div>
  );
}

function PendingInputCard({
  input,
  disabled,
  onSubmit,
}: {
  input: PendingInputState;
  disabled?: boolean;
  onSubmit: (pendingInputId: string, choiceName: string) => void;
}) {
  const pendingInputId = valueOf(input.pendingInputId);

  return (
    <div className="qf-shell-card border-accent/30 bg-accent/10 px-4 py-4">
      <div className="qf-shell-folio">Pending action</div>
      <div className="mt-2 text-sm font-medium text-text">{input.intentName}</div>
      <div className="mt-1 text-xs text-text-muted">stage · {valueOf(input.stageId)}</div>
      <div className="mt-3 flex flex-col gap-2">
        {input.legalOptions.map((option) => (
          <button
            key={`${pendingInputId}:${option.intentName}`}
            type="button"
            disabled={disabled}
            onClick={() => onSubmit(pendingInputId, option.intentName)}
            className="rounded-lg border border-border bg-surface-alt px-3 py-2 text-left text-sm text-text transition-colors hover:border-accent/40 hover:bg-border disabled:opacity-50"
          >
            <span className="block font-medium">{option.displayName || option.intentName}</span>
            {option.description && (
              <span className="mt-1 block text-xs leading-5 text-text-muted">{option.description}</span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}

export default function GamesWorkspace({
  sessionId,
  sending,
  updateBanner,
  onOpenMode,
  onOpenSection,
  onRefresh,
  onNewSession,
}: GamesWorkspaceProps) {
  const [templates, setTemplates] = useState<GameTemplateSummary[]>([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [view, setView] = useState<GameBridgeView | null>(null);
  const [participantId, setParticipantId] = useState<string | null>(null);
  const [publicMessage, setPublicMessage] = useState("");
  const [loading, setLoading] = useState(false);
  const [mutating, setMutating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadTemplates() {
      try {
        const data = await listGameTemplates();
        if (cancelled) return;
        setTemplates(data.templates);
        setSelectedTemplateId((previous) => previous || data.templates[0]?.templateId || "");
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load game templates");
        }
      }
    }

    void loadTemplates();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function loadGame() {
      if (!sessionId) {
        setView(null);
        setParticipantId(null);
        return;
      }

      setLoading(true);
      setError(null);
      try {
        const publicView = (await getGameView(sessionId)).view;
        if (cancelled) return;

        const preferredParticipant = participantId
          ?? publicView.roster.find((participant) => participant.kind === "Human")?.participantId
          ?? publicView.roster[0]?.participantId
          ?? null;

        if (preferredParticipant) {
          const playerView = (await getGameView(sessionId, preferredParticipant)).view;
          if (!cancelled) {
            setParticipantId(preferredParticipant);
            setView(playerView);
          }
        } else {
          setView(publicView);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load game view");
          setView(null);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadGame();

    return () => {
      cancelled = true;
    };
  }, [sessionId, participantId]);

  const activeView = view;
  const hasActiveGame = !!activeView?.gameInstanceId
    && activeView.status !== "NotStarted"
    && activeView.status !== "Ended"
    && activeView.status !== "Aborted";
  const currentPlayer = activeView?.player ?? null;
  const visibleFeed = currentPlayer?.feed ?? activeView?.public.feed ?? [];
  const narration = activeView?.public.narration ?? [];
  const playerEvents = currentPlayer?.engineEvents ?? [];
  const pendingInputs = currentPlayer?.pendingInputs ?? [];
  const selectedTemplate = useMemo(
    () => templates.find((template) => template.templateId === selectedTemplateId) ?? null,
    [templates, selectedTemplateId],
  );

  async function refreshGame(nextParticipantId = participantId) {
    if (!sessionId) return;
    const response = await getGameView(sessionId, nextParticipantId);
    setView(response.view);
    onRefresh(sessionId);
  }

  async function withMutation(action: () => Promise<void>) {
    setMutating(true);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Game operation failed");
    } finally {
      setMutating(false);
    }
  }

  async function handleStartGame() {
    if (!sessionId || !selectedTemplateId) return;
    await withMutation(async () => {
      const result = await startGameFromTemplate(sessionId, selectedTemplateId, "You");
      const humanParticipant = result.view.roster.find((participant) => participant.kind === "Human")?.participantId
        ?? result.view.player?.participantId
        ?? null;
      setParticipantId(humanParticipant);
      setView(result.view);
      onRefresh(sessionId);
      if (humanParticipant) {
        await refreshGame(humanParticipant);
      }
    });
  }

  async function handleSubmitAction(pendingInputId: string, choiceName: string) {
    if (!sessionId || !currentPlayer) return;
    await withMutation(async () => {
      const result = await submitGameAction(sessionId, currentPlayer.participantId, pendingInputId, choiceName);
      setView(result.view);
      onRefresh(sessionId);
    });
  }

  async function handlePostMessage() {
    if (!sessionId || !currentPlayer || !publicMessage.trim()) return;
    await withMutation(async () => {
      const result = await postGamePublicMessage(sessionId, currentPlayer.participantId, publicMessage.trim());
      setPublicMessage("");
      setView(result.view);
      onRefresh(sessionId);
    });
  }

  async function handleEndGame() {
    if (!sessionId) return;
    await withMutation(async () => {
      const result = await endGame(sessionId);
      setView(result.view);
      onRefresh(sessionId);
    });
  }

  async function handleAbortGame() {
    if (!sessionId) return;
    await withMutation(async () => {
      const result = await abortGame(sessionId);
      setView(result.view);
      onRefresh(sessionId);
    });
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/70 px-6 py-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="qf-shell-folio">Games Table</div>
            <h1 className="qf-shell-title mt-1">{activeView?.templateId ?? "Games workspace"}</h1>
            <p className="qf-shell-subtitle mt-2 max-w-3xl">
              Games mode is a typed table surface. Public feed, private player information, pending actions, and game controls come from game endpoints rather than narrator text.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
            <span className="qf-shell-card px-3 py-1.5">
              status · <span className="text-text">{activeView?.status ?? "NotStarted"}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              stage · <span className="text-text">{activeView?.stageName ?? "none"}</span>
            </span>
            <span className="qf-shell-card px-3 py-1.5">
              round · <span className="text-text">{activeView?.roundNumber ?? 0}</span>
            </span>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <WorkspaceQuickButton label="Mode Menu" onClick={onOpenMode} />
          <WorkspaceQuickButton label="Sessions" onClick={() => onOpenSection("sessions")} />
          <WorkspaceQuickButton label="Context" onClick={() => onOpenSection("context")} />
          <WorkspaceQuickButton label="Refresh Table" onClick={() => { void refreshGame(); }} />
        </div>
      </div>

      {updateBanner}

      <div className="grid min-h-0 flex-1 gap-4 overflow-hidden p-4 xl:grid-cols-[minmax(280px,0.72fr)_minmax(0,1.18fr)_minmax(340px,0.82fr)]">
        <aside className="qf-shell-card flex min-h-0 flex-col overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Setup & Roster</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Start from a saved template, then pick which visible participant view to inspect. Roster data is typed endpoint state, not inferred prose.
            </p>
          </div>

          <div className="border-b border-border/50 px-4 py-4">
            {!sessionId ? (
              <div className="flex flex-col gap-3">
                <div className="text-sm leading-6 text-text-muted">Create a session before starting a game.</div>
                <ActionButton label="New session" emphasis="accent" onClick={() => { void onNewSession(); }} />
              </div>
            ) : hasActiveGame ? (
              <div className="flex flex-wrap gap-2">
                <ActionButton label="End" disabled={mutating} onClick={() => { void handleEndGame(); }} />
                <ActionButton label="Abort" disabled={mutating} emphasis="danger" onClick={() => { void handleAbortGame(); }} />
              </div>
            ) : (
              <div className="flex flex-col gap-3">
                <label className="flex flex-col gap-1 text-sm">
                  <span className="text-text-muted">Template</span>
                  <select
                    value={selectedTemplateId}
                    onChange={(event) => setSelectedTemplateId(event.target.value)}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-text"
                  >
                    <option value="">Select template...</option>
                    {templates.map((template) => (
                      <option key={template.templateId} value={template.templateId}>{template.displayName}</option>
                    ))}
                  </select>
                </label>
                {selectedTemplate && (
                  <div className="text-xs leading-5 text-text-muted">
                    {selectedTemplate.moduleId} · {selectedTemplate.minimumModuleVersion}–{selectedTemplate.maximumModuleVersion}
                  </div>
                )}
                <ActionButton
                  label="Start game"
                  disabled={mutating || !selectedTemplateId}
                  emphasis="accent"
                  onClick={() => { void handleStartGame(); }}
                />
              </div>
            )}
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
            <div className="qf-shell-folio mb-3">Participants</div>
            {!activeView || activeView.roster.length === 0 ? (
              <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                No participants yet. Start a game from a template to seat the table.
              </div>
            ) : (
              <div className="flex flex-col gap-2">
                {activeView.roster.map((participant) => {
                  const selected = participant.participantId === participantId;
                  return (
                    <button
                      type="button"
                      key={participant.participantId}
                      onClick={() => setParticipantId(participant.participantId)}
                      className={`qf-shell-card px-4 py-4 text-left transition-colors ${
                        selected ? "border-accent/50 bg-accent/10" : "hover:border-accent/40"
                      }`}
                    >
                      <div className="flex items-center justify-between gap-3">
                        <span className="font-medium text-text">{participant.displayName}</span>
                        {selected && <span className="qf-shell-folio">view</span>}
                      </div>
                      <div className="mt-2 text-xs text-text-muted">
                        {participant.kind} · {participant.isJoined ? "joined" : "not joined"}
                      </div>
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        </aside>

        <section className="qf-shell-card qf-shell-card--sunken flex min-h-0 flex-col overflow-hidden">
          <div className="border-b border-border/60 px-6 py-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0">
                <div className="qf-shell-folio">Table State</div>
                <h2 className="mt-1 text-2xl font-semibold text-text">
                  {hasActiveGame ? `${activeView?.stageName ?? "Game"} · round ${activeView?.roundNumber ?? 0}` : "No game active"}
                </h2>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-text-muted">
                  The center pane shows public narration facts and visible feed entries. Hidden/system-only facts never enter this UI path.
                </p>
              </div>
            </div>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-6 py-6">
            {error && (
              <div role="alert" className="mb-4 rounded-lg border border-danger-border bg-danger-soft px-4 py-3 text-sm text-danger-text">
                {error}
              </div>
            )}

            {loading && (
              <div className="mb-4 text-sm text-text-muted">Loading game table...</div>
            )}

            {!hasActiveGame ? (
              <div className="qf-shell-card border-dashed px-6 py-10 text-center text-text-muted" data-testid="games-no-game-state">
                <p className="text-base text-text">No game is running in this session.</p>
                <p className="mt-2 text-sm leading-6">
                  Choose a saved template from Setup & Roster to create the first table. The workspace will then show status, stage, public feed, private player information, pending actions, roster, and host controls here.
                </p>
              </div>
            ) : (
              <div className="grid gap-6 xl:grid-cols-2">
                <section>
                  <div className="qf-shell-folio mb-3">Public narration</div>
                  {narration.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      Public engine facts will appear here as the rules engine commits visible events.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      {narration.map((entry) => (
                        <div key={entry.eventId} className="qf-shell-card px-4 py-3 text-sm">
                          <div className="flex items-center justify-between gap-3 text-[11px] text-text-muted">
                            <span className="qf-shell-folio">{entry.eventType}</span>
                            <span>seq {entry.sequence}</span>
                          </div>
                          <div className="mt-2 leading-6 text-text">{entry.text}</div>
                        </div>
                      ))}
                    </div>
                  )}
                </section>

                <section>
                  <div className="qf-shell-folio mb-3">Visible feed</div>
                  {visibleFeed.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      Channel messages, direct messages visible to the selected participant, and public game-event links appear here.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      {visibleFeed.map((entry) => <FeedEntry key={`${entry.kind}:${entry.sequence}:${entry.messageId ?? entry.linkId ?? entry.gameEventId}`} entry={entry} />)}
                    </div>
                  )}
                </section>
              </div>
            )}
          </div>
        </section>

        <aside className="qf-shell-card flex min-h-0 flex-col overflow-hidden">
          <div className="border-b border-border/60 px-4 py-4">
            <div className="qf-shell-folio">Player Panel</div>
            <p className="mt-2 text-sm leading-6 text-text-muted">
              Private info and legal actions are scoped to the selected participant projection. Use these controls instead of typing gameplay into chat.
            </p>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
            {!currentPlayer ? (
              <div className="qf-shell-card border-dashed px-4 py-5 text-sm leading-6 text-text-muted">
                Select a seated participant to see their private projection and pending actions.
              </div>
            ) : (
              <div className="flex flex-col gap-5">
                <div>
                  <div className="qf-shell-folio mb-3">Private info</div>
                  {playerEvents.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      No private engine facts are visible to {currentPlayer.displayName} yet.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      {playerEvents.map((event) => <EngineEventEntry key={valueOf(event.eventId)} event={event} />)}
                    </div>
                  )}
                </div>

                <div>
                  <div className="qf-shell-folio mb-3">Pending actions</div>
                  {pendingInputs.length === 0 ? (
                    <div className="qf-shell-card border-dashed px-4 py-4 text-sm leading-6 text-text-muted">
                      No legal action is waiting for {currentPlayer.displayName}.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-3">
                      {pendingInputs.map((input) => (
                        <PendingInputCard
                          key={valueOf(input.pendingInputId)}
                          input={input}
                          disabled={mutating || sending}
                          onSubmit={(pendingInputId, choiceName) => { void handleSubmitAction(pendingInputId, choiceName); }}
                        />
                      ))}
                    </div>
                  )}
                </div>

                <div>
                  <div className="qf-shell-folio mb-3">Public channel</div>
                  <div className="flex gap-2">
                    <input
                      value={publicMessage}
                      onChange={(event) => setPublicMessage(event.target.value)}
                      placeholder="Post a table message..."
                      className="min-w-0 flex-1 rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text focus:border-accent focus:outline-none"
                    />
                    <ActionButton
                      label="Post"
                      disabled={mutating || !publicMessage.trim()}
                      onClick={() => { void handlePostMessage(); }}
                    />
                  </div>
                </div>
              </div>
            )}
          </div>
        </aside>
      </div>
    </div>
  );
}
