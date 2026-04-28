# Multi-Agent Game Harness Trace Artifacts

Status: Implemented for task #846.

## Purpose

The multi-agent game harness creates repeatable synthetic game runs and stores stable trace artifacts for debugging agent/player behavior. It mirrors the existing provider harness pattern: a scenario runner drives QuillForge-owned services, records structured evidence, writes JSON plus Markdown reports, and keeps trace data in a test artifact directory rather than session runtime state.

The harness is intended for social games where the rules engine is authoritative and QuillForge coordinates participants, prompts, memory, communication, and UI projections.

## Determinism Layer

Regression scenarios use **prompt-level deterministic scripted completion sources**. The fake completion source receives the same prompt shape that a game agent or memory summarizer receives, then returns scripted JSON responses. These runs are deterministic enough for unit/integration assertions over:

- prompt delivery cursors
- visible event/feed projection boundaries
- selected action names
- memory summary decisions
- token usage reported by the scripted source
- final engine outcome

Optional live-provider exploratory runs are intentionally nondeterministic. `HarnessGameScenarioRunner.RunWerewolfExploratoryNightAsync` accepts an `ICompletionService` and caller-supplied template, so local/manual harness code can use real configured provider aliases when keys are available. Their nondeterminism belongs in the trace artifact; tests should not assert golden semantic outcomes for live model behavior.

## Artifact Shape

A game harness report is written through `HarnessRunArtifactStore` under the configured harness run root. The default test root is a temp directory, matching the provider harness retention style.

Important files:

- `run-manifest.json` — run id, scenario name, directories, provider trace references when present.
- `app/{scenario-name}-trace.json` — machine-readable game trace.
- `reports/{scenario-name}-capture.json` — report metadata and usage summary.
- `reports/{scenario-name}-summary.md` — human-readable summary with determinism note, outcome, usage, and findings section.

The game trace captures:

- scenario name, run id, determinism mode, live-provider flag, session id, template/module ids, seed, status, stage, round, and final outcome.
- per-agent display name, provider alias, model, prompt cursor, and memory state.
- prompt envelopes with provider/model, prompt/response tokens, cursor values, content hashes, and short previews.
- chosen actions with participant, pending input id, outcome, reason code, provider/model, choice, and token usage.
- memory summaries with participant, round, outcome, decision id, provider/model, token usage, trim/retry/refusal fields, and summary hash.
- public feed plus per-participant private events and private feed entries.
- engine/runtime events normalized into greppable event names and reason codes.
- failure-surface taxonomy: `AgentResponseRejected`, `NoActionTaken`, and memory decision flags such as `ExceededTokenBudget`, `Trimmed`, `Retried`, and `RejectionReason`.

## Current Scenarios

`HarnessGameScenarioRunner` currently provides two scripted Werewolf scenarios plus one optional live-provider exploratory night run:

1. `game-werewolf-village-win`
   - starts a Werewolf template with four scripted agent participants.
   - requests night actions and voting actions through the runtime/rules-engine boundary.
   - scripts one parse failure to exercise `AgentResponseRejected` and `NoActionTaken` trace capture.
   - scripts the table to eliminate the active werewolf and records final `villagers_win` outcome.
   - records public/private feed projections so role reveal leakage is easy to inspect.

2. `game-werewolf-round-memory`
   - starts a Werewolf template, resolves night actions, posts a public table-talk message, records a round boundary, and runs round-end memory summaries.
   - uses a deliberately small memory budget so memory trim flags are present in the trace.
   - records memory cursors, summaries, token usage, and prompt envelopes after the round boundary.

3. `game-werewolf-live-exploratory-night`
   - available through `RunWerewolfExploratoryNightAsync` for manual/live-provider exploration.
   - uses the caller-provided `ICompletionService` and `GameTemplate`, so configured provider aliases/models come from the supplied template.
   - captures the same trace shape but marks `liveProviderRun: true` and `determinismMode: live-provider-exploratory`.

## Running The Harness Tests

```bash
dotnet test tests/QuillForge.ProviderHarness.Tests/QuillForge.ProviderHarness.Tests.csproj \
  -p:AllowMissingPrunePackageData=true \
  --filter HarnessGameScenarioTests
```

The generated temp run directory is available from the failing test output when assertions fail. For ad-hoc debugging, set a breakpoint or log the `HarnessRunArtifactStore.RunDirectory` value in the test.

## Interpreting Model Differences

For deterministic scripted runs, differences in action or memory traces are regressions unless the scenario fixture changed intentionally.

For future live-provider exploratory runs:

- compare provider/model metadata first so runs are not mistaken for equivalent samples.
- inspect prompt cursor fields to see which public engine events, private event ids, communication feed entries, and memory revisions the agent was shown.
- compare `PromptPreview`/`ResponsePreview` hashes and token usage to identify prompt drift or truncation.
- treat `AgentResponseRejected`, `NoActionTaken`, `model-refusal`, `parse-fail`, `schema-fail`, `illegal-action`, memory `Trimmed`, and memory `RejectionReason` as first-class failure signals.
- use final outcome as a benchmark clue, not proof that a live provider behaved correctly or incorrectly.

## Boundary Rules

- The harness does not mutate `SessionState` or game runtime state directly; it drives `IGameBridgeService`, `IGameRuntimeService`, `IGameAgentTurnService`, and `IGameAgentMemoryService`.
- Rules remain authoritative in `Den.RulesEngine` / `Den.RulesEngine.Werewolf`.
- Narration and trace text are display/debug artifacts only.
- Scripted completion sources are test fakes, not production providers.
