# Game Module Authoring Hooks

Status: Implemented for task #850.

## Purpose

Game modules should expose enough typed metadata for QuillForge to render and host future social games without copying Werewolf-specific UI, prompt, communication, or memory assumptions. The metadata is declarative. Gameplay authority remains in `Den.RulesEngine` through `RulesEngineService`, `RulesGameState`, and committed `IGameEvent` facts.

## Stable Hooks

The stable module-authoring surface is split between existing module descriptor fields and the task #850 `GameModuleAuthoringHooks` metadata.

Stable descriptor hooks:

- `GameModuleDescriptor.SetupFields` is the template setup schema rendered by the template editor and validated by `GameSetupValidationService`.
- `GameModuleDescriptor.CommunicationCapabilities` declares whether the module permits public channel messages and direct messages. Host/template/stage permissions still gate actual sends.
- `GameModuleDescriptor.MemoryExpectations` declares whether the module expects round summaries and gives host defaults for memory token budget/retention.
- `IGameModule.GetPromptAssets()` and `GameModuleDescriptor.RequiredPromptAssets` declare reusable rules/instruction/narration prompt assets without provider dependencies.
- `GameModuleDescriptor.ParticipantRequirements` declares generic roster requirements.

Stable `GameModuleAuthoringHooks` hooks:

- `Stages` describes stage ids, display labels, descriptions, order, and stage-level communication permissions.
- `ActionForms` describes generic pending-input form presentation by `(stageId, intentName)`. The v1 layout is intentionally simple (`ButtonList` / `SelectOne`) and binds to the existing `choiceName` typed action path rather than raw JSON.
- `ProjectionCapabilities` documents that modules participate in public event projection, participant-private projection, and host inspector projection through the common event journal/visibility layer.

QuillForge exposes these hooks in the typed game template catalog and in active `GameBridgeView.ModuleAuthoring`. Participant views also include the action form descriptors matching the currently pending inputs so the generic Games workspace can render action cards without parsing narrator text.

## Werewolf-Specific Details

The following remain Werewolf implementation details and should not become generic rules:

- role/team names such as werewolf, villager, seer, village, or werewolves;
- night/day/voting win-condition semantics;
- role reveal, teammate reveal, vote resolution, elimination, and win condition event payloads;
- `WerewolfGameEventNarrationComposer` display text;
- `WerewolfGamePanel` role/team/outcome projection UI.

Future modules can register different stages, action forms, prompt assets, communication capabilities, and memory hints without changing the generic bridge contracts.

## Fake Module Coverage

`GameBridgeServiceTests.GenericAuthoringHooks_ProjectThroughBridgeAndDriveFakeModuleToCompletion` registers a minimal non-Werewolf module explicitly. The test starts a game from a template, verifies setup/action/stage/prompt/communication/memory/projection hooks flow through `GameBridgeView`, submits a typed action through the generic bridge, and verifies the fake module emits a completion event and ends the game.

This is intentionally the proving layer for v1: generic hooks exist because they are consumed by bridge/UI contracts and enable a real non-Werewolf module test, not because of speculative abstraction.

## Boundaries

- No reflection or scanning: modules remain explicitly registered by module id and version.
- No raw `JsonElement` action payloads: action form metadata maps onto typed `PendingInputState`, `LegalIntentOption`, and `SubmitPlayerChoiceIntentCommand` values.
- No UI authority: UI reads hook metadata and submits typed actions; it does not determine legal choices, outcomes, or hidden facts.
- No provider dependency in `Den.RulesEngine`: prompt assets are strings and metadata only. Provider/model selection remains in QuillForge templates and runtime bindings.
