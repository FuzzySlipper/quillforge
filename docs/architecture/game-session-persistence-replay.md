# Game Session Persistence, Replay, and Inspector Notes

Status: Implemented for task #847.

## Persistence Ownership

Active game runtime state is session-owned. It persists with `SessionState.Game` under:

```text
user/data/session-state/{sessionId}.json
```

The persisted game substate includes the rules-engine snapshot, event journal, participant communication state, delivery cursors, prompt cursors, agent memories, memory decisions, prompt envelopes, and host records. Services mutate this state through `IGameRuntimeService`; endpoints and UI surfaces do not write it directly.

## Resume Semantics

Reloading a session restores the current game snapshot as-is. Pending human inputs and pending agent inputs remain pending after load. A resumed process can submit the human action through the game runtime/bridge service and can run agent turns through `IGameAgentTurnService` using the restored prompt cursors, memory state, and event journal.

No separate game runtime file is created in v1; game runtime follows the owning session lifecycle.

## Fork/Delete Semantics

Forking a session clones the current game runtime shape into the forked session state and appends a fork host record. This is branch creation, not historical replay from the selected message. The forked game therefore starts from the source session's current game snapshot unless a future task defines narrower reset rules.

Deleting a session deletes the owned session unit, including the persisted game substate, by deleting the session runtime document alongside the conversation tree. Message deletion alone does not reset or rewrite game runtime state.

## Replay Determinism Layer

Task #847 covers **engine replay determinism**: fixed seed plus the same ordered engine commands should produce comparable event journal signatures. `GameEventJournalReplayComparer` compares journal event signatures by sequence, type, visibility, participant/pending-input ids, reason codes, choices, and outcomes for core event families.

Prompt-level determinism is covered by the multi-agent harness from task #846. Live-provider runs remain nondeterministic and should be interpreted through trace artifacts.

## Inspector Projection

`GameInspectorProjection` is the one-stop debug surface for investigating weird game/agent behavior. It exposes:

- session/game/module identity and seed
- engine status, round, stage, pending inputs, and safe event-journal summaries
- participant bindings with provider/model metadata
- per-agent prompt delivery cursors
- per-participant event delivery cursors
- agent memory snapshots
- last N prompt envelopes with cursor values, token counts, hashes, and short prompt/response previews
- session token usage from `ITokenUsageTracker`

The inspector intentionally summarizes event journal entries instead of serializing raw typed engine event payloads. This keeps public/status/session-load style contracts from exposing hidden module fields such as private roles or night-action payloads by accident. Debug-only memory summaries and prompt previews are still exposed because they are required to investigate agent behavior.

HTTP access is available at:

```text
GET /api/sessions/{sessionId}/game/inspector?promptEnvelopeLimit=10
```

## Public/Private Visibility Rule

Public game views continue to use public projections and public communication feed entries. Participant-specific views use participant projections. Session load and inspector event-journal summaries must not expose raw private engine event payloads; tests lock this by checking that private choices are not serialized into inspector event summaries.
