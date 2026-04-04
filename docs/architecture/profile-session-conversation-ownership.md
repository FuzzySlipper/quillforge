# Ownership Spec: AppConfig, ProfileConfig, SessionState, and ConversationTree

## Status

Proposed

## Purpose

This note defines the ownership model for QuillForge's app defaults, reusable user
profiles, live session runtime, and persisted conversation history.

It is the reference vocabulary for follow-on work under tasks 386-389 and for
future code review. When a change touches config, profile selection, session
behavior, or conversation lifecycle, this document is the default source of
truth.

## Design Direction

QuillForge should follow the same broad discipline described in Den.Core:

- config and state are noun-shaped data
- services own mutation rules and coordination
- persisted artifacts keep descriptive names when shape matters

QuillForge is not required to force every flow through a full event bus before it
can adopt the ownership model. The important part here is clear authority:

- `AppConfig` owns app-wide durable defaults
- `ProfileConfig` owns reusable durable user choices
- `SessionState` owns one live interactive run
- `ConversationTree` owns branching chat history as an artifact
- `*Service` types own mutation rules, coordination, and lifecycle behavior

## Vocabulary

### `AppConfig`

`AppConfig` is the durable application-level configuration document.

It contains:

- provider configuration and model assignments
- app-wide defaults
- app-wide UI defaults such as layout/theme where those are truly global
- the default `ProfileConfig` selection for new sessions
- other durable preferences that are not specific to one session

It does not contain:

- the active profile for an already-running session
- session-scoped mode, plot, file, or draft state
- conversation history
- last-opened session behavior masquerading as config

Rule: if changing a value should affect only one conversation run, it does not
belong in `AppConfig`.

### `ProfileConfig`

`ProfileConfig` is a reusable durable configuration bundle that many sessions may
share.

A profile is the right home for reusable author intent such as:

- conductor selection
- lore set
- narrative rules
- writing style
- any other user-facing bundle choice that should be reused across sessions

A profile is not:

- a session transcript
- a live mutable draft workspace
- a replacement for `ConversationTree`

One `ProfileConfig` may seed zero, one, or many sessions.

### `SessionState`

`SessionState` is the authoritative live mutable runtime for one interactive run.

It owns:

- which profile the session runs under
- mode and current working context
- writer pending-review state
- narrative runtime such as director notes and plot progress
- explicit session-scoped overrides when the user intentionally diverges from the
  base profile for just this session

It does not own:

- reusable profile definitions
- app defaults
- the persisted message tree itself

`SessionState` is now the implementation name in code and should be taught
directly in new work.

### `ConversationTree`

`ConversationTree` is the persisted branching conversation artifact.

This name should stay descriptive. It is not just another anonymous "state" bag.
The tree name matters because it communicates:

- branching structure
- fork/regenerate semantics
- message identity by GUID
- persistence as an artifact, not just a runtime scratch object

Rule: do not rename `ConversationTree` to a more generic `ConversationState`,
`ChatState`, or similar term just to make the naming family look uniform. The
shape is part of the meaning.

### `*Service`

`*Service` names are for behavior owners.

A service owns:

- mutation rules
- validation
- sequencing
- derived-context preparation
- lifecycle coordination across config/state/artifacts

A service does not become a synonym for stored data.

Rule: config and state are nouns; services are the authority that changes them.

## Current Implementation Mapping

Today the codebase still mixes old and new concepts:

- [`AppConfig`](../../src/QuillForge.Core/Models/AppConfig.cs) still carries
  active profile-like selections such as `Persona.Active`, `Lore.Active`,
  `NarrativeRules.Active`, and `WritingStyle.Active`.
- [`SessionState`](../../src/QuillForge.Core/Models/SessionState.cs)
  already has the right general ownership shape for session runtime, but its
  `Profile` sub-state currently mirrors global config defaults rather than
  pointing at a first-class reusable profile.
- [`/api/profiles/switch`](../../src/QuillForge.Web/Endpoints/ProfileEndpoints.cs)
  currently mutates `AppConfig`, which makes a session-scoped interaction look
  like an app-global preference change.
- session fork and delete behavior in
  [`SessionEndpoints`](../../src/QuillForge.Web/Endpoints/SessionEndpoints.cs)
  currently operates on `ConversationTree` only, not on the paired session
  runtime document.

Those mismatches are transitional, not the desired steady-state model.

## Ownership Boundaries

### `AppConfig` Boundary

`AppConfig` is for defaults and true globals.

Examples that belong here:

- provider keys and provider settings
- model routing defaults
- diagnostics defaults
- default profile for new sessions
- layout/theme defaults that apply across the whole app unless overridden

Examples that do not belong here:

- "this session is using profile X right now"
- "this writer session has pending prose waiting for review"
- "this branch of the conversation is in roleplay mode"

### `ProfileConfig` Boundary

`ProfileConfig` is for durable reusable composition.

Examples that belong here:

- `ConductorId`
- `LoreSet`
- `NarrativeRulesId`
- `WritingStyleId`
- any future reusable bundle setting that should travel with a named profile

Examples that do not belong here:

- current project file
- director notes for a specific scene
- branch-local experiments
- message history

### `SessionState` Boundary

`SessionState` is for run-local truth.

Examples that belong here:

- `ProfileId`
- active mode
- project/file/character context
- writer pending content
- director notes
- active plot file
- plot progress

The session is the authority for "what this run is doing now."

### `ConversationTree` Boundary

`ConversationTree` stores the branching message artifact for a session.

It should not absorb:

