# Service Command/Event Conventions

## Status

Proposed

## Purpose

This note defines the lightweight command/event vocabulary QuillForge should use
for service APIs and streaming/runtime signals.

It is intentionally small. The goal is to make naming and extension decisions
more predictable without introducing a separate bus, mediator layer, or event
infrastructure before the product needs one.

## Core Rule

- `*Command` means requested intent that asks the system to do something
- `*Event` means a fact that something happened

That naming distinction is useful even when both flows still use ordinary method
calls, return values, and in-process objects.

QuillForge does not need a dedicated command dispatcher or event bus to benefit
from the vocabulary.

## Commands

Use a `*Command` record when a service operation is mutation-heavy and any of
these are true:

- the request changes owned state or coordinates multiple stores/services
- the request shape has enough fields that positional parameters become noisy
- the same request will likely be built at multiple call sites
- naming the request clarifies intent better than a bag of primitives

Command types should stay:

- small
- explicit
- transport-agnostic
- scoped to one mutation intent

Examples in the current codebase:

- [`SetSessionProfileCommand`](../../src/QuillForge.Core/Models/SessionRuntimeCommands.cs)
- [`SetSessionRoleplayCommand`](../../src/QuillForge.Core/Models/SessionRuntimeCommands.cs)
- [`SetSessionModeCommand`](../../src/QuillForge.Core/Models/SessionRuntimeCommands.cs)
- [`CreateSessionCommand`](../../src/QuillForge.Core/Services/ISessionBootstrapService.cs)
- [`ActivateCharacterCardsCommand`](../../src/QuillForge.Web/Services/CharacterCardCommandService.cs)

## Events

Use an `*Event` type when the object describes something that already happened
and consumers should react to, observe, or display it.

In QuillForge today, events are mostly in-process runtime signals rather than
durable integration messages.

Examples in the current codebase:

- streaming output events such as
  [`TextDeltaEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs),
  [`ToolCallDeltaReceivedEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs),
  [`ToolCallValidatedEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs), and
  [`DoneEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- forge pipeline events such as `StageStartedEvent` and `ForgeCompletedEvent`
  under `src/QuillForge.Core/Pipeline/`

Events are not commands with a different suffix. They should describe facts, not
requests.

For streaming specifically, transport-facing facts and app-facing facts may both
exist in the same overall flow:

- transport facts describe what the provider emitted, such as
  `ToolCallDeltaReceivedEvent`
- app facts describe what QuillForge accepted or produced, such as
  `ToolCallValidatedEvent` and `DiagnosticEvent`

## When Not To Add Commands

Do not force every service method behind a `*Command` type.

Direct parameters are still preferred when the operation is simple and the
intent is already obvious from the method signature.

Current examples that should stay simple:

- [`IProfileConfigService.SelectAsync`](../../src/QuillForge.Core/Services/IProfileConfigService.cs)
- [`IProfileConfigService.CloneAsync`](../../src/QuillForge.Core/Services/IProfileConfigService.cs)
- [`IProfileConfigService.DeleteAsync`](../../src/QuillForge.Core/Services/IProfileConfigService.cs)

Those methods are already clear, have small parameter lists, and do not benefit
from extra wrapper records.

## Results

Commands do not require a separate command framework. The usual QuillForge shape
is:

- method takes a `*Command` when the request is mutation-shaped
- method returns a domain result record or model
- the owning service keeps validation, sequencing, and persistence rules

For session state mutation, that shape is already visible in
[`ISessionStateService`](../../src/QuillForge.Core/Services/ISessionStateService.cs)
with command inputs and
[`SessionMutationResult<T>`](../../src/QuillForge.Core/Models/SessionMutationResult.cs)
outputs.

## Review Against Current Code

### Session State Service

[`SessionRuntimeService`](../../src/QuillForge.Core/Services/SessionRuntimeService.cs)
is the strongest current match for the convention.

Why it fits:

- it owns real mutations over session-owned state
- several operations already accept dedicated command records
- the command types keep endpoint code readable
- the shared `SessionMutationResult<T>` keeps busy/invalid handling explicit

This is the model future session/profile/runtime mutation work should generally
follow.

The writer-pending review flow is now the concrete reference implementation:

- command input:
  [`CaptureWriterPendingCommand`](../../src/QuillForge.Core/Models/SessionRuntimeCommands.cs)
- event outputs:
  [`WriterPendingContentCapturedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs),
  [`WriterPendingCaptureSkippedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs),
  [`WriterPendingContentAcceptedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs),
  and
  [`WriterPendingContentRejectedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs)

See
[`session-runtime-command-event-reference.md`](./session-runtime-command-event-reference.md)
for the reasoning, authority model, and expected call pattern.

### Profile Config Service

[`ProfileConfigService`](../../src/QuillForge.Core/Services/ProfileConfigService.cs)
is a good example of where not to add command ceremony by default.

Why:

- many methods are straightforward CRUD or lookup operations
- parameter counts are small
- wrapping `DeleteAsync(profileId)` or `SelectAsync(profileId)` in command
  records would add names without adding clarity

If a profile mutation later grows cross-service coordination or a wider request
shape, introducing a command at that point is fine. It does not need to happen
preemptively.

## Non-Goals

This note does not introduce:

- a mediator pattern
- a command bus
- persistent domain events
- forced command/event wrappers for every endpoint
- a rename sweep for existing result types that are already clear

The point is vocabulary and consistency, not infrastructure.

## House Style Summary

- use `*Command` for named mutation intent
- use `*Event` for facts that happened
- keep both as plain records/classes until real infrastructure is justified
- prefer direct parameters for obvious small operations
- keep validation and persistence rules in the owning service, not in the
  command/event types themselves
