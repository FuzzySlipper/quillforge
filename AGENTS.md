# AGENTS.md (Project Local)

## Mission

QuillForge is an AI-powered creative writing system — a conversational interface backed by specialized AI agents that help authors build, explore, and write within richly detailed fictional worlds. It provides a Librarian for lore retrieval, a Prose Writer for scene generation, an Orchestrator for conversational routing, and an autonomous Forge pipeline for long-form story production.

This is a **ground-up rewrite in C#/.NET 10** of an existing Python/FastAPI application (see `../librarian-agent/` for the original). The rewrite prioritizes testability, clean architecture, robust session management, and simplified deployment.

**PRD:** `docs/prd.md` — the full product requirements document with architecture details, data models, API contracts, and testing strategy.

## Task Management

Tasks are tracked via the Den MCP server (project ID: `quillforge`).

**Available MCP tools:**

- `mcp__den__list_tasks` — list all tasks and status
- `mcp__den__get_task` — get full task details including dependencies and subtasks
- `mcp__den__next_task` — find the next unblocked task to work on
- `mcp__den__create_task` — create a new task or subtask
- `mcp__den__update_task` — update task fields and status (requires agent identity)
- `mcp__den__add_dependency` / `mcp__den__remove_dependency` — manage task dependencies
- `mcp__den__send_message` — send a message on a task or thread
- `mcp__den__store_document` / `mcp__den__search_documents` — store and search project documents

## Hard Architectural Rules

These are non-negotiable. Do not suggest alternatives.

- **No static singletons.** No `ServiceLocator.Get<T>()`, no static mutable state. All dependencies pass through constructors.
- **No reflection for registration or discovery.** No `[AutoRegister]`, no assembly scanning. All service registration is explicit in the composition root (`Program.cs`).
- **No object pooling** unless profiling proves it's needed.
- **No LLM provider types outside QuillForge.Providers.** Provider-specific SDK types (Anthropic, OpenAI, etc.) must not leak beyond `QuillForge.Providers`. Core and other projects program against Core abstractions (`ICompletionService`, `CompletionRequest`, etc.). NuGet packages for general .NET abstractions (e.g., `Microsoft.Extensions.Logging.Abstractions`) are fine in Core.
- **`Microsoft.Extensions.AI` is a preferred adapter, not a mandatory transport layer.** Use it as the default path for well-behaved providers, shared middleware, and standard tool/chat flows, but do not contort the product around its abstractions when providers require nonstandard request fields, streaming formats, reasoning payloads, or other provider-specific behavior. In those cases, add a provider-specific adapter inside `QuillForge.Providers` behind `ICompletionService` rather than forcing all providers through `IChatClient`.
- **Effusive logging.** Log at every significant operation — agent calls, tool dispatch, pipeline stage transitions, session mutations, file I/O, provider calls. Use structured logging via `Microsoft.Extensions.Logging`. When in doubt, log it.
- **No lambdas or closures for tool handlers.** All tool handlers must be named types implementing `IToolHandler` so they are discoverable, testable, and self-documenting.
- **Dependency direction is fixed:** `QuillForge.Web → QuillForge.Providers → QuillForge.Core` and `QuillForge.Web → QuillForge.Storage → Den.Persistence`. `QuillForge.Storage → QuillForge.Core`. Core depends on nothing. `Den.Persistence` depends on nothing in QuillForge. No circular references. No project references Web.
- **DisableTransitiveProjectReferences** is enforced. Projects cannot access transitive dependencies of their references.
- **Messages use stable GUIDs, never array indices.** All API operations on messages reference GUIDs. The frontend sends GUIDs. No positional indexing of conversation history.
- **Atomic file writes.** All file mutations use write-to-temp-then-rename. Never write directly to the target path.
- **Persisted config/state changes go through `Den.Persistence` stores.** AppConfig and RuntimeState use `IAppConfigStore` / `RuntimeStateStore`, which wrap `Den.Persistence` persisted-document stores. Do not manually serialize + write config files from endpoints. Use `SaveAsync` or `UpdateAsync` on the store. See "Persisted Document Boundary" below.
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

### Session Context

Session-scoped state flows explicitly through parameters, not middleware or ambient context:

- **GET endpoints** extract an optional session ID from the query string via `httpContext.TryGetSessionId()` (extension in `SessionIdExtensions.cs`). When absent, `null` falls back to default/global state.
- **POST endpoints** extract session ID from the deserialized request body. Do not unify GET and POST extraction into a single helper — the data sources are intentionally different.
- **`ISessionRuntimeStore.LoadAsync(sessionId)`** is the single source of truth for session-scoped runtime state. Avoid calling `LoadAsync(null)` unless you explicitly need the default/global state.
- **Tool handlers** use `AgentContext.SessionId` at call time to resolve session-scoped resources. Never capture session state at handler construction time — handlers are singletons.
- **`AgentContext`** carries the session ID and resolved profile settings (lore set, writing style) through the orchestrator → tool loop → handler chain. Build it per-request in the endpoint.