- active profile selection
- mode or plot runtime
- app defaults

Likewise, `SessionState` should not absorb the message graph just because both
are persisted per session.

They are sibling artifacts that move together in lifecycle operations while
remaining distinct concepts.

## Session Profile Rules

### Authoritative Session Binding

The session should be authoritative about which profile it runs under.

Target rule:

- every session stores a `ProfileId`
- prepared runtime context is resolved from `SessionState + ProfileConfig +
  durable content`
- `AppConfig` only supplies the default `ProfileId` for new sessions

Changing the active profile for one session must not require mutating global app
config.

### Session-Scoped Overrides

`SessionState` should store more than only `ProfileId`, but only under strict
rules.

Allowed rule:

- store sparse session-scoped overrides when the user intentionally says, in
  effect, "use this session-specific variation without changing my reusable
  profile"

Examples of acceptable overrides:

- temporarily switching the lore set for one session
- trying a different writing style for one branch/run
- changing conductor behavior for a single experimental session

Restrictions:

- overrides are explicit, not accidental side effects
- overrides are session-local and must never back-write into `ProfileConfig`
- overrides must never silently mutate `AppConfig`
- if no override exists, resolution falls back to `ProfileConfig`

So the ownership model is:

- `ProfileConfig` is the reusable base
- `SessionState` stores `ProfileId`
- `SessionState` may also store explicit per-session override values

This keeps sessions flexible without letting reusable config and live runtime
collapse into one blurry object.

## Lifecycle Semantics

### New Session

Creating a new session should create both:

- a new `ConversationTree`
- a new `SessionState`

The new session is seeded from:

- an explicitly requested `ProfileConfig`, or
- the default profile defined by `AppConfig`

If compatibility code still inherits from legacy default runtime files during
the migration, that should be treated as a temporary bridge, not the long-term
model.

### Fork Session

Forking creates a new owned session unit.

It should produce:

- a new `ConversationTree` seeded from the selected branch/thread
- a new `SessionState` cloned from the source session, subject to explicit reset
  rules

Important clarification:

- fork is branch creation, not historical runtime reconstruction
- QuillForge does not currently persist a full time-travel log of runtime state
  per message
- therefore, a fork from an earlier message clones the source session's current
  runtime shape unless a service deliberately defines narrower reset behavior

Required reset rule for forked session state:

- clear truly transient review/in-flight fields that should not leak into the
  new session by accident

Example:

- writer pending review content should not automatically come along unless the
  forking service explicitly decides that it is meaningful for the new run

### Delete Session

Deleting a session deletes the whole owned session unit.

Required behavior:

- delete the persisted `ConversationTree`
- delete the persisted `SessionState`

Deleting only one of the two is an ownership bug.

### Delete Message

Deleting a message is not the same as deleting a session.

Deleting a message:

- mutates the `ConversationTree`
- does not implicitly delete the session runtime document
- does not implicitly rewrite profile ownership

If message deletion later needs mode-specific cleanup, that cleanup should be
owned by an explicit service rule rather than by the storage layer guessing.

## Terminology Direction

`persona` is deprecated as a structural term.

Use these replacements:

- `conductor` for the prompt/voice/instruction file currently exposed through
  legacy persona routes
- `profile` for the reusable named configuration bundle
- `session` for the live interactive run
- `conversation tree` for the branching message artifact

Guidance:

- do not use `persona` as the umbrella word for profile, session, and
  conductor-like concepts
- expose conductor prompt editing through conductor-named routes and stores
  instead of compatibility `/api/persona` endpoints
- prefer conductor-first API and client contracts (`conductor`,
  `activeConductor`, `conductors`) for live runtime behavior
- preserve legacy persisted session overrides by treating `activePersona` as a
  read-only compatibility alias for the renamed `activeConductor` field

## Service Ownership Rules

### App Config Service Boundary

`IAppConfigStore` and related config services own durable app-default writes.

Examples:

- change default profile for future sessions
- change global layout default
- update provider/model configuration

### Profile Service Boundary

A dedicated profile service/store should own:

- list/load/save/delete for `ProfileConfig`
- validation of profile bundle fields
- profile cloning or rename semantics

### Session Service Boundary

The session runtime service owns:

- session-scoped profile selection
- session overrides
- mode changes
- writer pending transitions
- narrative runtime mutation

This is the service boundary that should answer "what profile is this session
actually using right now?"

### Session Context Service Boundary

Prepared prompt/tool context should be derived by a service that reads:

- `SessionState`
- `ProfileConfig`
- relevant durable content files

It should not need to guess whether to trust `AppConfig` or session runtime.

## Review Heuristics

Use these quick checks in future code review:

- If a user action changes only one running session, it should not mutate
  `AppConfig`.
- If data should be reusable across many sessions, it should not live only in
  `SessionState`.
- If the data is the message graph, keep the `ConversationTree` name.
- If behavior coordinates mutation or lifecycle, it belongs in a `*Service`.
- If code uses `persona` to mean profile/session/conductor interchangeably, the
  naming is still wrong.

## Immediate Implications For Follow-On Tasks

- Task 386 should make session-owned profile selection authoritative and stop
  treating `AppConfig` as the active truth for a running session.
- Task 387 should introduce first-class durable `ProfileConfig` storage and make
  `AppConfig` point to a default profile rather than storing all active profile
  fields as the only durable truth.
- Task 388 should make fork/delete operate on the owned pair:
  `ConversationTree + SessionState`.
- Task 389 should continue replacing structural `persona` language with
  `conductor` and `profile`, while keeping compatibility shims where needed.
