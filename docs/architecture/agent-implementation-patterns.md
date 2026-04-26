# Agent Implementation Patterns

This note collects the code-shape references that used to sit inline in `AGENTS.md`. Keep `AGENTS.md` operational; keep the concrete code patterns here.

## Tool Handlers

Tool handlers are named types implementing `IToolHandler`. Do not use lambdas, closures, or ad hoc endpoint-local tool logic.

Primary references:
- `src/QuillForge.Core/Services/IToolHandler.cs`
- `src/QuillForge.Core/Services/TypedToolHandler.cs`
- `src/QuillForge.Core/Agents/Tools/QueryLoreHandler.cs`
- `src/QuillForge.Core/Agents/Tools/QueryContextHandler.cs`
- `src/QuillForge.Core/Agents/Tools/SaveLoreFileHandler.cs`

Current preferred pattern:
- define a named handler type
- expose a stable `Name`
- expose a `ToolDefinition`
- prefer typed argument parsing at the boundary
- return `ToolResult.Ok(...)` or `ToolResult.Fail(...)`

```csharp
public sealed class QueryLoreHandler : TypedToolHandler<QueryLoreArgs>
{
    public override string Name => "query_lore";
    public override ToolDefinition Definition => QueryLoreTool.Definition;

    protected override async Task<ToolResult> HandleTypedAsync(
        QueryLoreArgs input,
        AgentContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Query))
        {
            return ToolResult.Fail("Query is required.");
        }

        var result = await _librarian.QueryAsync(input.Query, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(result.Bundle));
    }
}
```

## Tool Loop

`ToolLoop` is the single implementation of the call-model / inspect-tool-calls / dispatch / continue pattern. Agents configure it; they do not reimplement it.

Primary references:
- `src/QuillForge.Core/Agents/ToolLoop.cs`
- `src/QuillForge.Core/Agents/OrchestratorAgent.cs`
- `src/QuillForge.Core/Agents/LibrarianAgent.cs`

Common shape:

```csharp
var response = await _toolLoop.RunAsync(
    config,
    tools,
    conversation,
    context,
    ct);
```

If a new flow needs tool-using LLM behavior, extend `ToolLoop` or the surrounding typed boundaries instead of inventing a second loop.

## Modes

Modes are named classes implementing `IMode`. They contribute prompt shape, tool filtering, and response post-processing without owning session state themselves.

Primary references:
- `src/QuillForge.Core/Agents/Modes/IMode.cs`
- `src/QuillForge.Core/Agents/Modes/GuideMode.cs`
- `src/QuillForge.Core/Agents/Modes/WriterMode.cs`
- `src/QuillForge.Core/Agents/Modes/RoleplayMode.cs`
- `src/QuillForge.Core/Agents/Modes/LoreBuilderMode.cs`
- `src/QuillForge.Core/Agents/Modes/ForgeMode.cs`
- `src/QuillForge.Core/Agents/Modes/CouncilMode.cs`
- `src/QuillForge.Core/Agents/Modes/ResearchMode.cs`

Mode instances should stay lightweight:
- prompt-building belongs in the mode
- state ownership belongs in `SessionState` and session-owned services
- mode post-processing should coordinate through services rather than capture ambient mutable state

## Conversation Tree

`ConversationTree` is the persisted branching conversation artifact. It is not a generic bag of session state.

Primary references:
- `src/QuillForge.Core/Models/ConversationTree.cs`
- `docs/architecture/profile-session-conversation-ownership.md`

Key operations:
- `Append(...)` adds a new child message and advances the active leaf
- `CreateVariant(...)` creates a sibling variant for regenerate flows
- `GetThread(...)` walks root to leaf for a linear branch view
- `ToFlatThread()` returns the active thread without the synthetic root
- `Delete(...)` removes a node and orphaned descendants

Messages are identified by stable GUIDs. UI and API flows should reference message IDs, not positional indices.

## Discriminated-Union Style Models

QuillForge uses abstract base classes plus sealed derived types for union-like model families.

Primary references:
- `src/QuillForge.Core/Models/StreamEvent.cs`
- `docs/architecture/llm-transport-boundary.md`

Example:

```csharp
public abstract class StreamEvent;
public abstract class TransportStreamEvent : StreamEvent;
public abstract class AppStreamEvent : StreamEvent;
public sealed class TextDeltaEvent(string text) : TransportStreamEvent;
public sealed class ToolCallValidatedEvent(string toolName, string toolId, ToolInput input) : AppStreamEvent;
```

Use this pattern when the family is closed and the type distinction carries domain meaning.

## Factory-Validated Results

Use factory methods when a type has a small set of valid states and direct construction would allow inconsistent combinations.

Primary reference:
- `src/QuillForge.Core/Models/ToolResult.cs`

Example:

```csharp
public sealed record ToolResult
{
    private ToolResult(bool success, string content, string? error) { ... }
    public static ToolResult Ok(string content) => new(true, content, null);
    public static ToolResult Fail(string error) => new(false, string.Empty, error);
}
```

## Forge Pipeline

Forge runs as a stage-based pipeline. Each stage is independently testable and reports progress through Forge events.

Primary references:
- `src/QuillForge.Core/Pipeline/IPipelineStage.cs`
- `src/QuillForge.Core/Pipeline/ForgePipeline.cs`
- `src/QuillForge.Core/Pipeline/PlanningStage.cs`
- `src/QuillForge.Core/Pipeline/DesignStage.cs`
- `src/QuillForge.Core/Pipeline/WritingStage.cs`
- `src/QuillForge.Core/Pipeline/ReviewStage.cs`
- `src/QuillForge.Core/Pipeline/AssemblyStage.cs`

Stages currently run in this order:
1. Planning
2. Design
3. Writing
4. Review
5. Assembly

## Provider Boundary And `Microsoft.Extensions.AI`

Provider-specific SDK types stay inside `QuillForge.Providers`. Other layers program against QuillForge-owned abstractions such as `ICompletionService`, `CompletionRequest`, and `StreamEvent`.

Primary references:
- `src/QuillForge.Core/Services/ICompletionService.cs`
- `src/QuillForge.Providers/Adapters/ChatClientCompletionService.cs`
- `src/QuillForge.Providers/Adapters/ReasoningCompletionService.cs`
- `docs/architecture/llm-transport-boundary.md`

`Microsoft.Extensions.AI` is a preferred adapter where it fits cleanly. It is not a requirement that every provider flow be forced through `IChatClient` if the provider needs nonstandard request or replay behavior.