### Web Contracts

Endpoint request/response shapes use named `sealed record` types in `QuillForge.Web/Contracts/`, split by domain (`ChatContracts.cs`, `SessionContracts.cs`, etc.). Do not introduce new anonymous objects for high-traffic endpoints — use or extend the existing contract types. Frontend TypeScript types in `types.ts` and `api.ts` must match the backend DTOs.

### Interpretation Probe

The `/probe` command runs a diagnostic that tests how the current model interprets mode instructions and tool boundaries. Key rules:

- Probes test **applied interpretation under ambiguity**, not instruction parroting. The prompt battery presents realistic but ambiguous user requests.
- Probe mode **disables tool execution** (`Tools: null`) while exposing tool definitions in the prompt text so the model can reason about them.
- Results persist as timestamped markdown under `build/data/llm-debug/` for cross-model comparison.
- The scenario battery is versioned (`ProbeBattery.Version`) so reports from different versions are not conflated.
- When adding new scenarios, keep prompts stable enough for cross-run comparison and ensure each scenario tests a distinct interpretation boundary.

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

## Synthetic User Build Test

When the user asks to "perform a synthetic user build test", "run a manual build test", or similar, do not stop at unit/integration tests. Treat it as a live-build exercise using the Development-only debug bridge plus normal UI/manual verification.

### Goal

Simulate a careful human tester using the running app end-to-end, with emphasis on frontend/backend contract mismatches, stale runtime state, streaming/tool-loop failures, and features that only break in a live build.

### Required Approach

1. Build and run the app in Development.
2. Use the debug bridge endpoints for deterministic backend/manual probing:
   - `POST /api/debug/bridge/session/reset`
   - `POST /api/debug/bridge/mode`
   - `POST /api/debug/bridge/chat`
   - `GET /api/debug/bridge/session/{id}`
   - `GET /api/debug/bridge/state`
3. Also exercise the real web UI and normal endpoints when the bug could be in the frontend contract, SSE handling, or browser behavior. Do not rely only on the debug bridge.
4. Prefer short realistic user messages and commands over synthetic no-op prompts. Make the app actually use tools, mode state, sessions, and profile selection.
5. For every failure, capture:
   - exact action taken
   - endpoint or UI surface used
   - expected behavior
   - actual behavior
   - the smallest code-path evidence you can find afterward
6. If the repo uses Den and the user is asking for bug discovery rather than an immediate fix, add or update concrete tasks instead of leaving only prose notes.

### Minimum Coverage Checklist

A synthetic user build test should cover all of these unless the user explicitly narrows scope:

- App boots successfully and `/api/status` reports sane values.
- Normal chat streaming via `/api/chat/stream` produces visible assistant output.
- A prompt that should invoke `query_lore` actually routes through the orchestrator and returns lore-backed content.
- Session continuity works across multiple turns in the same session.
- Session reload shows persisted assistant turns, not just user turns.
- Profile switching changes active persona, lore set, and writing style in runtime behavior, not just in saved config.
- General mode works.
- Writer mode works, including pending-content accept/reject/regenerate behavior if applicable.
- Roleplay mode works, including character selection if applicable.
- Council mode works.
- Artifact generation works if available.
- Forge endpoints at least smoke-test successfully if they are part of the current build.
- Diagnostics panel/debug output surfaces tool calls, warnings, and empty-response cases instead of failing silently.
- At least one message deletion/fork/regenerate flow is exercised when the UI exposes it.
- At least one content-browsing/editing flow is exercised for persona, lore, writing style, or related files.

### Required Chat Prompts

During the run, include prompts that are likely to force real behavior instead of shallow happy-path text:

- A lore question that should obviously trigger the librarian.
- A writer-mode scene request that should use lore and writing-style context.
- A roleplay prompt that depends on the selected character/profile.
- A council-style question that should produce multi-member output.
- A prompt that creates or mutates session state, then a follow-up that verifies the state was preserved.

### Default Deliverable

When asked for a synthetic user build test, the expected output is:

1. Findings first, ordered by severity.
2. Clear reproduction steps for each confirmed bug.
3. File/endpoint evidence for the mismatch.
4. Den tasks added or updated, if bug logging was requested.
5. A short coverage note listing what was exercised and what could not be tested.

### Reusable Prompt

Use this as the default internal prompt/checklist for future agents:

