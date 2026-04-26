# Architecture Decision: Guide Mode, Assistant Surface, and Mode-Owned Routing

## Status

Accepted for implementation under tasks 620-627.

## Purpose

This note turns the earlier `mode-agent-architecture-guide-assistant-rethink`
design sketch into an implementation-ready target architecture.

It resolves five questions:

- what replaces `general`
- whether `Guide` and `Assistant` are the same thing
- whether QuillForge still needs a user-editable `conductor`
- whether Writer and Roleplay must always ground through Narrative Director
- whether Forge should join that same interaction model

## Current Gaps

The current architecture has several overlapping problems:

- `GeneralMode` is vague and overpowered. It acts like a front desk, a routing
  brain, and a free-form creative surface at the same time.
- `WriterMode` still tells the top-level agent to use `write_prose` directly
  instead of routing through `NarrativeDirectorAgent`.
- `RoleplayMode` already uses `direct_scene`, so the grounding architecture is
  only partially applied today.
- `ProseWriterAgent` can still fall back to "write freely without
  world-building constraints" when lore is missing, which is the opposite of
  the desired canon-first behavior.
- `conductor` is carrying two unrelated concerns at once: user-editable
  personality/style and system routing policy.
- Forge is currently described like a conversational planning mode even though
  its real authority is the explicit pipeline.

The result is an app that still leaves too much room for a model to "freestyle"
across missing boundaries.

## Decisions

### 1. `General` Is Removed and Replaced by `Guide`

`general` is retired. The replacement is a fixed app-owned `guide` mode.

`Guide` is:

- the startup and fallback mode for new sessions
- the front desk for explaining the app, modes, commands, and content layout
- responsible for surfacing obvious configuration, profile, and content
  problems
- strongly biased toward getting the user into a task-specific mode quickly

`Guide` is not:

- a creative collaborator
- a roleplay surface
- a catch-all execution mode
- a user-editable prompt slot

`Guide` should use a fixed system prompt owned in code. It may use `query_docs`
and app-owned health/inspection capabilities, but it should not become a broad
file-management or prose-writing surface by default.

### 2. `Guide` and `Assistant` Are Separate Concepts

They are not the same role.

`Guide` is a mode.

`Assistant` is a constrained user-facing interlocutor contract used inside
specific modes that need a conversational facilitator.

For the first implementation pass:

- `Guide` exists as its own mode
- `Assistant` is not introduced as a standalone selectable mode
- `Assistant` is the user-facing surface inside `council` and `research`

This keeps the mode story clear:

- `guide` explains and routes the user toward a real workflow
- `council` and `research` still remain the user-visible modes
- the user speaks through an `Assistant`-shaped interface in those modes

If a future standalone assistant mode becomes valuable, it can be added later
without blurring the Guide boundary.

### 3. No User-Editable Routing Prompt Survives

QuillForge still needs orchestration, but it does not need a user-editable
LLM routing persona.

The target architecture is:

- top-level routing is app-owned and deterministic by active mode plus explicit
  commands
- each mode owns a narrow execution path
- user-editable prompt content is allowed only where it styles a clearly owned
  role, not where it changes system routing authority

This means:

- `conductor` is removed as a live routing prompt slot
- no replacement user-editable routing prompt is added
- any remaining coordination preferences become code behavior or fixed app-owned
  prompts

Near-term implementation note:

- the existing `OrchestratorAgent` may remain temporarily as an implementation
  shell during migration
- but it should stop being conceptually user-shaped, and it should stop loading
  user conductor content once task 624 lands

The question "do we need an LLM routing agent?" is answered as:

- not as a generic user-facing conductor
- maybe as a tightly scoped response composer inside a mode
- but broad routing across the product should be programmed, not improvised

### 4. Writer and Roleplay Always Ground Through Narrative Director

For canon-sensitive interactive prose, `NarrativeDirector` becomes mandatory.

This applies to:

- `writer`
- `roleplay`

The target flow is:

1. mode-owned app logic interprets the user turn and command state
2. `NarrativeDirector` gathers canon, prior state, narrative rules, and session
   context
3. `NarrativeDirector` decides the next beat and produces a prose brief
4. `NarrativeDirector` updates narrative/story state as needed
5. `ProseWriter` renders the visible prose from that brief

Ownership split:

- `Librarian` owns semantic retrieval from the active lore document corpus only
- `query_context` owns broad source-aware lookup across character cards,
  session canon, plot/story state, recent conversation, and lore-document
  snippets
- Lore Builder owns creation and maintenance of durable lore files
- `NarrativeDirector` owns grounding, planning, and narrative-state updates
- `ProseWriter` owns prose rendering and voice execution

`ProseWriter` is not the first-order scene decider anymore.

Operational consequences:

- top-level Writer mode should no longer instruct the system to call
  `write_prose` directly as the first move
- `write_prose` remains a sub-agent tool used by Narrative Director and other
  tightly owned flows, not the default top-level surface for scene creation
- corrections about characterization or canon should trigger re-grounding, not
  local patching

### 5. Missing Canon or Misconfiguration Must Disclose and Stop

The app should fail loud when a mode depends on canon or profile material that
is missing, empty, or misconfigured.

That includes:

- empty or missing lore sets in canon-sensitive scene flows
- missing narrative rules or character context required by the mode
- broken content paths for required profile assets
- failed lore lookups or tool failures during grounded scene generation

The system should disclose the problem and ask the user to fix or confirm the
next step. It should not silently loosen constraints and continue writing from
generic narrative priors.

