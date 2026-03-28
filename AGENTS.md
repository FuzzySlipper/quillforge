# AGENTS.md (Project Local)

## Mission

QuillForge is an AI-powered creative writing system — a conversational interface backed by specialized AI agents that help authors build, explore, and write within richly detailed fictional worlds. It provides a Librarian for lore retrieval, a Prose Writer for scene generation, an Orchestrator for conversational routing, and an autonomous Forge pipeline for long-form story production.

This is a **ground-up rewrite in C#/.NET 10** of an existing Python/FastAPI application (see `../librarian-agent/` for the original). The rewrite prioritizes testability, clean architecture, robust session management, and simplified deployment.

**PRD:** `.taskmaster/docs/prd.md` — the full product requirements document with architecture details, data models, API contracts, and testing strategy.

## Task Management

Tasks are tracked in `.taskmaster/tasks/tasks.json` via the TaskMaster MCP server.

**Available MCP tools:**

- `mcp__taskmaster-ai__get_tasks` — list all tasks and status
- `mcp__taskmaster-ai__get_task` — get details on a specific task
- `mcp__taskmaster-ai__next_task` — find the next unblocked task to work on
- `mcp__taskmaster-ai__set_task_status` — mark tasks done/in-progress/blocked etc.
- `mcp__taskmaster-ai__parse_prd` — generate tasks from a PRD document (use `append: true` to add without replacing)
- `mcp__taskmaster-ai__expand_all` / `mcp__taskmaster-ai__expand_task` — break tasks into subtasks
- `mcp__taskmaster-ai__update_subtask` — update subtask details
- `mcp__taskmaster-ai__analyze_project_complexity` / `mcp__taskmaster-ai__complexity_report`

**To add a task:** use `parse_prd` with `append: true` on a small markdown snippet, or edit `.taskmaster/tasks/tasks.json` directly (ensure unique ID, valid dependency IDs, valid status value).

## Hard Architectural Rules

These are non-negotiable. Do not suggest alternatives.

- **No static singletons.** No `ServiceLocator.Get<T>()`, no static mutable state. All dependencies pass through constructors.
- **No reflection for registration or discovery.** No `[AutoRegister]`, no assembly scanning. All service registration is explicit in the composition root (`Program.cs`).
- **No object pooling** unless profiling proves it's needed.
- **No LLM provider types outside QuillForge.Providers.** Provider-specific SDK types (Anthropic, OpenAI, etc.) must not leak beyond `QuillForge.Providers`. Core and other projects program against Core abstractions (`ICompletionService`, `CompletionRequest`, etc.). NuGet packages for general .NET abstractions (e.g., `Microsoft.Extensions.Logging.Abstractions`) are fine in Core.
- **`Microsoft.Extensions.AI` is a preferred adapter, not a mandatory transport layer.** Use it as the default path for well-behaved providers, shared middleware, and standard tool/chat flows, but do not contort the product around its abstractions when providers require nonstandard request fields, streaming formats, reasoning payloads, or other provider-specific behavior. In those cases, add a provider-specific adapter inside `QuillForge.Providers` behind `ICompletionService` rather than forcing all providers through `IChatClient`.
- **Effusive logging.** Log at every significant operation — agent calls, tool dispatch, pipeline stage transitions, session mutations, file I/O, provider calls. Use structured logging via `Microsoft.Extensions.Logging`. When in doubt, log it.
- **No lambdas or closures for tool handlers.** All tool handlers must be named types implementing `IToolHandler` so they are discoverable, testable, and self-documenting.
- **Dependency direction is fixed:** `QuillForge.Web → QuillForge.Providers → QuillForge.Core` and `QuillForge.Web → QuillForge.Storage → QuillForge.Core`. Core depends on nothing. No circular references. No project references Web.
- **DisableTransitiveProjectReferences** is enforced. Projects cannot access transitive dependencies of their references.
- **Messages use stable GUIDs, never array indices.** All API operations on messages reference GUIDs. The frontend sends GUIDs. No positional indexing of conversation history.
- **Atomic file writes.** All file mutations use write-to-temp-then-rename. Never write directly to the target path.
- **Services are stateless.** Take minimum necessary state as parameters. Conversation state lives in `ConversationTree`, not scattered across services.
- **Tool loop is written once.** The `ToolLoop` class in Core is the single implementation of the call-LLM-check-tools-dispatch-repeat pattern. No agent reimplements this loop.
- **No mocking libraries in tests.** Use simple hand-written implementations of interfaces (like `FakeCompletionService`, `InMemorySessionStore`). This keeps tests readable and avoids reflection magic.
- **Avoid clever linq usage.** Prefer lengthy clear verbose code over shorter more enigmatic linq.
- **Use the config file, not constants.** Sanity check the config file values, but prefer to keep everything exposed for users to edit. 

## Core Patterns

### Tool System

Tools are strongly-typed named classes, not JSON dictionaries:

```csharp
public class QueryLoreHandler : IToolHandler
{
    private readonly LibrarianAgent _librarian;

    public string Name => "query_lore";
    public ToolDefinition Definition => new("query_lore", "Query the lore corpus", ...);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext ctx, CancellationToken ct)
    {
        var query = input.GetProperty("query").GetString()!;
        var bundle = await _librarian.QueryAsync(query, ct);
        return ToolResult.Success(JsonSerializer.Serialize(bundle));
    }
}
```

