import type { GameBridgeView, ParticipantFeedEntry, PendingInputState } from "../types";

function feedText(entry: ParticipantFeedEntry): string {
  return entry.summary ?? entry.text ?? entry.gameEventId ?? "";
}

function stageGuidance(stageId: string | null, stageName: string | null): string {
  if (stageId === "night") return "Night: review private role information and resolve any night-only prompts.";
  if (stageId === "day-discussion") return "Day discussion: use the public feed to discuss suspicions before voting.";
  if (stageId === "voting") return "Voting: choose an active participant to eliminate or abstain from the pending action card.";
  return stageName ? `${stageName}: follow the visible pending actions from the rules engine.` : "Werewolf game state is loading.";
}

function classifyPending(input: PendingInputState): string {
  if (input.stageId.value === "voting" || input.intentName.toLowerCase().includes("vote")) return "Vote";
  if (input.stageId.value === "night") return "Night prompt";
  return "Action";
}

export function WerewolfGamePanel({ view }: { view: GameBridgeView }) {
  if (view.moduleId !== "werewolf") {
    return null;
  }

  const playerFeed = view.player?.feed ?? [];
  const publicFeed = view.public.feed;
  const roleLines = playerFeed
    .map(feedText)
    .filter(text => text.startsWith("Your role is") || text.startsWith("Werewolf teammates:"));
  const outcomeLines = view.public.narration
    .map(entry => entry.text)
    .filter(text => text.includes(" win:") || text.startsWith("Game ended:"));
  const recentWerewolfFacts = [...publicFeed, ...playerFeed]
    .map(feedText)
    .filter(text => text.includes("Night")
      || text.includes("Day discussion")
      || text.includes("Voting")
      || text.includes("voted for")
      || text.includes("eliminated")
      || text.includes("Your role is")
      || text.includes("Werewolf teammates"))
    .slice(-8);
  const pending = view.player?.pendingInputs ?? [];

  return (
    <section className="qf-shell-card border-accent/30 bg-accent/5 px-4 py-4" data-testid="werewolf-panel">
      <div className="qf-shell-folio">Werewolf Table</div>
      <div className="mt-2 text-sm leading-6 text-text-muted">
        Werewolf labels are display-only projections from typed engine facts. They do not change rules or game state.
      </div>

      <div className="mt-4 grid gap-3 md:grid-cols-2">
        <div className="qf-shell-card px-4 py-3">
          <div className="qf-shell-folio">Stage</div>
          <div className="mt-2 text-base font-medium text-text" data-testid="werewolf-stage-label">
            {view.stageName ?? "Unknown"} · round {view.roundNumber ?? 0}
          </div>
          <p className="mt-2 text-sm leading-6 text-text-muted">{stageGuidance(view.stageId, view.stageName)}</p>
        </div>

        <div className="qf-shell-card px-4 py-3">
          <div className="qf-shell-folio">Private role view</div>
          {roleLines.length === 0 ? (
            <p className="mt-2 text-sm leading-6 text-text-muted">No role information is visible to the selected participant.</p>
          ) : (
            <ul className="mt-2 flex flex-col gap-2 text-sm text-text" data-testid="werewolf-role-info">
              {roleLines.map(line => <li key={line}>{line}</li>)}
            </ul>
          )}
        </div>
      </div>

      <div className="mt-4 grid gap-3 md:grid-cols-2">
        <div className="qf-shell-card px-4 py-3">
          <div className="qf-shell-folio">Werewolf prompts</div>
          {pending.length === 0 ? (
            <p className="mt-2 text-sm leading-6 text-text-muted">No Werewolf action is waiting for this participant.</p>
          ) : (
            <div className="mt-2 flex flex-col gap-2" data-testid="werewolf-pending-prompts">
              {pending.map(input => (
                <div key={input.pendingInputId.value} className="rounded-lg border border-border bg-surface-alt px-3 py-2 text-sm">
                  <div className="font-medium text-text">{classifyPending(input)} · {input.intentName}</div>
                  <div className="mt-1 text-xs text-text-muted">{input.legalOptions.length} legal option(s)</div>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="qf-shell-card px-4 py-3">
          <div className="qf-shell-folio">Outcome</div>
          {outcomeLines.length === 0 ? (
            <p className="mt-2 text-sm leading-6 text-text-muted">No win condition has resolved yet.</p>
          ) : (
            <ul className="mt-2 flex flex-col gap-2 text-sm text-text" data-testid="werewolf-outcome">
              {outcomeLines.map(line => <li key={line}>{line}</li>)}
            </ul>
          )}
        </div>
      </div>

      <div className="mt-4 qf-shell-card px-4 py-3">
        <div className="qf-shell-folio">Recent Werewolf projection</div>
        {recentWerewolfFacts.length === 0 ? (
          <p className="mt-2 text-sm leading-6 text-text-muted">Werewolf-specific public/private facts will appear as the engine commits them.</p>
        ) : (
          <ul className="mt-2 flex flex-col gap-1 text-sm leading-6 text-text" data-testid="werewolf-recent-facts">
            {recentWerewolfFacts.map((line, index) => <li key={`${index}:${line}`}>{line}</li>)}
          </ul>
        )}
      </div>
    </section>
  );
}