```text
Perform a synthetic user build test of QuillForge against a live Development build.

Do not stop at dotnet test. Start the app, use the debug bridge endpoints for deterministic probing, and also use the real UI/endpoints for any feature where frontend/backend contract mismatches, SSE parsing, session persistence, or browser behavior could hide bugs.

Cover at minimum:
- status/bootstrap
- normal chat streaming
- lore lookup through the orchestrator
- session continuity and reload
- profile switching (persona/lore/writing style)
- general mode
- writer mode
- roleplay mode
- council mode
- artifact generation if available
- forge smoke paths if available
- diagnostics visibility
- one message delete/fork/regenerate flow
- one content browsing/editing flow

Use realistic prompts that force tool use and stateful behavior, not just “say hello”.
For every failure, capture the exact action, expected vs actual behavior, and then trace it back to the smallest relevant code path.
If the request is bug-hunting rather than immediate implementation, add/update Den tasks with concrete acceptance tests.
Return findings first, then coverage notes, then any task IDs created.
```

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
├── story/                    (append-only chapter files + .state.yaml per project)
├── writing/                  (workspace drafts)
├── chats/                    (roleplay session logs)
├── forge/                    (forge project directories)
├── forge-prompts/            (customizable forge stage prompts)
├── council/                  (advisor persona prompts)
├── layouts/                  (UI layout markdown files)
├── character-cards/          (character card data + portraits)
├── backgrounds/              (UI background images)
├── generated-images/         (AI-generated images)
├── generated-audio/          (TTS-generated audio files)
├── artifacts/                (generated in-world artifacts)
├── research/                 (research agent output, organized by project)
└── data/
    ├── config.yaml           (app configuration)
    ├── providers.json        (encrypted API keys)
    ├── runtime-state.json    (legacy runtime state, migrated to session-state on startup)
    ├── sessions/             (conversation history JSON)
    ├── session-state/        (per-session runtime state JSON)
    └── llm-debug/            (debug logs + probe reports)
```

### Persisted Document Boundary (`Den.Persistence`)

`src/Den.Persistence/` is a product-neutral persistence module that provides typed, file-backed document stores with a centralized load/default/normalize/validate/save lifecycle. It uses the `Den.*` namespace for future cross-project extraction via git submodule.

**When to use it:** Any time you need to persist config or runtime state to a file. If the data is a single document at a known path (not a keyed collection), it belongs behind `IPersistedDocumentStore<T>`.

**What's migrated:**
- `AppConfig` → `AppConfigStore` (`IAppConfigStore`) wrapping `YamlPersistedDocumentStore<AppConfig>`
- `RuntimeState` → `RuntimeStateStore` wrapping `JsonPersistedDocumentStore<RuntimeState>`

**What's not migrated (by design):**
- `SessionRuntimeState` — keyed collection (one file per session) with inheritance semantics; doesn't fit single-document model
- `ProviderConfig` — encryption layer between on-disk and in-memory formats; stored type differs from loaded type

**Adding a new persisted field:**
1. Add the field to the model type (e.g., `AppConfig`)
2. If it needs normalization (clamping, defaulting), add it to the document's `Normalize` override (e.g., `AppConfigDocument`)
3. Done. The store handles load, save, and atomic write. No separate wiring needed.

**Adding a new persisted document:**
1. Define the model type
2. Create a class extending `PersistedDocumentBase<T>` — implement `RelativePath` and `CreateDefault`, optionally override `Normalize` and `ThrowIfInvalid`
3. Create a store using `JsonPersistedDocumentStore<T>` or `YamlPersistedDocumentStore<T>`
4. Register in DI

**Rules:**
- Do not add QuillForge-specific domain types to `Den.Persistence`. It must stay product-neutral.
- Dependency direction: `QuillForge.Storage → Den.Persistence`. Den.Persistence depends on nothing in QuillForge.
- All persisted-document stores include `SemaphoreSlim` locking — thread safety is not optional.

### Content Path Management

**All content directory names are defined in `QuillForge.Core.ContentPaths`.** This is the single source of truth — never use bare string literals for content paths.

Rules:
- **Use `ContentPaths.*` constants** for all `Path.Combine(contentRoot, ...)` calls and `IContentFileService.ListAsync(...)` relative paths.
- **New directories** must be added to both `ContentPaths` (the constant and `AllDirectories` array) so `FirstRunSetup` creates them on first run.
- **Store abstractions** (`ILoreStore`, `IPersonaStore`, etc.) should own path construction for their domain. Endpoints should prefer calling store methods over constructing paths directly.
- **`config.yaml` path** is always `Path.Combine(contentRoot, ContentPaths.ConfigFile)` — never hardcoded.
