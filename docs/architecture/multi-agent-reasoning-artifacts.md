# Architecture Decision: Multi-Agent Reasoning Artifacts for Interactive Turns

## Status

Accepted for implementation under tasks `636`-`640`.

## Purpose

This note defines the target shape for reasoning display in interactive chat.

QuillForge currently preserves only a single flattened `reasoning` string per
assistant message. That is enough to restore one toggle in the UI, but it is
not enough to explain which agent produced that reasoning during a multi-agent
turn.

This is especially weak in Writer and Roleplay flows, where users often care
more about `prose-writer` reasoning than the outer `orchestrator` or
`narrative-director` layer.

The goal of this decision is to make reasoning:

- agent-attributed
- persistable across session restore
- available to chat/debug UI
- separate from provider replay and normal transcript context

## Current Gaps

The current architecture has three specific limitations.

### 1. One message, one flattened reasoning string

`MessageMetadata` currently stores a single `Reasoning` string in
[`MessageNode.cs`](../../src/QuillForge.Core/Models/MessageNode.cs).

That means:

- one top-level message can only expose one reasoning payload
- nested agent reasoning is flattened or lost
- old and new reasoning sources are forced through the same field

### 2. Nested agent calls do not have a durable display boundary

Nested interactive flows already exist:

- `orchestrator`
- `narrative-director`
- `prose-writer`
- `assistant` in council/research style flows

Those agents already have stable runtime names through `AgentConfig.AgentName`
in [`AgentTypes.cs`](../../src/QuillForge.Core/Models/AgentTypes.cs), but their
reasoning is not preserved as distinct message artifacts.

### 3. Provider replay and UI reasoning are easy to conflate

Top-level reasoning-capable providers already rely on typed replay envelopes in
[`ProviderReplayEnvelope.cs`](../../src/QuillForge.Core/Models/ProviderReplayEnvelope.cs)
and [`ReasoningCompletionService.cs`](../../src/QuillForge.Providers/Adapters/ReasoningCompletionService.cs).

That replay is transport baggage for same-provider continuation. It is not the
same thing as user-visible reasoning diagnostics.

## Decisions

### 1. Reasoning Becomes a Per-Message Artifact Collection

Assistant messages may carry multiple reasoning artifacts.

Each artifact represents one completed agent contribution to the final
assistant-visible turn.

Examples:

- `orchestrator`
- `narrative-director`
- `prose-writer`
- `assistant`
- future Forge-specific interactive agents if they become user-visible

The persisted message model should support multiple artifacts on one assistant
message instead of forcing them into one string.

Recommended Core shape:

```csharp
public sealed record ReasoningArtifact
{
    public required string AgentId { get; init; }
    public required string AgentLabel { get; init; }
    public required string Content { get; init; }
    public int Sequence { get; init; }
}
```

Recommended `MessageMetadata` direction:

```csharp
public sealed record MessageMetadata
{
    public string? Reasoning { get; init; } // compatibility alias
    public IReadOnlyList<ReasoningArtifact> ReasoningArtifacts { get; init; } = [];
    public ProviderReplayEnvelope? ProviderReplay { get; init; }
}
```

Rules:

- `ReasoningArtifacts` is the source of truth for new code
- `Reasoning` remains during migration as a compatibility/default-display alias
- artifact order is preserved by `Sequence`
- duplicate `AgentId` values are allowed when the same agent contributes more
  than once in one visible turn

### 2. Stable Agent Identity Reuses `AgentConfig.AgentName`

Do not invent a second agent naming system for reasoning.

The stable `AgentId` for reasoning artifacts should reuse the existing
`AgentConfig.AgentName` values already used for token attribution.

Examples in current code:

- `orchestrator`
- `narrative-director`
- `prose-writer`
- `research`
- `forge-writer`

This avoids drift between usage tracking, diagnostics, and reasoning display.

`AgentLabel` should be a user-facing display label derived from that stable id.
The label may be stored with the artifact for simplicity and snapshot stability.

Recommended first-pass label mapping:

- `orchestrator` -> `Orchestrator`
- `narrative-director` -> `Narrative Director`
- `prose-writer` -> `Prose Writer`
- `assistant` -> `Assistant`
- `research` -> `Research`

### 3. Compatibility Keeps One Default-Display Reasoning String

The existing single-string `Reasoning` field should remain temporarily, but it
becomes a compatibility alias rather than the source of truth.

Its value should be the text of the artifact chosen as the default display
artifact for that message.

