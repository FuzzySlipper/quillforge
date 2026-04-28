# Social Games Setup, Testing, and Extension Workflow

Status: Implemented for task #848.

This guide is the user/developer entry point for QuillForge social games. It complements the deeper architecture notes in:

- [`docs/architecture/social-games-framework-architecture.md`](architecture/social-games-framework-architecture.md)
- [`docs/architecture/game-module-authoring-hooks.md`](architecture/game-module-authoring-hooks.md)
- [`docs/architecture/game-session-persistence-replay.md`](architecture/game-session-persistence-replay.md)
- [`docs/architecture/game-harness-trace-artifacts.md`](architecture/game-harness-trace-artifacts.md)
- [`docs/architecture/werewolf-scenario-regression-notes.md`](architecture/werewolf-scenario-regression-notes.md)

## User Workflow: Create and Start a Werewolf Game

### 1. Configure at least one provider

Agent players use the same provider registry as the rest of QuillForge. Configure providers from Provider Manager or the provider API before assigning agent seats.

Common local setup:

```bash
curl -X POST http://localhost:5204/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias":"local","type":"Ollama","baseUrl":"http://localhost:11434","defaultModel":"qwen2.5:14b"}'
```

Provider/model choices for game agents are stored per game template roster entry. Templates select from existing configured providers; they do not create new global provider/model assignment slots.

### 2. Open Games mode

Switch to **Games** mode. The workspace is a typed table surface:

- left pane: saved templates, template editor, roster, start/end/abort controls
- center pane: public narration and public/feed projections
- right pane: selected participant private view, visible engine facts, pending actions, public channel
- Werewolf-specific panel: role/team/stage/outcome projections, shown only for the `werewolf` module

Mode switching away from Games is rejected while a game is active so the active table is not silently abandoned.

### 3. Create or edit a template

Use the **Game Template Editor** in Games mode.

Core fields:

- **Template id/display name/description** — durable template identity and user-facing label.
- **Module** — currently `werewolf`, explicitly registered by module id/version.
- **Rules options** — rendered from the module setup schema. Werewolf currently exposes:
  - `werewolf_count`
  - `seer_enabled`
  - `one_night_compatible` (documents compatibility intent; specialized One Night mechanics remain follow-up work)
- **Roster size and user seat** — choose whether a human participant is seated and which participant id represents the user.
- **Agent players** — assign participant ids, provider aliases, optional model overrides, fixed names, character prompts, and personalities.
- **Memory token budget** — controls round-end memory summary budget for agent participants.
- **Communication toggles** — public channel / direct messages / host messages. The effective permission is the intersection of template setting, module capability, and active stage permission.

Validation is split by ownership:

- template-side checks validate template shape, provider aliases, and roster config;
- module-side checks validate rule options, player counts, participant requirements, required prompt assets, and module-specific setup constraints.

The UI surfaces validation issues returned by `GameTemplateService`; it should not duplicate hidden rule logic.

### 4. Save, select, and start

After the template validates:

1. Save it.
2. Select it in **Setup & Roster**.
3. Create a session if none exists.
4. Click **Start game**.

The bridge starts a session-owned game runtime from the template, creates a rules-engine instance with the selected module/version, stores the seed, and persists the runtime under:

```text
<content-root>/data/session-state/{sessionId}.json
```

The game can be reloaded and resumed with pending human or agent inputs intact.

### 5. Play and inspect

For human actions, select the participant view and use the pending action cards. The cards are rendered from typed pending inputs plus module action-form metadata; they submit typed `choiceName` actions to the bridge.

For table talk, use the public channel when the template/module/stage allow it. Direct-message state is modeled generically and is only visible to authorized participants. Display-only game-event links may appear in feeds; they do not become new gameplay authority.

Agent turn and memory automation is currently exercised by Core services and the provider harness. The important boundaries are:

- `IGameAgentTurnService` invokes configured providers for pending agent inputs.
- `IGameAgentMemoryService` runs round-end memory summaries.
- prompt delivery cursors prevent repeatedly dumping the full public/private history.
- invalid/unparseable agent responses are recorded as typed failure events instead of being treated as legal choices.

## Architecture Summary

Social games have four main layers.

### Portable engine: `Den.RulesEngine`

Owns deterministic gameplay authority:

- `RulesGameState` live aggregate
- `RulesGameStateSnapshot` persisted copy
- `RulesEngineService` typed command application
- `GameEventJournal` committed typed facts
- `GameVisibilityProjector` public/player visibility projections
- `GameModuleRegistry` explicit module registration
- `GameSetupValidationService` setup validation
- replay/diff helpers such as `GameEventJournalReplayComparer`

The portable engine has no QuillForge host, provider, ASP.NET, storage, or UI dependency.

### Module project: `Den.RulesEngine.Werewolf`

Provides first-module rules and assets:

- descriptor and setup schema
- setup validation
- initial state and seeded role assignment
- stage transitions and legal choice validation
- typed Werewolf events
- prompt assets
- module authoring hooks for stages/action forms/projections

Werewolf proves the framework but does not define every future game's rules.

### QuillForge host bridge

