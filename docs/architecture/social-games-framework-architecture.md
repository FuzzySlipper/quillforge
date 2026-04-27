# Social Games Framework Architecture

## Status

Proposed for task #829. This document is the pre-implementation architecture
contract for the social games epic (#828). No production code should be added
until this contract has been reviewed.

## Purpose

QuillForge needs a modular framework for turn-based social games with human and
LLM participants. Werewolf is the first module because it exercises the hardest
QuillForge-specific concerns early: hidden information, social participation,
agent turns, direct messages, memory summaries, prompt delivery cursors, and
post-hoc debugging of odd model behavior.

Werewolf is not the upper bound of the engine. It is intentionally a small proof
of the host, visibility, and agent plumbing around a portable rules engine. The
long-term target includes richer tabletop and PNP-style modules where stacked
rules, priority ordering, discrete resolution payloads, traceability, and replay
matter much more. The `RulesEngineService` shape is therefore a central design
choice, not a combat-specific inheritance from RuleWeaver.

## Source Material

This spec builds on:

- Den document `social-games-framework-plan`.
- [service-command-event-conventions.md](./service-command-event-conventions.md).
- [session-runtime-command-event-reference.md](./session-runtime-command-event-reference.md).
- [profile-session-conversation-ownership.md](./profile-session-conversation-ownership.md).
- [content-layout-and-persistence.md](./content-layout-and-persistence.md).
- [agent-implementation-patterns.md](./agent-implementation-patterns.md).
- Den `_global/ess-architecture-guide`, `_global/ess-naming-guide`, and
  `_global/ess-review-checklist`.
- RuleWeaver survey points from `../ruleweaver`, especially
  `RulesEngineService`, `TurnService`, typed events, event journal recording,
  trace sinks, explicit composition, and architecture boundary tests.

## Settled Naming Decisions

| Concern | Decision | Rationale |
| --- | --- | --- |
| Generic mode wire name | `games` | Existing mode wire names are short lowercase words. The mode is not Werewolf-specific. |
| Central engine service | `RulesEngineService` | The key reusable mechanism is rule resolution, not a single game loop. This name also matches the future PNP goal. |
| QuillForge session substate | `GameRuntimeState` | Matches existing session-owned runtime state names such as `WriterRuntimeState` and `NarrativeRuntimeState`. |
| Channel and DM placement | Embedded in `GameRuntimeState` for v1 | Keeps v1 persistence simple while using generic communication types that can later move to a dedicated store. |
| First Werewolf scope | Baseline Werewolf with One Night-compatible hooks | Baseline Werewolf proves social and hidden-info plumbing. One Night can follow without reshaping the module contract. |
| Narration | Deterministic templates for v1 | Snapshot-testable and display-only. Optional LLM-styled narration is a later traced adapter. |

## Architecture Overview

The system has four major boundaries.

`Den.RulesEngine` is the portable rules layer. It owns deterministic gameplay
state, module contracts, rules resolution, committed game events, event
visibility metadata, setup validation, replay, and engine trace records. It has
no dependency on QuillForge, ASP.NET, provider SDKs, storage implementations, or
UI frameworks.

QuillForge owns the host integration. It stores the session-owned
`GameRuntimeState`, runs the session mutation gate, starts games from durable
templates, provides RNG seeds, invokes agent players, builds prompts from trusted
visibility projections, stores per-agent memory, exposes endpoints, renders UI,
and writes durable state through existing stores.

Reusable channel and direct-message state live in QuillForge v1 but are not
Werewolf-shaped. They model participant communication that a game may use. They
are embedded under `GameRuntimeState` initially and can be promoted later if a
non-game consumer appears.

`Den.RulesEngine.Werewolf` is the first game module. It supplies module
metadata, setup validation, role assignment rules, legal actions, phase
transitions, visibility rules, deterministic narrator templates, and prompt/rules
assets. It must prove the module contract without becoming the framework.

## RuleWeaver Architecture Survey

### Patterns To Copy

RuleWeaver's most important portable pattern is a central rules service that
resolves typed payloads through ordered rule handlers. Its
`RulesEngineService` registers handlers explicitly, resolves a payload through
discrete phases, records trace entries, and exposes one clear service boundary
for new rules. See
`../../../ruleweaver/src/RuleWeaver.Core/Rules/RulesEngineService.cs`.

`Den.RulesEngine` should copy that shape at the architectural level:

- rules are named handler types registered explicitly by modules;
- resolution accepts typed payloads instead of raw JSON;
- handlers run in deterministic priority and phase order;
- a trace record is emitted for every meaningful resolution step;
- in-flight resolution payloads become committed typed events only after the
  engine accepts the transition.

RuleWeaver's `TurnService` is also useful as a model for a service-owned
coordination state with a shaped read model. It owns initiative order, current
actor, round number, start/end transitions, and round-end facts. See
`../../../ruleweaver/src/RuleWeaver.Core/Scheduling/TurnService.cs`.
`Den.RulesEngine` should copy the idea of authoritative turn/round/stage
services, not the combat-specific initiative algorithm.

RuleWeaver separates live typed events from historical projections. Its
`EventJournalRecorder` subscribes to typed events and records a stable journal
projection. See
`../../../ruleweaver/src/RuleWeaver.Core/Logging/EventJournalRecorder.cs`.
`Den.RulesEngine` should go one step further: the event journal is not only a
log projection, it is the replay authority for committed gameplay facts.

RuleWeaver's `RuleTraceEntry` and null/in-memory trace sinks are a good
observability pattern. See
`../../../ruleweaver/src/RuleWeaver.Core/Rules/Trace/RuleTraceEntry.cs`.
`Den.RulesEngine` should expose a portable engine observability surface such as
`IRulesEngineObserver` receiving typed `EngineTraceRecord` values, with a
default no-op implementation.

RuleWeaver's `GameBootstrap` keeps runtime composition explicit. See
`../../../ruleweaver/src/RuleWeaver.App/Runtime/GameBootstrap.cs`.
QuillForge should copy this explicit registration style in `Program.cs` or a
nearby service registration module. No reflection or scanning should discover
game modules.

RuleWeaver's architecture test locks its core away from rendering dependencies.
See `../../../ruleweaver/tests/Architecture.Tests/CoreBoundaryTests.cs`.
Task #830 should add equivalent tests proving `Den.RulesEngine` has no
QuillForge, provider, ASP.NET, UI, or storage dependency.

### Assumptions Not To Copy

RuleWeaver is a tactical RPG engine. The following assumptions must not leak
into `Den.RulesEngine`:

- attack rolls, hit/miss effects, damage, HP, defenses, conditions, classes,
  feats, powers, equipment, implements, keywords, levels, and action economy;
- initiative order, factions as combat teams, turn activity points, reactions,
  opportunity attacks, ongoing damage, and saving throws;
- grid maps, line of sight, range, reach, movement, positions, terrain, zones,
  and pathfinding;
- content registries for actions, entities, classes, items, talents, and combat
  scenarios;
- a combat runner as the default game loop.

`Den.RulesEngine` can support PNP modules later, but those concepts must arrive
through module-owned contracts and typed payloads. The portable engine core
should know about participants, modules, setup, stages, pending inputs,
deterministic RNG, rule resolution, event visibility, and replay. It should not
know D&D, 4e, tactical grids, or Werewolf.

## ESS Ownership Map

| Concern | Owner | Type family |
| --- | --- | --- |
| Authoritative gameplay state | `Den.RulesEngine` | `RulesGameState` |
| Rules resolution | `Den.RulesEngine` | `RulesEngineService` |
| Rule handlers | Game module projects | named `IRuleHandler<TPayload>` implementations |
| Module metadata and validation | `Den.RulesEngine` plus module projects | `GameModuleDescriptor`, `GameModuleRegistry` |
| Engine replay history | `Den.RulesEngine` | `GameEventJournal` |
| Engine tracing | `Den.RulesEngine` | `EngineTraceRecord`, `IRulesEngineObserver` |
| Running QuillForge binding | QuillForge session runtime | `GameRuntimeState` under `SessionState` |
| Durable game templates | QuillForge store/service | `GameTemplate`, `GameTemplateStore`, `GameTemplateService` |
| Channel and DM messages | QuillForge session runtime v1 | `ParticipantCommunicationState`, `ParticipantChannelService` |
| Agent player invocation | QuillForge service | `AgentPlayerTurnService` |
| Text-to-intent translation | QuillForge service/agent | `GameIntentTranslationAgent` |
| Prompt visibility projection | QuillForge trusted service | `AgentVisibleEventsService`, `AgentVisibleEvents` |
| Agent memory summaries | QuillForge service | `GameAgentMemoryService`, `MemorySummaryDecision` |
| Narration | QuillForge adapter/projection | `NarratorProjectionComposer` |
| UI and HTTP | QuillForge adapters | request/response DTOs, panels, screens |

Services are behavior owners, not hidden state bags. Durable writes go through
the owning store or session service. Adapters parse input, call services, and
format output.

## Den.RulesEngine Boundary

`Den.RulesEngine` should expose a small portable API:

- `RulesEngineService` applies one typed intent command or engine system
  transition to a `RulesGameState`.
- `GameModuleRegistry` holds explicitly registered modules.
- `GameSetupValidator` validates templates and module setup inputs against the
  registered module set.
- `GameEventJournal` records committed typed gameplay facts with stable event
  IDs and monotonic sequence numbers.
- `GameReplayService` rebuilds engine state from seed plus committed input and
  event history.
- `GameVisibilityProjector` produces player-visible engine event projections.
- `IRulesEngineObserver` receives `EngineTraceRecord` values.

The service must be deterministic. Given the same module set, initial setup,
seed, and ordered intent commands, replay produces the same committed event
sequence and final engine snapshot.

The engine rejects unseeded RNG use. The QuillForge bridge supplies the seed at
game start from the user/template/default policy. The engine records the seed in
the event journal so replay is self-contained.

### Resolution Stack

`RulesEngineService` should keep the RuleWeaver-inspired rule stack even though
Werewolf uses very little of it.

A module may define typed resolution payloads such as:

- `AssignRolesPayload`
- `ResolveNightActionPayload`
- `ResolveVotePayload`
- future PNP payloads such as `ResolveAttackPayload`, `ApplyConditionPayload`,
  or `ResolveSkillCheckPayload`

Each payload resolves through deterministic phases. The initial phase names can
follow RuleWeaver's proven vocabulary, `CanStart`, `OnRun`, and `OnEnd`, unless
task #831 finds a clearer module-neutral naming set. The critical requirement is that
the phases are discrete, ordered, traced, and typed.

Every handler invocation produces an `EngineTraceRecord` containing at least:

- game instance ID;
- module ID and module version;
- current event sequence before resolution;
- payload type;
- phase;
- handler name;
- priority;
- short structured outcome;
- validation or cancellation reason when applicable.

Trace records are observability. Committed gameplay facts are events. Trace
records must help explain why an event happened without becoming gameplay
authority.

## Module Boundary

Each module owns its game-specific content and rules. The portable module
contract should include:

- `ModuleId`;
- semantic module version;
- compatible template version range;
- setup schema and typed validation;
- player count and participant requirements;
- initial state creation;
- legal intent command descriptors by stage;
- rule handler registration;
- visibility projection rules;
- deterministic narration template assets;
- prompt/rules text assets for agent players.

Module registration is explicit. The QuillForge host registers
`Den.RulesEngine.Werewolf` during startup. Unknown module IDs, unsupported
versions, and incompatible template ranges surface as typed validation errors at
load time. There is no silent fallback to a nearby module or version.

Werewolf v1 should implement baseline Werewolf. One Night-style compatibility is
expressed by setup and stage extension points, not by building One Night rules in
the first slice.

## QuillForge Games Mode Boundary

The `games` mode is a mode shell and prompt/UI boundary. It is not the game
master.

The mode owns:

- mode name and system-prompt section for human-facing game interaction;
- tool policy for game sessions;
- presentation hooks that route the user to game endpoints;
- rejection messaging when the user tries to use non-game behavior.

The mode does not own:

- rules decisions;
- stage transitions;
- hidden information;
- agent player prompts;
- memory summaries;
- channel/DM persistence.

Active game messages should route through game endpoints and bridge services,
not through the general-purpose Orchestrator as an improvised rules controller.
The existing `OrchestratorAgent` may remain an implementation shell where
needed, but it must not see game events as a broad helpful assistant and it must
not decide gameplay outcomes.

### Mode Switching During A Game

If `GameRuntimeState.Status` is running, waiting for input, resolving, or
waiting on agent turns, switching away from `games` is rejected. The user must
explicitly end or abort the game first.

The rejection should be typed, for example:

- `GameModeSwitchRejectedEvent`
- reason code `game_in_progress`

This follows the session-as-execution-lane model. It avoids orphaned agent turns
and avoids mode-owned state being mutated outside the game boundary.

## GameRuntimeState

`SessionState` gains one substate:

```csharp
public sealed class GameRuntimeState
{
    public GameRuntimeStatus Status { get; set; }
    public string? ModuleId { get; set; }
    public string? ModuleVersion { get; set; }
    public long? Seed { get; set; }
    public RulesGameStateSnapshot? EngineSnapshot { get; set; }
    public GameEventJournalSnapshot EventJournal { get; set; } = new();
    public ParticipantCommunicationState Communication { get; set; } = new();
    public List<AgentMemorySnapshot> AgentMemories { get; set; } = [];
    public List<AgentPromptDeliveryCursor> PromptCursors { get; set; } = [];
    public List<AgentPromptEnvelope> PromptEnvelopes { get; set; } = [];
}
```

The exact C# shape belongs to #838 and #841. The ownership rules are settled
here:

- engine snapshot and journal are copied from `Den.RulesEngine` results;
- QuillForge session services are the only writers;
- endpoints never mutate this state directly;
- v1 stores the event journal embedded in session-state JSON;
- split to a sibling artifact only if journal or prompt envelope size makes
  normal session-state load/save measurably expensive.

The v1 split trigger should be operational, not aesthetic: large real sessions,
slow loads, or a need to stream inspector data independently. Until then,
embedding keeps fork/delete/replay behavior straightforward.

## Durable Game Templates

Templates are reusable user-owned setup documents, not running game state.

A `GameTemplate` owns:

- template ID and display name;
- module ID;
- supported module version range;
- player slots and participant names;
- human versus agent participant bindings;
- provider alias/model/personality/name settings per agent player;
- memory token budgets;
- game-specific setup values;
- optional RNG seed policy.

Template validation has two layers:

- `GameTemplateService` validates QuillForge concerns such as transport shape,
  provider alias existence, model selection, duplicate participant names, and
  memory budget bounds.
- `GameModuleRegistry.CanLoad(template)` validates module compatibility and
  module-specific game rules.

Templates should be stored through a `GameTemplateStore` with atomic writes.
The concrete path is finalized in #837. The default should be a user-editable
content-root path rather than app-global config because templates are reusable
creative/game artifacts.

## Channel And Direct Message Boundary

V1 embeds communication state in `GameRuntimeState`, but type names stay
generic:

- `ParticipantCommunicationState`
- `ParticipantChannelMessage`
- `ParticipantDirectMessage`
- `ParticipantChannelService`
- `DirectMessageService`

Messages have stable IDs, monotonic per-source sequences, sender participant
IDs, recipient participant IDs or recipient sets, timestamps, and visibility.

Game modules may declare whether a stage allows public channel messages, direct
messages, both, or neither. The communication service enforces those permissions
and records typed failure events such as `DirectMessageRejectedEvent` with reason
code `dm_forbidden`.

This state must not encode Werewolf terms such as village, wolf chat, day chat,
or night chat. Werewolf maps its concepts onto generic channels and visibility
sets.

## Event And Visibility Model

The engine keeps one authoritative event journal. Events carry visibility
metadata instead of being copied into separate truth stores.

Visibility categories:

- `public`: visible to every participant;
- `private-to-player`: visible only to one participant;
- `private-to-set`: visible to a named participant set;
- `hidden/system-only`: visible to replay, debugging, and host services only;
- `narrated/public projection`: display text derived from typed facts.

Engine events are fact-shaped and past tense. Examples:

- `GameStartedEvent`
- `RolesAssignedEvent`
- `PrivateRoleRevealedEvent`
- `NightPhaseStartedEvent`
- `PlayerVotedEvent`
- `VoteResolvedEvent`
- `GameEndedEvent`
- `NoActionTakenEvent`

Narration is not an engine event unless the fact is "a narration projection was
rendered." Narrator text never becomes gameplay authority.

For v1, narrator output is UI/human-only. Agent prompts receive typed engine
facts plus visible channel/DM messages, not narrator prose. Optional future LLM
narration must be opt-in, traced like any other model call, and excluded from
gameplay replay.

## AgentVisibleEvents

`AgentVisibleEvents` is the sole allowed event input to prompt assembly.

It is constructed by `AgentVisibleEventsService`, a trusted QuillForge boundary.
Prompt builders must not accept raw engine journal entries or raw channel/DM
state.

The projection is the union of:

- engine events visible to the agent participant;
- public channel messages visible to that participant;
- direct messages where the participant is sender or recipient;
- memory summary metadata needed to orient the agent;
- cursor metadata showing exactly which events/messages are new.

This makes hidden-information leakage a type-level boundary. A prompt builder
cannot accidentally include hidden/system-only events because it never receives
that data shape.

## Agent Turn Flow

Normal agent turn:

1. `RulesEngineService` emits pending input facts for one or more participants.
2. `AgentPlayerTurnService` finds agent participants with pending input.
3. `AgentVisibleEventsService` builds each participant's visible projection.
4. Prompt assembly uses module prompt assets, prior memory, newly visible facts,
   communication messages, and the participant's private view.
5. Agent output is expected to be structured when possible.
6. Structured output bypasses LLM translation and goes directly to deterministic
   validation.
7. Free-form or ambiguous text goes through `GameIntentTranslationAgent`.
8. The bridge submits a typed intent command to `RulesEngineService`.
9. Cursors advance only for facts actually included in the prompt envelope.
10. Failures become typed records/events.

Agent players receive no tools. Human players receive no tools except
game-specific tools and an explicit games-mode allowlist. Lore, writer, forge,
research, and broad file tools are blocked during game sessions.

## Text-To-Intent Translation

LLMs may be participants, command translators, or storytellers. They are never
the game master and never gameplay authority.

`GameIntentTranslationAgent` is a narrowly scoped translator. It converts fuzzy
human text or bounded repair text into typed game intent commands. It is
prompted for translation fidelity, not helpfulness.

Use the translator only for:

- free-form human player input;
- ambiguous text that cannot be mapped to one typed UI action;
- bounded repair or normalization after an agent produced almost-valid text.

Bypass the translator for:

- typed UI actions;
- game-specific buttons/forms;
- structured agent outputs;
- deterministic system transitions.

Those inputs go directly to validation and engine command handling. This avoids
extra model calls and prevents a helpful model from "fixing" the rules.

## Deterministic Agent Application Order

When multiple agent players have parallel pending inputs, QuillForge may invoke
providers concurrently, but it applies accepted intents in deterministic order.

V1 order is participant ID order by stable participant identifier. If a module
needs a different order, it must declare that order as part of its typed pending
input facts.

Missing, timed-out, late, stale, or invalid responses are not collapsed into one
generic no-op. The journal distinguishes:

- `NoActionTakenEvent` for an intentional or rules-default no action;
- `AgentTurnTimedOutEvent` for timeout;
- `AgentResponseRejectedEvent` for parse/schema/illegal failures;
- `LateAgentResponseIgnoredEvent` for response after stage advancement;
- `DuplicateAgentResponseIgnoredEvent` for duplicate responses.

The goal is that "agent did nothing" is never ambiguous in logs, traces, or the
inspector.

## Agent Memory And Prompt Cursors

Round-end memory summarization is QuillForge agent coordination. It is not
rules-engine gameplay authority.

When the engine emits a round/stage-end fact, QuillForge asks each agent to
summarize what it remembers from visible facts and communication since its prior
memory cursor. The resulting `MemorySummaryDecision` records:

- agent participant ID;
- prior memory revision;
- new memory revision;
- source cursor snapshot;
- token budget;
- token counts;
- provider/model;
- trim/retry/refusal flags;
- rejection reason, if any;
- content hash and stored summary text.

Prompt cursor state is per agent. `AgentPromptDeliveryCursor` tracks:

- last delivered public engine event sequence;
- delivered private event IDs or private sequence positions;
- channel cursor;
- direct-message cursor;
- memory revision;
- last prompt envelope ID.

Task #841 must also store the last N prompt envelopes per agent. Each
`AgentPromptEnvelope` records:

- the `AgentVisibleEvents` cursor snapshot that produced it;
- full assembled prompt text, or a content hash when storage constraints require
  externalization;
- provider and model;
- token counts;
- raw agent response;
- parsed structured output, if any;
- resulting intent command or rejection record.

## Agent Failure Surface

Every failure category below must map to a typed event or record so debugging is
greppable across logs, harness traces, persisted state, and inspector output.

| Category | Example typed fact |
| --- | --- |
| Invalid intent | `GameIntentRejectedEvent` reason `invalid_intent` |
| Unparseable response | `AgentResponseRejectedEvent` reason `unparseable_response` |
| Schema failure | `AgentResponseRejectedEvent` reason `schema_failed` |
| Illegal action | `GameIntentRejectedEvent` reason `illegal_action` |
| Out of stage | `GameIntentRejectedEvent` reason `out_of_stage` |
| Hidden-info attempt | `GameIntentRejectedEvent` reason `hidden_info_attempt` |
| DM forbidden | `DirectMessageRejectedEvent` reason `dm_forbidden` |
| Memory budget exceeded | `MemorySummaryRejectedEvent` reason `budget_exceeded` |
| Summary trim/retry/refusal | `MemorySummaryDecision` flags and reason |
| Provider failure | `AgentProviderFailedEvent` |
| Retry exhaustion | `AgentRetryExhaustedEvent` |
| Model refusal or safety block | `AgentModelRefusedEvent` |
| Prompt assembly failure | `PromptAssemblyFailedEvent` |
| Cursor inconsistency | `PromptCursorInconsistencyDetectedEvent` |
| Timeout or cancellation | `AgentTurnTimedOutEvent` or `AgentTurnCancelledEvent` |
| Late or stale response | `LateAgentResponseIgnoredEvent` |
| Duplicate response | `DuplicateAgentResponseIgnoredEvent` |
| Participant binding mismatch | `AgentParticipantBindingRejectedEvent` |
| Replay or invariant mismatch | `ReplayInvariantFailedEvent` |
| Prompt-injection attempt | `PromptInjectionAttemptRecordedEvent` |

This taxonomy is intentionally broader than Werewolf needs because future PNP
modules will be harder to inspect after the fact.

## Session Lifecycle

Concurrent games are allowed across different sessions. Each session owns its
own `GameRuntimeState` and its own mutation gate. Same-session game mutations
must not interleave.

Forking a session with an active game is allowed only at a stable service
boundary: no provider call is currently in flight and the session mutation gate
can be acquired. If a turn is in flight, fork returns busy.

At a stable active-game fork, the new session receives deep copies of:

- engine snapshot;
- event journal;
- channel and DM history;
- agent memory snapshots;
- prompt delivery cursors;
- retained prompt envelopes.

The forked state appends a hidden/system-only host record such as
`GameRuntimeForkedEvent` with source session and target session IDs. Future
events continue from the copied max sequence. The sessions then diverge
independently.

Deleting a session deletes:

- conversation tree;
- `SessionState`;
- `GameRuntimeState`;
- embedded engine snapshot and journal;
- channel/DM history;
- memory snapshots;
- prompt cursors;
- retained prompt envelopes.

If deletion races with a provider response, the session lifecycle service
requests cancellation where possible. Late responses for deleted sessions are
ignored and logged as host diagnostics, not applied to recreated state.

## Replay And Determinism

There are two determinism layers.

Engine replay is deterministic. Given the same module versions, setup inputs,
seed, and accepted intent command journal, `GameReplayService` produces the same
engine event sequence and final engine snapshot.

Agent input replay is achieved with scripted or fake completion sources. Live
provider calls are captured in prompt envelopes and harness artifacts, not
asserted as deterministic.

Downstream tests must state which layer they cover. Task #832 covers engine
replay. Task #846 covers prompt-level scripted/fake agent replay. Task #847
covers persistence, resume, and inspector projections.

## Observability And Inspector

`Den.RulesEngine` exposes portable trace records through `IRulesEngineObserver`.
The default implementation is no-op. QuillForge subscribes at the bridge layer
and maps trace records into structured logs, harness traces, and inspector data.

The inspector projection introduced in #847 should be `GameInspectorProjection`.
It exposes:

- engine snapshot summary;
- event journal;
- visibility projection by participant;
- channel/DM state;
- prompt cursors;
- memory summaries;
- last N prompt envelopes;
- token usage;
- failure taxonomy records;
- replay validation status.

The inspector is a typed projection. It is not a second gameplay authority.

## Implementation Task Map

- #830 scaffolds portable `Den.RulesEngine` projects and boundary tests.
- #831 defines core engine state, events, intent commands, resolution payloads,
  and module contracts.
- #832 implements `RulesEngineService`, deterministic transitions, event
  journal, replay, seeded RNG enforcement, and trace records.
- #833 implements explicit module registry and setup validation.
- #834 implements baseline Werewolf and deterministic narrator templates.
- #835 adds Werewolf scenario and regression tests.
- #836 designs generic participant channel and direct-message state.
- #837 implements game templates, store, validation, and API contracts.
- #838 adds `SessionState.Game`/`GameRuntimeState` and the game mutation service.
- #839 implements the QuillForge bridge, endpoints, text-to-intent translator,
  and mode-switch rejection.
- #840 implements agent player turns and deterministic application order.
- #841 implements memory summaries, prompt cursors, and prompt envelopes.
- #842 integrates game communication with channels and DMs.
- #843 adds `games` mode registration and the core workspace UI.
- #844 adds template customization UI.
- #845 adds Werewolf UI and narrator projections.
- #846 adds multi-agent harness traces for scripted/fake agent replay.
- #847 adds persistence, replay, resume, and inspector coverage.
- #850 adds future module authoring hooks.
- #848 documents setup, testing, and extension workflow.
- #849 runs final ESS drift review and hardening.

## Test Strategy

Architecture tests:

- `Den.RulesEngine` cannot reference QuillForge, providers, ASP.NET, storage, or
  UI projects.
- Game module registration is explicit.
- Provider SDK types stay outside `Den.RulesEngine` and `QuillForge.Core`.
- Prompt assembly cannot accept raw journal entries, only `AgentVisibleEvents`.

Engine tests:

- deterministic replay with fixed seed;
- unseeded RNG rejection;
- rule handler priority and phase ordering;
- trace records for handler execution and cancellation;
- event ID and sequence monotonicity;
- setup validation errors;
- module version compatibility errors;
- public/private/hidden projection safety;
- invalid intent rejection;
- round/stage transitions.

Werewolf tests:

- role assignment and private role reveal;
- werewolf-set visibility;
- night actions;
- day discussion/vote resolution;
- win conditions;
- tied/missing/invalid votes;
- baseline setup compatibility.

QuillForge service tests:

- session mutation gate prevents same-session interleaving;
- mode switch is rejected during an active game;
- concurrent sessions can run independent games;
- fork/delete active-game semantics;
- template validation split;
- channel/DM permissions;
- translator bypass for structured inputs;
- translator rejection of helpful rewrites;
- agent failure taxonomy records.

Agent and harness tests:

- `AgentVisibleEvents` cannot contain hidden events;
- prompt cursors advance only after prompt inclusion;
- last N prompt envelopes are retained;
- scripted/fake completion replay is stable;
- live provider traces capture nondeterministic outputs without asserting
  equality.

Frontend and contract tests:

- `games` mode is in API and TypeScript mode unions;
- game DTO snapshots are stable;
- no-game and active-game workspace states render;
- inspector projection snapshots include journal, cursors, memory, prompt
  envelopes, and failure records.

## Follow-Up Rules For Implementation

Use clear typed boundaries before adding clever abstractions. Werewolf should
stay small, but the engine service should be shaped for future stacked rules.
If a later PNP module needs complex resolution, it should extend typed payloads,
handlers, trace records, and module contracts rather than bypassing the engine.

When implementation pressure appears, keep these invariants:

- the engine is gameplay authority;
- QuillForge coordinates participants and persistence;
- LLMs never decide gameplay outcomes;
- hidden information crosses only trusted projection boundaries;
- every odd agent behavior leaves a typed trail;
- every mutable state object has one writer-owner.