This allows older contract consumers to continue showing one reasoning block
without silently discarding the richer underlying structure.

Recommended default-selection policy:

1. If a `prose-writer` artifact exists, use that.
2. Else if an `assistant` artifact exists, use that.
3. Else use the last artifact by `Sequence`.

That policy intentionally favors the artifact users are most likely to care
about in Writer and Roleplay turns.

### 4. Reasoning Artifacts Are Display/Diagnostic Data, Not Replay Data

Reasoning artifacts must remain separate from provider replay.

Boundary:

- `ReasoningArtifacts` are QuillForge-owned display/diagnostic models
- `ProviderReplayEnvelope` remains adapter-owned transport baggage

Rules:

- nested agent reasoning artifacts are not resent as ordinary transcript text
- nested agent reasoning artifacts are not stored inside `ProviderReplay`
- only the top-level assistant message continues to carry provider replay for
  provider-specific continuation semantics

This follows the transport boundary rules already established in
[`llm-transport-boundary.md`](./llm-transport-boundary.md).

### 5. Collection Uses Explicit Session-Scoped Capture, Not Tool Payload Tricks

Do not try to recover nested agent reasoning from:

- tool result strings
- tool result metadata hidden inside visible content
- provider replay envelopes for nested agents

The recommended capture boundary is an explicit reasoning artifact sink on
`AgentContext` or an equivalent small session-scoped capture contract passed
through agent-owned execution paths.

Recommended direction:

```csharp
public sealed record AgentContext
{
    public Func<ReasoningArtifact, CancellationToken, Task>? OnReasoningArtifact { get; init; }
}
```

Rules:

- the sink is optional outside interactive flows
- the sink is session-scoped and explicit, not global mutable state
- completed agent calls publish one or more artifacts through that sink
- the sink is not part of `ToolResult`

This stays aligned with QuillForge's explicit session-context rules and avoids
inventing a second hidden ambient mechanism beyond the existing token tracking
scope.

### 6. Session, Chat, and Debug Contracts Must Expose the Collection

The richer artifact model must flow through:

- chat completion `done` payloads
- session load/history restore
- debug bridge/session inspection
- frontend message types and variant restore

Contract direction:

- add a structured reasoning artifact array to chat/session/debug DTOs
- keep the existing single `reasoning` string temporarily for compatibility
- restore the array from persisted session data instead of reconstructing it
  from the alias string

The compatibility rule is:

- old clients may still read `reasoning`
- new clients must prefer `reasoningArtifacts`

### 7. UI Keeps One Disclosure Area with Internal Selection

The UI should not render one separate toggle per agent. That would get crowded
fast.

Instead:

- keep one reasoning disclosure area per assistant bubble
- inside it, show a compact selector when multiple artifacts exist
- default the visible selection using the same policy as the compatibility alias
- allow switching between agent artifacts without changing the outer bubble
  layout

The same model should be used in:

- message bubbles
- session restore / variant browsing
- context/debug overlays

### 8. Streaming Rolls Out in Two Phases

Phase 1:

- capture nested reasoning artifacts
- persist them
- restore them
- expose them in final `done` payloads and session/debug APIs

Phase 2:

- decide whether nested reasoning should also stream live with agent
  attribution

This split is intentional. The first phase provides immediate diagnostic value
without forcing a full nested streaming redesign inside the top-level chat SSE
pipeline.

Current compatibility path:

- the existing top-level `reasoning_delta` SSE event may remain for the outer
  assistant stream
- nested artifacts can first arrive in the final completion payload and restored
  session data

If QuillForge later adds live nested reasoning streaming, it should use an
agent-attributed event shape rather than more anonymous flattened deltas.

## Explicit Non-Goals

This decision does not mean:

- reasoning becomes part of normal transcript context
- provider replay is generalized to nested agent calls
- every nested LLM call must stream live into the chat bubble immediately
- UI should expose raw provider transport details directly

## Implementation Guidance

Tasks should proceed in this order:

1. Define the typed artifact model and compatibility alias behavior.
2. Add artifact persistence to message metadata and session storage.
3. Add explicit nested-agent capture in interactive flows.
4. Extend chat/session/debug contracts.
5. Add compact UI selection and default-display behavior.
6. Add regression coverage for nested capture, restore, and compatibility.

## Result

After this work:

- one assistant message can show multiple agent-attributed reasoning artifacts
- Writer and Roleplay can prefer `prose-writer` reasoning for display
- debug and harness work can reason about which agent produced what
- provider replay remains cleanly separate from display diagnostics