QuillForge owns session/runtime integration:

- `GameRuntimeState` under `SessionState.Game`
- `IGameRuntimeService` as the mutation boundary
- `IGameBridgeService` for starting games and submitting typed/text actions
- `ParticipantCommunicationState` for public channel, direct messages, and display-only game-event feed links
- `AgentVisibleEventsService` for safe agent prompt deltas
- `IGameAgentTurnService` and `IGameAgentMemoryService`
- `IGameInspectorService` for debug projections

All game runtime mutations go through the session mutation gate and the named runtime/bridge services. Endpoints and UI do not mutate game state directly.

### Web/UI contracts

The web layer exposes typed contracts rather than narrator-text inference:

- `/api/game-templates` for template list/load/save/clone/delete/validate
- `/api/game-templates/catalog` for registered modules, setup fields, action forms, prompt assets, providers, and capabilities
- `/api/sessions/{sessionId}/game` for public/participant game views
- `/api/sessions/{sessionId}/game/start`
- `/api/sessions/{sessionId}/game/actions`
- `/api/sessions/{sessionId}/game/messages`
- `/api/sessions/{sessionId}/game/direct-messages`
- `/api/sessions/{sessionId}/game/end`
- `/api/sessions/{sessionId}/game/abort`
- `/api/sessions/{sessionId}/game/inspector` for debug inspection

The React workspace consumes these typed contracts. Werewolf-specific UI remains module-gated and display-only.

## Adding a New Game Module

Use Werewolf and the test fake module as examples, not as a base class to copy wholesale.

### 1. Create the module project

A future production module should live outside QuillForge host layers, e.g.:

```text
src/Den.RulesEngine.MyGame/
tests/Den.RulesEngine.MyGame.Tests/
```

Reference `Den.RulesEngine` only. Do not reference QuillForge Core/Web/Storage/Providers from a portable module.

### 2. Define descriptor and setup schema

Implement `IGameModule.Descriptor` with:

- `ModuleId` and `ModuleVersion`
- supported template version range
- display name
- player-count range
- `SetupFields`
- `CommunicationCapabilities`
- `MemoryExpectations`
- `RequiredPromptAssets`
- `ParticipantRequirements`
- `AuthoringHooks`

Use setup fields for user-editable rule options. Use typed `GameSetupValue` records and validate through `ValidateSetup`.

### 3. Define state shape through engine primitives

The engine core knows about participants, participant sets, stages, rounds, pending inputs, seeded RNG, event journals, and visibility. Module-specific authority should be encoded through:

- participant sets (for role/team-like membership)
- stage ids and `GameStageState`
- pending inputs and `LegalIntentOption`
- typed module events
- deterministic transition logic inside `HandleIntentCommand`

Avoid untyped dictionaries or raw `JsonElement` as gameplay authority.

### 4. Define events and visibility

Create typed module events for facts the game commits. Each event must carry correct `GameEventVisibility`:

- public facts are visible to all and may appear in public narration/feed links;
- private participant/set facts are only visible through participant projections;
- hidden/system-only facts remain host/debug/replay-only.

Do not serialize raw private payloads through public views, session-load contracts, or normal status surfaces.

### 5. Implement transitions

`RulesEngineService` applies core commands and invokes module phases. Your module should validate and respond to:

- `StartGameIntentCommand`
- `SubmitPlayerChoiceIntentCommand`
- `RecordNoActionTakenIntentCommand`
- stage/round/system commands needed by the game

Keep deterministic behavior dependent on the engine seed and ordered commands.

### 6. Add prompt assets and memory hints

Return prompt assets from `GetPromptAssets()` and list required assets in the descriptor. Keep assets provider-neutral strings. Agent invocation and provider/model selection stay in QuillForge templates/runtime.

Use `MemoryExpectations` to tell the host whether round summaries matter and what default token budget/retention shape is reasonable.

### 7. Add authoring hooks

Use `GameModuleAuthoringHooks` for host/UI metadata:

- `Stages` — stable stage labels, descriptions, sequence, and stage communication permissions.
- `ActionForms` — `(stageId, intentName)` form metadata for pending inputs. V1 forms submit typed `choiceName` values.
- `ProjectionCapabilities` — whether the module participates in public, participant-private, and host inspector projections.

The generic Games workspace may use these hooks to render action cards. It still submits typed actions to the bridge and never decides legal outcomes.

### 8. Register explicitly

Add the module to explicit composition, e.g. the app registration point that currently registers `new WerewolfModule()`. Do not use reflection/scanning discovery.

### 9. Test the module and bridge path

Recommended coverage:

- module setup validation and invalid setup failures
- deterministic seed behavior
- stage transitions
- legal and illegal choices
- event visibility through `GameVisibilityProjector`
- scenario tests for full game outcomes
- bridge/service tests proving start, pending input, typed action, event emission, and finish without Werewolf code
- contract/snapshot tests if new endpoint shapes are exposed
- harness scenario coverage if agent behavior matters

## Testing Workflows

### Fast deterministic validation

Use targeted tests while developing:

```bash
dotnet test tests/Den.RulesEngine.Tests/Den.RulesEngine.Tests.csproj \
  -p:AllowMissingPrunePackageData=true

dotnet test tests/Den.RulesEngine.Werewolf.Tests/Den.RulesEngine.Werewolf.Tests.csproj \
  -p:AllowMissingPrunePackageData=true

dotnet test tests/QuillForge.Core.Tests/QuillForge.Core.Tests.csproj \
  -p:AllowMissingPrunePackageData=true --filter "GameBridgeServiceTests|GameAgentTurnServiceTests|GameAgentMemoryServiceTests"

dotnet test tests/QuillForge.Architecture.Tests/QuillForge.Architecture.Tests.csproj \
  -p:AllowMissingPrunePackageData=true --filter "GameTemplateCatalogResponse|GameViewResponse|WerewolfUiScenarioTests|GameEndpointTests"
```

Before review, run the normal solution build and deterministic non-Ollama suite:

```bash
dotnet build QuillForge.slnx -p:AllowMissingPrunePackageData=true
dotnet test QuillForge.slnx -p:AllowMissingPrunePackageData=true --no-build --filter "FullyQualifiedName!~Ollama"
git diff --check
```

`AllowMissingPrunePackageData=true` is currently needed for the .NET 10 preview toolchain in Web/Architecture tests.

### Multi-agent game harness

Use the deterministic game harness for scripted agent/memory traces:

```bash
dotnet test tests/QuillForge.ProviderHarness.Tests/QuillForge.ProviderHarness.Tests.csproj \
  -p:AllowMissingPrunePackageData=true \
  --filter HarnessGameScenarioTests
```

The harness writes JSON and Markdown trace artifacts for prompt envelopes, selected actions, memory decisions, public/private projections, token usage, and failure-surface taxonomy. See [`docs/architecture/game-harness-trace-artifacts.md`](architecture/game-harness-trace-artifacts.md).

### Manual/live app validation

For UI and runtime validation, follow [`docs/synthetic-testing.md`](synthetic-testing.md): build and run the app in Development, use real endpoints/UI, and capture exact failures.

Social-game smoke path:

1. Start `src/QuillForge.Web` in Development.
2. Configure a provider alias (local Ollama or API-backed provider).
3. Switch to Games mode.
4. Create/save a Werewolf template with at least one human seat and enough agent seats for the module player count.
5. Select the template and start a game.
6. Verify public narration/feed, selected participant private view, pending actions, and Werewolf display panel.
7. Submit a human pending action.
8. Post a public message when the stage permits it.
9. Reload the session and verify game state persists.
10. Use `/api/sessions/{sessionId}/game/inspector?promptEnvelopeLimit=10` for debug inspection.

Provider-key requirements:

- deterministic engine/module tests do not require live provider keys;
- harness scripted scenarios use fake completion services and do not require keys;
- live/manual agent behavior requires configured provider aliases and any required API keys or a running local Ollama server;
- the full unfiltered solution test may exercise local/live Ollama tests, so use the non-Ollama filter for deterministic validation unless you are explicitly validating Ollama.

## Troubleshooting

### Provider failures

Symptoms:

- agent turn result records `provider-level-failure`;
- prompt envelope has empty/error response;
- trace artifacts show provider/model metadata but no usable response.

Checks:

- provider alias exists in Provider Manager / provider config;
- API key or local server is available;
- model override is valid for that provider;
- provider timeout is not too low for the selected model;
- harness trace has the expected provider/model fields.

### Invalid agent responses

Symptoms:

- `AgentResponseRejectedEvent`;
- `NoActionTakenEvent` after retry/timeout/fallback;
- harness failure-surface flags: `parse-fail`, `schema-fail`, `illegal-action`, `model-refusal`, `retry-exhaustion`.

Checks:

- inspect the prompt envelope in the game harness trace or inspector;
- verify prompt delivery cursors show the agent was given the needed public/private facts;
- compare the model response against required compact JSON / legal choice names;
- verify the pending input's `LegalOptions` and module action-form metadata match the intended choice path.

### Session busy errors

Game mutations run through `ISessionMutationGate`. If a session is busy, another mutation is already in progress for that session.

Checks:

- retry after the current request finishes;
- avoid launching overlapping start/action/message/end requests for the same session;
- inspect endpoint/UI logs for concurrent requests;
- keep long provider calls bounded by the configured agent-turn timeout.

### Hidden-information debugging

Rules:

- public views should only contain public engine events and public communication feed entries;
- participant views may include events/feed entries visible to that participant;
- hidden/system events should not appear in player/public views;
- inspector event summaries should not serialize raw private module payloads.

Debug path:

1. Use the participant-specific game view to see what a player could see.
2. Use the inspector for safe summaries, prompt cursors, memory snapshots, and prompt envelope previews.
3. Use harness traces for deeper public/private event/feed comparisons.
4. If leakage appears, inspect `GameVisibilityProjector`, `ParticipantChannelService`, and game-event-link creation in `GameRuntimeService` before changing UI code.

Narration and Werewolf UI text are display-only; they should never be used to decide rules, legal actions, or hidden facts.