This explicitly retires the current "write freely" fallback in
`ProseWriterAgent` for grounded interactive writing paths.

### 6. `Assistant` Is the Interlocutor for Council and Research

`Council` and `Research` need a user-facing voice, but not a broad do-anything
controller.

`Assistant` is that voice.

The Assistant contract is:

- fixed base system prompt owned by the app
- optional user-editable style/personality layered underneath that base prompt
- limited tool surface
- no direct file-system tool access in the first pass
- no impersonation of downstream agents or missing subsystems

In practice:

- `council` uses Assistant to gather and synthesize council output
- `research` uses Assistant to frame research requests and synthesize findings
- the user experiences an interlocutor, but substantive work still belongs to
  the specialized underlying services and agents

The exact durable name of the editable Assistant prompt slot is deferred to task
623, but it is explicitly not a replacement for `conductor`.

### 7. Forge Stays Command- and Pipeline-Owned

Forge does not adopt the Writer/Roleplay scene-grounding architecture.

Decision:

- Forge remains owned by explicit commands, services, and pipeline stages
- `NarrativeDirector` does not become the default front door for Forge
- `Assistant` does not become the owner of Forge stage execution

If Forge later gains a conversational helper, that helper should be limited to:

- explaining Forge concepts and commands
- showing status
- helping the user prepare valid inputs

It should not blur ownership of planning, writing, review, or assembly stages.

This keeps Forge testable and makes pipeline authority obvious.

## Target User-Facing Model

The intended steady-state mode story is:

- `guide`: onboarding, explanation, diagnostics, mode selection
- `writer`: user talks to the prose pipeline, grounded by Narrative Director
- `roleplay`: user talks to in-scene prose, grounded by Narrative Director
- `council`: user talks to Assistant, which convenes and synthesizes advisors
- `research`: user talks to Assistant, which coordinates sourced investigation
- `forge`: user operates an explicit command and pipeline domain

## Migration Boundaries

### Mode Names and Session Compatibility

- `guide` replaces `general` as the target mode name everywhere user-facing
- legacy persisted `general` values should deserialize as `guide` during the
  migration window
- API endpoints and slash commands should accept `general` as a compatibility
  alias until UI and stored sessions have been migrated
- new sessions should default to `guide`, not `general`

### Profile and Session Model Impact

Current durable/runtime shapes expose `conductor` in:

- `ProfileConfig`
- `SessionState.Profile.ActiveConductor`
- profile/session DTOs
- status and diagnostics payloads

Target direction:

- `conductor` is removed from live profile selection and prompt composition
- any new user-editable style for interlocution lives in a distinct Assistant
  prompt slot
- Guide has no user-editable prompt slot

Compatibility boundary:

- keep the legacy `conductor` field and files readable during migration
- do not let them continue to shape live routing behavior after task 624
- treat existing conductor files as migration material, not as a long-term
  runtime dependency

### Prompt and Content Directory Impact

Current prompt families are:

- `conductor/`
- `narrative-rules/`
- `writing-styles/`

Target direction:

- `narrative-rules/` remains the Narrative Director-facing editable slot
- `writing-styles/` remains the ProseWriter-facing editable slot
- `conductor/` becomes legacy-only during migration and should eventually be
  retired from active UI flows
- Assistant gets its own editable prompt family in a later task
- Guide remains app-owned and does not add a user-editable prompt directory

### Endpoint and UI Impact

The following surfaces are expected to change under follow-on tasks:

- mode switcher copy and allowed values
- profile picker conductor selector
- prompt browser conductor tab
- header/status surfaces that show conductor identity
- context meter and token accounting that currently include conductor tokens
- `/api/conductors` endpoints and related frontend API calls
- probe/debug prompt reconstruction that currently loads conductor text

### Runtime and Service Impact

The current `OrchestratorAgent` can be migrated incrementally rather than
deleted in one step.

Recommended transition:

1. remove `general` behavior in favor of Guide behavior
2. narrow mode-owned tool sets and routing rules
3. make Writer and Roleplay deterministic at the top level
4. remove conductor loading from interactive request preparation and prompt
   assembly
5. rename/refactor orchestration types later if the old names become misleading

This keeps the migration safe while still moving toward app-owned routing.

### Testing and Probe Impact

Regression coverage should shift from the old "general free-form router"
expectations to the new boundaries:

- Guide explains and redirects instead of doing the work
- Writer and Roleplay always ground through Narrative Director
- missing canon/config causes disclosure rather than freestyle prose
- Council and Research use Assistant-style synthesis over specialized services
- Forge stays explicit and command-owned

The interpretation probe should also stop treating conductor text as the main
coordination layer once task 624 lands.

## Bounded Open Questions

These questions remain open, but they do not block the core decisions above:

- what the durable editable Assistant prompt slot should be named
- whether to rename `OrchestratorAgent` immediately or only after conductor
  removal is complete
- whether Forge eventually needs a small helper surface for command education
  and status explanation

None of these reopen the main decisions in this note.

## Task Map

- task 620: replace `general` with `guide`
- task 621: make Narrative Director mandatory for Writer and Roleplay
- task 622: remove freestyle fallback and disclose canon/config failures
- task 623: add Assistant-backed Council/Research interaction
- task 624: remove conductor as a live routing prompt slot
- task 625: keep Forge pipeline-owned and implement any explicit helper boundary
- task 626: add docs for the new interaction model
- task 627: add regression coverage for the new mode and agent boundaries
