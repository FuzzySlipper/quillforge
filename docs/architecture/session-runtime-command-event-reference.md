# Session Runtime Command/Event Reference

## Status

Reference implementation

## Why This Exists

This note turns the Event/Service/State guidance into one concrete QuillForge
slice that future contributors can copy.

The goal is not to add a bus or a mediator. The goal is to make the safe path
obvious:

- callers submit a named command when they are asking a service-owned state
  slice to change
- the owning service validates and sequences the change
- successful mutations return a named event describing what happened
- busy or invalid outcomes stay explicit in `SessionMutationResult<T>`

That shape gives agent-authored code good friction. A future change can follow a
named command and a named event instead of grabbing at mutable state and
inventing ad-hoc booleans.

## Authority Principle

The session runtime slice owns per-session interactive state. Endpoints,
handlers, and agents are not allowed to mutate that state directly.

In QuillForge terms:

- endpoints and tools issue commands
- `SessionRuntimeService` is the authority that loads state, validates intent,
  acquires the mutation gate, persists transitions, and logs what happened
- `SessionState` remains the single-owner runtime state
- returned events describe facts after the authority made its decision

That is the Den.Core authority principle adapted to QuillForge without adding
extra infrastructure.

## Reference Slice

The writer-pending review workflow in
[`SessionRuntimeService`](../../src/QuillForge.Core/Services/SessionRuntimeService.cs)
is the reference implementation.

Command inputs:

- [`CaptureWriterPendingCommand`](../../src/QuillForge.Core/Models/SessionRuntimeCommands.cs)

Event outputs:

- [`WriterPendingContentCapturedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs)
- [`WriterPendingCaptureSkippedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs)
- [`WriterPendingContentAcceptedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs)
- [`WriterPendingContentRejectedEvent`](../../src/QuillForge.Core/Models/SessionRuntimeEvents.cs)

Supporting result wrapper:

- [`SessionMutationResult<T>`](../../src/QuillForge.Core/Models/SessionMutationResult.cs)

## The Loop In QuillForge Terms

The Den.Core loop maps onto this QuillForge slice as:

1. A caller creates a command that expresses intent.
2. The owning service loads its state and applies validation rules.
3. The service persists the transition if allowed.
4. The service returns an event-shaped fact describing what happened.
5. The caller reacts to the fact instead of inferring behavior from raw state.

For writer-pending capture, that means the caller no longer has to infer whether
capture happened by diffing `SessionState`. It receives either a captured fact
or a skipped fact directly.

## Why Typed Dispatch Matters

This is especially important for agent-authored changes.

Without named command/event types, an agent will often:

- pass primitive bags through multiple layers
- inspect state after the fact to guess what happened
- duplicate validation logic in endpoints or handlers

That code tends to work in one path and drift in the next.

With this reference shape:

- the command says what the caller is asking for
- the service is the only place that decides
- the event says what actually happened
- tests can assert on explicit facts instead of incidental state combinations

## How To Copy This Pattern

Use this shape when a service mutation is important enough that naming the
request and the resulting fact improves clarity.

Follow these rules:

- keep commands imperative and request-shaped
- keep events past-tense and fact-shaped
- let the service own validation, sequencing, persistence, and logging
- use `SessionMutationResult<T>` or another explicit result wrapper for busy and
  invalid outcomes
- do not put mutation logic inside the command or event types
- do not bypass the service by mutating owned state from an endpoint or tool

## When Not To Copy It

Do not introduce command/event wrappers for trivial CRUD-style methods that are
already obvious from their signatures.

This is a reference pattern for real state transitions, not a mandate to wrap
every method in a record.
