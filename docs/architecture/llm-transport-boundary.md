# LLM Transport Boundary for QuillForge-Owned Models

## Status

Proposed

## Purpose

This note defines where raw provider payloads may exist in QuillForge, where
they must be translated into QuillForge-owned models, and how malformed data
should surface when the boundary is crossed.

It is the reference note for task 417 and the follow-on implementation slices
under tasks 418-421. When a change touches provider adapters, tool dispatch,
streaming events, or model-authored structured payloads, this document is the
default source of truth.

## Problem

QuillForge currently carries `JsonElement` and other opaque transport data
further inward than it should.

That looseness has been survivable, but it creates three recurring failures:

- malformed provider payloads are sometimes repaired into empty objects or
  fallback text instead of being rejected at the first trustworthy boundary
- tool handlers and agent code can grab fields directly from untyped JSON,
  which works initially but bypasses compiler-checkable contracts
- provider-specific replay data needed for reasoning models can leak into
  Core-facing types unless the ownership boundary is stated explicitly

This is the exact kind of brittleness the Den.Core Event/Service/State work is
meant to reduce. The goal is not ceremony for its own sake. The goal is "good
friction": make the correct typed path easier to extend than ad hoc JSON access.

## Motivation

The motivation here is grounded in both QuillForge production behavior and the
Den.Core research base:

- QuillForge has already needed defensive recovery for incomplete streamed tool
  calls in
  [`ToolLoop`](../../src/QuillForge.Core/Agents/ToolLoop.cs) and both provider
  adapters.
- reasoning-capable providers already require explicit raw replay data in
  [`ReasoningCompletionService`](../../src/QuillForge.Providers/Adapters/ReasoningCompletionService.cs),
  proving that the provider edge is real and cannot be wished away.
- the Den.Core authority principle says external input is submitted to the app,
  not trusted as already-valid internal state.
- the Den.Core research note found that roughly 94% of LLM compilation errors
  are type errors. In practice, that means typed seams are not bureaucracy;
  they are one of the strongest ways to keep agent-authored changes on the rails.

## Known Failure Patterns

The boundary spec should respond to observed and current failure modes, not an
imagined clean-room architecture.

### 1. Streamed tool arguments can arrive incomplete or malformed

Current evidence:

- [`ChatClientCompletionService`](../../src/QuillForge.Providers/Adapters/ChatClientCompletionService.cs)
  accumulates partial tool-call fragments, then falls back to `{}` when argument
  JSON cannot be parsed.
- [`ReasoningCompletionService`](../../src/QuillForge.Providers/Adapters/ReasoningCompletionService.cs)
  does the same for OpenAI-compatible streamed `tool_calls`.

Why this is brittle:

- the adapter has already detected malformed transport data, but the malformed
  payload is converted into an apparently valid empty object
- the real failure is then deferred to a later handler, or worse, silently
  interpreted as "missing optional fields"

### 2. Streams can claim tool use without delivering tool call payloads

Current evidence:

- [`ToolLoop`](../../src/QuillForge.Core/Agents/ToolLoop.cs) contains explicit
  recovery logic for `stop_reason=tool_use` with zero streamed tool calls,
  followed by a non-streaming retry.

Why this matters:

- this is a transport-layer inconsistency, not a domain-level business rule
- the system already knows it is in degraded recovery mode and should surface
  that fact clearly rather than letting later code guess what happened

### 3. Tool handlers currently parse raw `JsonElement` directly

Current evidence:

- [`IToolHandler`](../../src/QuillForge.Core/Services/IToolHandler.cs) exposes
  `HandleAsync(JsonElement input, ...)`
- handlers such as
  [`QueryLoreHandler`](../../src/QuillForge.Core/Agents/Tools/QueryLoreHandler.cs),
  [`WriteProseHandler`](../../src/QuillForge.Core/Agents/Tools/WriteProseHandler.cs),
  and
  [`UpdateNarrativeStateHandler`](../../src/QuillForge.Core/Agents/Tools/UpdateNarrativeStateHandler.cs)
  read fields directly from JSON

Why this is brittle:

- required fields fail as runtime exceptions or ad hoc `ToolResult.Fail(...)`
- wrong shapes are handled inconsistently from tool to tool
- contributors are rewarded for grabbing one more property from JSON instead of
  extending a typed request model

### 4. Some tools rehydrate arbitrary nested JSON into loose object graphs

Current evidence:

- [`StoryStateHandlers`](../../src/QuillForge.Core/Agents/Tools/StoryStateHandlers.cs)
  converts arbitrary JSON into `Dictionary<string, object>` and nested lists

Why this is brittle:

- shape drift is accepted by default
- numbers, arrays, and nested objects lose domain meaning at the boundary
- later failures become state-shape bugs rather than immediate validation errors

### 5. Agent code reads transport-shaped tool payloads after dispatch

Current evidence:

- [`ProseWriterAgent`](../../src/QuillForge.Core/Agents/ProseWriterAgent.cs)
  and
  [`ForgeWriterAgent`](../../src/QuillForge.Core/Agents/ForgeWriterAgent.cs)
  inspect `ToolUseBlock.Input` directly to recover lore query strings