### Tool Loop

Single reusable engine. All agents configure it, none reimplement it:

```csharp
var response = await _toolLoop.RunAsync(
    config: new AgentConfig(model, maxTokens, systemPrompt, maxToolRounds: 10),
    tools: [queryLoreHandler, rollDiceHandler],
    conversation: conversationState,
    ct: cancellationToken
);
```

### Mode System

Orchestrator behavior varies by mode. Each mode is a separate class implementing `IMode`:

```csharp
public interface IMode
{
    string Name { get; }
    string SystemPromptSection { get; }
    IReadOnlyList<IToolHandler> GetTools();
    Task OnResponseAsync(AgentResponse response, ConversationState state);
}
```

Modes: `GeneralMode`, `WriterMode`, `RoleplayMode`, `ForgeMode`, `CouncilMode`. Mode-specific state (e.g., WriterMode's pending content) lives in the mode instance, not the orchestrator.

### Conversation Tree (Session Model)

Conversations are trees, not flat lists:

```csharp
public record MessageNode
{
    public required Guid Id { get; init; }
    public required Guid? ParentId { get; init; }
    public required string Role { get; init; }
    public required MessageContent Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<Guid> ChildIds { get; init; } = [];
    public MessageMetadata? Metadata { get; init; }
}
```

`ConversationTree` encapsulates all mutation behind thread-safe methods. The node dictionary is not publicly exposed as mutable — use lookup methods or `IReadOnlyDictionary`. Mutable state (`ActiveLeafId`, `Name`) is only modified through locked methods.

- `ConversationTree.Append()` — add message as child of current leaf
- `ConversationTree.Fork()` — branch from any node
- `ConversationTree.Delete()` — remove node + orphaned descendants
- `ConversationTree.GetThread()` — walk leaf→root for the active linear thread
- All operations are thread-safe (internal locking)

### Discriminated Unions

Use abstract base class + sealed derived types for union-like types:

```csharp
public abstract class StreamEvent;
public sealed class TextDeltaEvent(string Text) : StreamEvent;
public sealed class ToolCallEvent(string ToolName, string ToolId, JsonElement Input) : StreamEvent;
public sealed class DoneEvent(string StopReason) : StreamEvent;
```

### Factory-Validated Types

Use static factory methods to prevent inconsistent state:

```csharp
public record ToolResult
{
    public bool Success { get; }
    public string Content { get; }
    public string? Error { get; }

    private ToolResult(...) { ... }
    public static ToolResult Ok(string content) => ...;
    public static ToolResult Fail(string error) => ...;
}
```

### Forge Pipeline

State machine with discrete stages:

```csharp
public interface IPipelineStage
{
    string StageName { get; }
    IAsyncEnumerable<ForgeEvent> ExecuteAsync(ForgeContext context, CancellationToken ct);
}
```

Stages: Planning → Design → Writing → Review → Assembly. Each stage is independently testable.

## Build & Test

```bash
dotnet restore QuillForge.slnx
dotnet build QuillForge.slnx
dotnet test QuillForge.slnx    # 114 tests as of Task 7 completion
```

**Note:** The Web project and Architecture.Tests require `AllowMissingPrunePackageData=true` due to a .NET 10 preview SDK issue with the ASP.NET Core framework reference.

### Publishing

```bash
# Self-contained single-file binary per platform
dotnet publish src/QuillForge.Web -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Reference: Original Python Project

The original Python implementation is at `../librarian-agent/`. Key files for understanding existing behavior:

- `src/agents/orchestrator.py` — Orchestrator with mode switching, tool handling (~1600 LOC)
- `src/agents/librarian.py` — Lore retrieval agent
- `src/agents/prose_writer.py` — Scene generation with auto-lore-lookup
- `src/services/forge.py` — Autonomous story generation pipeline
- `src/llm.py` — Unified LLM client interface (what we're replacing with `ICompletionService`)
- `src/llm_anthropic.py` / `src/llm_openai.py` — Provider implementations
- `src/providers.py` — Provider registry with encrypted key storage
- `src/web/server.py` — FastAPI endpoints (what becomes `QuillForge.Web`)
- `src/models.py` — Shared data models
- `dev/frontend/` — React/TypeScript frontend (carried over unchanged)

## Content Directory Structure

The `build/` directory (user content, gitignored) has this structure. Preserve it for backwards compatibility:

```
build/
├── config.yaml              (app configuration)
├── .env                      (API keys — never commit)
├── lore/                     (world-building markdown, organized by lore set)
├── persona/                  (character/persona definitions)
├── writing-styles/           (prose style guides)
├── story/                    (append-only chapter files)
├── writing/                  (workspace drafts)
├── chats/                    (roleplay session logs)
├── forge/                    (forge project directories)
├── forge-prompts/            (customizable forge stage prompts)
├── council/                  (advisor persona prompts)
├── layouts/                  (UI layout markdown files)
├── character-cards/          (character card data)
├── backgrounds/              (UI background images)
├── generated-images/         (AI-generated images)
└── data/
    ├── providers.json        (encrypted API keys)
    ├── sessions/             (conversation history JSON)
    └── llm-debug/            (debug logs for LLM calls)
```
