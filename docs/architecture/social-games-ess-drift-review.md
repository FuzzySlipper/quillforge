# Social Games ESS Drift Review

Status: Task #849 hardening review, April 2026.

This note records the final architecture drift pass for the social-games epic (#828). It reviews the implemented game framework against:

- project guidance in AGENTS/Den guidance;
- Den `_global/ess-architecture-guide`;
- Den `_global/ess-naming-guide`;
- Den `_global/ess-review-checklist`;
- [`social-games-framework-architecture.md`](./social-games-framework-architecture.md);
- [`game-module-authoring-hooks.md`](./game-module-authoring-hooks.md);
- [`game-session-persistence-replay.md`](./game-session-persistence-replay.md);
- [`social-games-setup-testing-extension.md`](../social-games-setup-testing-extension.md).

## Verdict

No blocking ESS drift was found in the implemented social-games integration.

The framework matches the intended ownership split:

- `Den.RulesEngine` owns deterministic live rules authority through `RulesGameState`, typed `IGameIntentCommand` inputs, typed `IGameEvent` facts, `GameEventJournal`, `GameVisibilityProjector`, and `RulesEngineService`.
- QuillForge owns session persistence, templates, participant communication, agent turns, agent memory, narration, endpoint contracts, and UI projections.
- `RulesGameStateSnapshot` is the persisted copy stored in `GameRuntimeState`; it is not mutated by endpoints or UI.
- `GameRuntimeService` remains the named same-session mutation boundary for runtime changes and uses `ISessionMutationGate`.
- `GameBridgeService` routes template/text/typed action requests into runtime/engine services and does not become game authority.
- `GamesMode` remains a prompt/workspace boundary; gameplay actions use typed game endpoints and bridge commands.
- Werewolf-specific projections and narrator text remain module-gated/display-only.

## Checklist Results

### Mutable state ownership

| State | Owner | Result |
| --- | --- | --- |
| Live engine state | `RulesGameState` operated on by `RulesEngineService` | OK |
| Persisted engine state | `RulesGameStateSnapshot` under `SessionState.Game` / `GameRuntimeState` | OK |
| Session game runtime | `GameRuntimeService` through `ISessionMutationGate` and `ISessionStateStore` | OK |
| Saved templates | `GameTemplateService` + `IGameTemplateStore` | OK |
| Communication feed/DMs | `ParticipantCommunicationState` mutated through `ParticipantChannelService` and game runtime boundary | OK |
| Agent prompt cursors/memory | QuillForge game agent services and runtime substate | OK |
| UI state | React component state only; no durable gameplay authority | OK |

No shared mutable gameplay state was found between endpoint/UI adapters and services. Endpoints delegate to `IGameBridgeService` / `IGameInspectorService`; they do not load `SessionState`, call `RulesEngineService`, or write `GameRuntimeState` directly.

### Services, adapters, and stores

- `RulesEngineService` is a behavior boundary for deterministic rules application.
- `GameRuntimeService` is a behavior/mutation boundary for session-owned game runtime state.
- `GameBridgeService` is a host bridge that translates QuillForge requests into runtime/engine commands.
- `GameTemplateService` owns template validation/normalization before persistence.
- File-backed stores remain in `QuillForge.Storage`; core services depend on store interfaces.
- Web endpoints are thin typed adapters; game endpoints bind request DTOs and delegate.
- Provider interaction stays behind QuillForge completion abstractions in game agent services.

### Events and commands

- Engine events are typed facts such as `GameStartedEvent`, `PlayerChoiceSubmittedEvent`, `StageAdvancedEvent`, `RoundEndedEvent`, `GameEndedEvent`, and `GameAbortedEvent`.
- Host runtime events are typed facts such as `GameRuntimeStartedEvent`, `GameRuntimeEngineCommandAppliedEvent`, and `GameRuntimeAbortedEvent`.
- Engine mutation inputs use explicit `*IntentCommand` names, e.g. `StartGameIntentCommand`, `SubmitPlayerChoiceIntentCommand`, `AbortGameIntentCommand`.
- HTTP inputs use `*Request` DTOs in web contracts.
- Transport/runtime event names that must cross UI or logs are derived from typed records and are not used as gameplay authority.

No unqualified domain `Command` drift was introduced in the game engine/bridge path.

### Registry explicitness

Game modules remain explicitly registered through `GameModuleRegistry` / `GameModuleRegistryFactory` and application composition. No reflection or assembly scanning is used for module discovery. Future modules should continue adding explicit registrations by module id and version.

A non-blocking #850 follow-up already exists as task #946 to polish authoring hook edge cases, including `GameModuleAuthoringHooks.Empty` defaults and richer snapshot coverage.

### Dependency boundaries

Verified and locked by architecture tests:

- `Den.RulesEngine` has no project, package, framework, QuillForge, ASP.NET, provider SDK, UI, or storage reference.
- `Den.RulesEngine.Werewolf` references only `Den.RulesEngine`.
- `QuillForge.Core` references the portable rules engine contracts but not the Werewolf module.
- Provider SDK packages/namespaces are not referenced outside `QuillForge.Providers`.

### Typed JSON boundaries

The game engine, bridge, runtime, model, endpoint, and contract path stays typed:

- no `JsonElement`, `JsonDocument`, `JsonNode`, `GetProperty`, or `TryGetProperty` navigation appears in `src/QuillForge.Core/Services/Game*.cs`, `src/QuillForge.Core/Models/Game*.cs`, or `src/QuillForge.Web/Endpoints/GameEndpoints.cs`;
- game endpoints bind typed request DTOs;
- game agent services deserialize provider text into typed DTOs and validate into legal typed choices before applying engine commands;
- the portable engine's JSON handling is limited to the polymorphic `IGameEvent` persistence converter, not gameplay behavior.

Task #849 added architecture tests that lock the game core/endpoint path against raw JSON navigation drift.

## Non-blocking Follow-ups Already Tracked

The following non-blocking follow-ups remain planned and do not block this hardening pass:

- #876 — Tighten rules-engine visibility projector input surface.
- #877 — Module end/abort lifecycle hook design.
- #883 — Harden game template validation UX edge cases.
- #886 — Polish game runtime mutation edge-case tests.
- #890 — Harden game intent translation and bridge edge cases.
- #892 — Harden agent-turn failure visibility and error-path tests.
- #897 — Polish agent memory summary budget and projection performance.
- #898 — Remove confusing provider default-model options.
- #899 — Add composite game narration composer dispatch.
- #922 — Harness trace cleanup/follow-ups from #846 review.
- #928 — Consolidate game event introspection helpers.
- #946 — Polish game module authoring hook edge cases from #850 review.

No additional non-blocking drift findings were found that needed new follow-up tasks.

## Validation Commands

Task #849 validation included:

```bash
dotnet test tests/QuillForge.Architecture.Tests/QuillForge.Architecture.Tests.csproj \
  -p:AllowMissingPrunePackageData=true \
  --filter "DependencyBoundaryTests"
```

Before review, run the full release-style validation:

```bash
dotnet build QuillForge.slnx -p:AllowMissingPrunePackageData=true
dotnet test QuillForge.slnx -p:AllowMissingPrunePackageData=true --no-build --filter "FullyQualifiedName!~Ollama"
git diff --check
```

Use the non-Ollama filter for deterministic validation unless explicitly validating local/live Ollama behavior.