Why this is brittle:

- this is Core business logic reading transport JSON after the tool boundary
- once this pattern exists, it spreads naturally because it is convenient

### 6. Some model-authored structured output still relies on fallback JSON parsing

Current evidence:

- [`LibrarianAgent`](../../src/QuillForge.Core/Agents/LibrarianAgent.cs) parses
  assistant text through direct JSON parse, markdown-fence stripping, balanced
  brace extraction, and finally raw text fallback

Why this matters:

- this is not provider transport parsing, but it is the same category of risk:
  a structured payload is expected, yet malformed shapes degrade into looser
  fallbacks instead of crossing an explicit typed validation boundary

This note does not require solving all model-authored JSON parsing immediately,
but it does name the pattern so future contributors do not mistake it for a
preferred design.

## Den.Core Primitive Mapping

QuillForge does not need a full event bus before it can use Den.Core vocabulary.
The important part is consistent authority and naming.

### Command

Commands are external intent submitted into the LLM layer.

In QuillForge this includes:

- an incoming chat turn submitted to the orchestrator
- a request to run the tool loop for an agent turn
- a validated request to invoke one specific tool with typed arguments

These are imperative requests. They are not facts yet.

### Event

Events are immutable facts produced while the LLM layer is running.

Current examples:

- [`TextDeltaEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- [`ReasoningDeltaEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- [`ToolCallDeltaReceivedEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- [`ToolCallValidatedEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- [`DoneEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)
- [`DiagnosticEvent`](../../src/QuillForge.Core/Models/StreamEvent.cs)

Design direction:

- transport-facing events should describe what the provider emitted
- domain-facing events should describe what QuillForge accepted, validated, or
  rejected
- the high-traffic tool-call stream now follows that split explicitly:
  providers emit `ToolCallDeltaReceivedEvent`, while `ToolLoop` emits
  `ToolCallValidatedEvent` only after input validation succeeds

Those two concerns may still share a file today, but they should not share an
ambiguous payload contract forever.

### State

State is the single-owner record of what is true now.

In QuillForge's LLM flows that means:

- `SessionState` owns live runtime context for a session
- `ConversationTree` owns the persisted branching transcript artifact
- provider replay payloads are not state; they are opaque transport baggage that
  may be carried temporarily but are not authoritative domain truth

### Service

Services are stateless behavior owners that validate, translate, coordinate, and
publish results.

In this slice, that includes:

- provider adapters implementing `ICompletionService`
- [`ToolLoop`](../../src/QuillForge.Core/Agents/ToolLoop.cs)
- agent classes that configure prompts and tools around the loop
- named tool handler types implementing `IToolHandler`

The folder name `Agents/` does not change the service responsibility: these
objects should still behave like stateless behavior owners.

## Allowed Raw Edge Zones

Raw JSON and provider-owned payloads are allowed only in the following zones.

### 1. Provider SDK and HTTP payload parsing inside `QuillForge.Providers`

Allowed examples:

- raw response parsing in
  [`ChatClientCompletionService`](../../src/QuillForge.Providers/Adapters/ChatClientCompletionService.cs)
- raw response parsing and raw request construction in
  [`ReasoningCompletionService`](../../src/QuillForge.Providers/Adapters/ReasoningCompletionService.cs)

Rule:

- provider-specific JSON may be read, accumulated, and normalized here
- this is the outer transport edge, so `JsonDocument`, `JsonElement`,
  `JsonObject`, and SDK-native content types are acceptable here

### 2. Tool declaration schema as opaque JSON Schema metadata

Allowed example:

- [`ToolDefinition.InputSchema`](../../src/QuillForge.Core/Models/CompletionTypes.cs)

Rule:

- JSON Schema is itself JSON, so keeping declaration metadata as raw schema is
  acceptable
- the schema object is descriptive metadata, not business data to be inspected
  ad hoc across the app

### 3. Opaque provider replay payloads used only for same-provider round-tripping

Allowed examples:

- `CompletionMessage.ProviderReplay`
- `CompletionResponse.ProviderReplay`
- `DoneEvent.ProviderReplay`

Rule:

- these payloads may remain opaque because they exist to preserve provider
  fidelity for a later adapter round-trip
- they are pass-through transport artifacts, not app-domain models
- no code outside the owning provider adapter family should branch on their
  internal shape

## Required Translation Points

Everywhere else, raw transport data should be translated into QuillForge-owned
models before business logic continues.

### 1. Provider response to completion model boundary

Translation should happen inside the adapter.

Required output shape:

- `CompletionResponse`
- `MessageContent`
- typed content blocks
- transport events for streaming

Rule:

- Core should receive QuillForge models, not provider SDK types
- if the provider payload is malformed, the adapter should fail or emit an
  explicit transport error path instead of silently manufacturing a safe default

### 2. Tool call payload to typed tool arguments boundary

Translation should happen exactly once, at the ToolLoop boundary, before a tool
handler's business logic runs.

Rule:

- handlers should receive a typed argument model or validated envelope
- handlers should not call `GetProperty(...)` on raw provider JSON
- schema validation belongs before handler dispatch, not spread through handler
  bodies
- outward-facing app events after that boundary should describe validated tool
  invocation, not raw provider tool-call deltas

### 3. Tool result and tool call observation inside agents

Translation should happen before agent/business code inspects tool usage.

Rule:

- agent code should observe typed tool-invocation records or typed telemetry
- it should not recover business facts by reopening `ToolUseBlock.Input`

### 4. Structured model output to domain model boundary

When QuillForge expects structured output from the model, it should move through
an explicit typed parser/validator before domain code consumes it.

Rule:

- a parsing fallback may still exist during migration
- the long-term target is typed deserialization plus explicit validation, not
  brace extraction and best-effort recovery in business code

## Validation and Error Surfacing

Malformed transport data should fail at the first trustworthy boundary and
surface in three places at once: logs, diagnostics, and caller-visible results.

### Provider-edge failures

When adapter parsing fails or detects an incomplete payload:

- log a structured error with provider/model/session context
- include a clipped raw payload or argument fragment for debugging
- preserve the fact that this was a transport failure, not a business-rule
  rejection
- prefer an explicit failed completion path over silently substituting `{}` when
  the malformed shape changes meaning

### Tool-argument validation failures

When a tool call fails schema or typed-model validation:

- do not invoke the handler body
- emit a `DiagnosticEvent` when live diagnostics are enabled
- log the tool name, tool id, session id, and validation error
- return a structured `ToolResult.Fail(...)` message that the model can respond
  to without crashing the loop

This preserves current tool-loop resilience while making the failure reason
specific and early.

### Domain-model parse failures

When a model-authored structured payload cannot be translated into a domain
model:

- keep the failure localized to the parser boundary
- log the raw text and parse reason
- decide explicitly whether the caller receives a fallback value or a typed error
- do not let malformed payloads mutate owned state first and only fail later

## Good Friction Rules

This refactor should intentionally make the correct path the easy path.

Rules:

- if a typed request model exists, use it instead of adding another
  `TryGetProperty(...)`
- if a provider-specific field must survive round-trips, keep it opaque and
  adapter-owned instead of teaching the rest of Core its shape
- if a handler needs a new field, extend the typed args model and its validation
  rather than reading more raw JSON in the handler
- if a structured payload affects owned state, validate it before it crosses the
  service boundary

The compiler should be the first reviewer of boundary mistakes wherever
possible.

## Review Against Current Code

### What already matches the direction

- `ICompletionService` is the correct architectural seam between Core and
  provider SDKs.
- provider-specific replay data already has an explicit escape hatch through
  `ProviderReplay`.
- `ToolLoop` is already the single execution engine for call-model / dispatch
  / repeat behavior, which gives this migration one natural choke point.

### What is still transitional

- `ToolCallDeltaReceivedEvent.Input` is intentionally raw `JsonElement` because it
  is a transport-facing fact emitted at the provider boundary
- `ProviderReplay` still carries adapter-owned replay data through Core as a
  narrowly scoped escape hatch
- some flexible handlers still walk `ToolInput` JSON manually for dynamic object
  payloads
- some structured model outputs still rely on best-effort JSON extraction

Those are transitional seams, not the desired steady state.

## First Implementation Slices

These are the intended next moves after this note.

### Slice 1: typed tool-argument boundary in ToolLoop

Task 418 should:

- introduce a QuillForge-owned typed tool-argument path or validated envelope
- validate tool payloads once before dispatch
- move `IToolHandler` implementations off raw `JsonElement` bodies

This is the highest-value slice because it removes the easiest JSON shortcut in
Core.

### Slice 2: clarify transport events versus app-facing events

Task 419 should:

- separate provider transport deltas from QuillForge-accepted tool invocation
  facts where that distinction is currently blurred
- ensure streaming events communicate whether the app is reporting raw provider
  output, validated tool calls, or diagnostics

### Slice 3: apply command/event naming to one LLM-facing service slice

Task 420 should:

- pick one concrete service path
- name incoming requests as commands and resulting facts as events
- use it as the reference implementation for future LLM-layer cleanup

### Slice 4: audit remaining transport leakage after the typed boundary lands

Task 421 should:

- audit provider replay escape-hatch usage
- audit agents that inspect tool payload JSON directly
- audit any remaining Core-facing `JsonElement` seams that survived for valid
  reasons versus accidental ones

## Non-Goals

This note does not require:

- forcing all providers through `Microsoft.Extensions.AI`
- eliminating provider-specific adapters
- removing JSON Schema from tool declaration metadata
- inventing a command bus or event bus before the product needs one
- rewriting every model-authored structured response flow in the same change

## House Style Summary

- raw provider JSON lives at the provider edge and nowhere deeper by default
- opaque replay payloads may pass through Core only as adapter-owned baggage
- tool arguments translate once at ToolLoop before handler business logic
- agent and domain code should consume QuillForge-owned typed models, not reopen
  transport JSON
- malformed payloads fail early with logs, diagnostics, and explicit results
