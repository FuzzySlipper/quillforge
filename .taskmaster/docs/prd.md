# QuillForge - Product Requirements Document

## 1. Overview

QuillForge is an AI-powered creative writing system that helps authors build, explore, and write within richly detailed fictional worlds. It provides a conversational interface backed by specialized AI agents — a Librarian for lore retrieval, a Prose Writer for scene generation, and an autonomous Forge pipeline for long-form story production.

This is a **ground-up rewrite in C#/.NET 10** of an existing Python/FastAPI application. The rewrite prioritizes testability, clean architecture, robust session management, and simplified deployment (single self-contained binary per platform).

### Target Users

Non-technical creative writers who run the application locally. Users interact through a React web frontend served by the application. They customize the UI via layout markdown files and configure lore, personas, and writing styles through the file system.

### Key Goals

1. **Testability first**: Unit tests must catch bugs before they surface during long creative sessions or multi-hour forge pipeline runs. Core business logic has zero dependency on LLM SDKs, HTTP frameworks, or file I/O.
2. **Robust session management**: Replace the current fragile index-based conversation history with a tree-structured model supporting stable message IDs, safe forking, deletion, and concurrent access.
3. **Clean LLM abstraction**: LLM provider details (Anthropic, OpenAI, etc.) must not leak beyond the provider layer. Agents program against a unified interface.
4. **Single-binary deployment**: Ship self-contained executables per platform (Windows, macOS ARM/x64, Linux). No runtime dependencies for end users. Auto-update from GitHub Releases.
5. **Preserve the frontend**: The existing React/TypeScript frontend is carried over unchanged and served as static files.

## 2. Architecture

### 2.1 Solution Structure

```
QuillForge.sln
src/
  QuillForge.Core/          Zero external dependencies. All agents, models,
                            services interfaces, tool handlers, pipeline stages.
  QuillForge.Providers/     LLM SDK references (Anthropic, OpenAI). IChatClient
                            adapters. Provider registry. Only project that
                            touches LLM SDKs.
  QuillForge.Storage/       File system implementations of Core service interfaces.
                            Lore loading, story persistence, session storage,
                            encrypted key store, image/TTS integration.
  QuillForge.Web/           ASP.NET Core Minimal API host. Composition root (DI
                            wiring). Endpoints, SSE streaming, static file serving.

tests/
  QuillForge.Core.Tests/    Fast pure unit tests. No I/O, no LLM, no network.
  QuillForge.Providers.Tests/  Message format conversion, tool schema translation.
  QuillForge.Storage.Tests/ File I/O tests with temp directories.
  QuillForge.Architecture.Tests/  Dependency boundary enforcement.

docs/
  agent/                    ARCHITECTURE.md, BUILD_AND_TEST.md for coding agents.
  design/                   Per-feature design decision docs.
```

### 2.2 Dependency Direction

```
QuillForge.Web → QuillForge.Core
QuillForge.Web → QuillForge.Providers
QuillForge.Web → QuillForge.Storage
QuillForge.Providers → QuillForge.Core
QuillForge.Storage → QuillForge.Core
QuillForge.Core → (nothing — pure .NET BCL only)
```

`DisableTransitiveProjectReferences` enforced in `Directory.Build.props`.

### 2.3 Build Configuration

- **Target:** `net10.0`
- **Language:** C# latest, nullable enabled, implicit usings
- **Package management:** `Directory.Packages.props` for centralized NuGet versions
- **Code style:** `EnforceCodeStyleInBuild`, `AnalysisLevel` latest
- **Test framework:** xUnit, no mocking library (use simple test implementations of interfaces)

## 3. Core Domain (QuillForge.Core)

### 3.1 Agent Architecture

#### 3.1.1 Completion Service Interface

```csharp
// The single LLM abstraction. Lives in Core. No SDK types cross this boundary.
public interface ICompletionService
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default);
}
```

`CompletionRequest` and `CompletionResponse` are Core types (records) with no reference to Anthropic or OpenAI SDKs. The Providers project implements this interface using `Microsoft.Extensions.AI`'s `IChatClient` adapters.

#### 3.1.2 Tool System

Tools are strongly typed, not raw JSON dictionaries.

```csharp
public record ToolDefinition(string Name, string Description, JsonElement InputSchema);

public interface IToolHandler
{
    string Name { get; }
    ToolDefinition Definition { get; }
    Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default);
}

public record ToolResult(bool Success, string Content, string? Error = null);
```

Each tool handler is a named class (no lambdas) with explicit dependencies injected via constructor. This makes them individually unit-testable.

#### 3.1.3 Tool Loop

A single reusable engine that all agents share. Never reimplemented per agent.

```csharp
public class ToolLoop
{
    Task<AgentResponse> RunAsync(AgentConfig config, IReadOnlyList<IToolHandler> tools,
                                  ConversationState conversation, CancellationToken ct);
    IAsyncEnumerable<StreamEvent> RunStreamAsync(AgentConfig config, IReadOnlyList<IToolHandler> tools,
                                                  ConversationState conversation, CancellationToken ct);
}
```

The tool loop handles: calling the completion service, detecting tool_use stop reasons, dispatching to the correct handler, appending results to the conversation, respecting max tool rounds, and handling max_tokens / auto-continuation.

#### 3.1.4 Agents

Each agent is a configuration (system prompt builder + tool set + optional response post-processing) fed into the shared ToolLoop.

**Librarian**: Loads lore corpus into system prompt. Answers structured queries with provenance. Returns `LoreBundle`. Parsing logic (JSON extraction from LLM output) is a pure function, tested independently.

**ProseWriter**: Generates scenes. Has a `QueryLoreHandler` tool that delegates to the Librarian. Auto-continuation on max_tokens is handled by `ContinuationStrategy` (extracted, testable). Writing style loaded from file via `IWritingStyleStore`.

**Orchestrator**: The user's conversational partner. Delegates to sub-agents via tool handlers. Mode-specific behavior is handled by separate `IMode` implementations (not branches in a god class).

**ForgeWriter / ForgePlanner / ForgeReviewer**: Specialized agents for the forge pipeline stages. Each has focused responsibility and minimal tool sets.

**Delegate**: Multi-provider task delegation for parallel execution of factual queries.

#### 3.1.5 Mode System

```csharp
public interface IMode
{
    string Name { get; }
    string SystemPromptSection { get; }
    IReadOnlyList<IToolHandler> GetTools();
    Task OnResponseAsync(AgentResponse response, ConversationState state);
}
```

Implementations: `GeneralMode`, `WriterMode` (pending content accept/reject state machine), `RoleplayMode` (auto-append, regenerate/delete), `ForgeMode`, `CouncilMode`. Each is independently testable.

### 3.2 Session Model (Conversation Tree)

**This replaces the current fragile index-based flat list.**

#### 3.2.1 Data Model

```csharp
public record MessageNode
{
    public required Guid Id { get; init; }
    public required Guid? ParentId { get; init; }
    public required string Role { get; init; }       // "user" | "assistant"
    public required MessageContent Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public List<Guid> ChildIds { get; init; } = [];
    public MessageMetadata? Metadata { get; init; }   // token counts, model used, etc.
}

public class ConversationTree
{
    public Guid SessionId { get; }
    public string Name { get; set; }
    public Guid RootId { get; }                       // Synthetic root node
    public Guid ActiveLeafId { get; set; }            // Current position in the tree
    public Dictionary<Guid, MessageNode> Nodes { get; }

    // Operations
    public MessageNode Append(Guid parentId, string role, MessageContent content);
    public IReadOnlyList<MessageNode> GetThread(Guid leafId);    // Walk to root = active thread
    public ConversationTree Fork(Guid fromNodeId);               // New session branching from a node
    public void Delete(Guid nodeId);                             // Remove node + orphaned subtree
    public IReadOnlyList<MessageNode> ToFlatThread();            // Active thread as ordered list (for LLM context)
}
```

#### 3.2.2 Key Properties

- **Stable identity**: Every message has a GUID. API operations reference GUIDs, never array indices.
- **Tree structure**: Forking creates a new branch from any node, not a copy of the array. The original conversation is unmodified. Multiple branches can coexist within one session.
- **Active thread**: `ActiveLeafId` tracks which branch the user is currently on. `GetThread()` walks from leaf to root to produce the linear message sequence the LLM sees.
- **Safe deletion**: Deleting a node removes it and any orphaned descendants. Sibling branches are unaffected.
- **Variant support**: Regenerating a response creates a sibling node (same parent, different content). The UI can let users swipe between variants.
- **Concurrency**: `ConversationTree` operations are guarded by a lock. Atomic snapshots for persistence.

#### 3.2.3 Session Persistence

```csharp
public interface ISessionStore
{
    Task<ConversationTree> LoadAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveAsync(ConversationTree session, CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
}
```

File storage implementation uses atomic writes (write to temp file, then rename). Session ID is a GUID (no timestamp collisions). Auto-save after each turn completes.

### 3.3 Models

Immutable records shared across agents:

- `LoreBundle` — passages, source files, confidence
- `ProseResult` — generated text, lore queries made, word count
- `ReviewResult` — continuity/adherence/voice/quality scores, feedback, pass/fail
- `ForgeManifest` — project name, stage, chapter statuses, stats
- `ChapterStatus` — status enum, revision count, scores, feedback
- `CompletionRequest/Response` — model, max_tokens, system prompt, messages, tools
- `StreamEvent` — text_delta, tool_call, done (discriminated union)

### 3.4 Service Interfaces

All defined in Core, implemented in Storage:

- `ILoreStore` — load lore files from directory, search content
- `IStoryStore` — read/append story files, list projects
- `ISessionStore` — conversation tree persistence (see 3.2.3)
- `IWritingStyleStore` — load writing style templates
- `IPersonaStore` — load persona definitions
- `IImageGenerator` — generate image from prompt, return URL/path
- `ITtsGenerator` — generate speech from text
- `IContentFileService` — generic read/write/list/search within content directories

### 3.5 Forge Pipeline

The autonomous story generation pipeline, modeled as a state machine with discrete stages.

```csharp
public interface IPipelineStage
{
    string StageName { get; }
    IAsyncEnumerable<ForgeEvent> ExecuteAsync(ForgeContext context, CancellationToken ct);
}
```

**Stages:**
1. `PlanningStage` — ForgePlanner agent produces chapter briefs
2. `DesignStage` — Refines briefs with character arcs, plot threads
3. `WritingStage` — ForgeWriter agent drafts chapters (with Librarian tool for lore)
4. `ReviewStage` — ForgeReviewer agent scores chapters, requests revisions
5. `AssemblyStage` — Combines approved chapters into final output

**ForgePipeline** orchestrates stages, manages the manifest, emits progress events, handles pause points (e.g., pause after chapter 1 for user review). Each stage is independently testable with mocked agents.

### 3.6 Configuration

```csharp
public record AppConfig
{
    public required PathsConfig Paths { get; init; }
    public required ModelsConfig Models { get; init; }
    public required LibrarianConfig Librarian { get; init; }
    public required ProseWriterConfig ProseWriter { get; init; }
    public required ForgeConfig Forge { get; init; }
    // Active profile selections
    public string ActivePersona { get; set; }
    public string ActiveLoreSet { get; set; }
    public string ActiveWritingStyle { get; set; }
}
```

Loaded from YAML (`config.yaml` in the data directory). Validated at startup.

## 4. Providers (QuillForge.Providers)

### 4.1 LLM Adapters

Implements `ICompletionService` from Core using `Microsoft.Extensions.AI`'s `IChatClient`.

```csharp
public class ChatClientCompletionService : ICompletionService
{
    private readonly IChatClient _client;
    // Translates CompletionRequest → IChatClient calls → CompletionResponse
}
```

Provider-specific adapters:
- `AnthropicProvider` — handles prompt caching (cache_control), Anthropic message format
- `OpenAIProvider` — handles OpenAI function calling format, provider quirks (DeepSeek reasoning_content, empty required arrays, Ollama num_ctx)

### 4.2 Provider Registry

Manages multiple configured providers with encrypted API keys. Resolves aliases to configured `ICompletionService` instances. Same CRUD operations as current Python version.

### 4.3 Model Fetching

Fetch available models from provider APIs, cache results locally.

## 5. Storage (QuillForge.Storage)

File system implementations of all Core service interfaces. Key design principles:

- **Atomic writes**: All file mutations use write-to-temp-then-rename pattern
- **Content directory structure**: Same as current (`build/lore/`, `build/story/`, `build/writing/`, etc.)
- **Encrypted key store**: API keys encrypted at rest using `System.Security.Cryptography`
- **No database**: All persistence is file-based (markdown, YAML, JSON)

### 5.1 Image Generation

- ComfyUI integration (HTTP API, workflow JSON templates, polling for completion)
- OpenAI DALL-E integration
- Fallback chain with provider detection

### 5.2 TTS

- Multiple provider support via `ITtsGenerator`
- Streaming audio output

## 6. Web Layer (QuillForge.Web)

### 6.1 ASP.NET Core Minimal API

**Composition root**: All DI wiring in `Program.cs`. Services registered explicitly (constructor injection, no reflection-based discovery).

**Endpoints** (grouped by feature):

**Chat:**
- `POST /api/chat/stream` — Streaming chat via SSE (`text/event-stream`)
- `GET /api/status` — Server status, context usage, active mode

**Session:**
- `GET /api/sessions` — List saved sessions
- `POST /api/sessions/new` — Create new session
- `POST /api/sessions/{id}/load` — Load a session
- `POST /api/sessions/{id}/fork` — Fork from a message ID (GUID, not index)
- `DELETE /api/sessions/{id}/messages/{messageId}` — Delete a message by GUID
- `POST /api/sessions/{id}/messages/{messageId}/regenerate` — Create variant

**Mode & Profile:**
- `GET /api/mode` — Current mode and pending state
- `POST /api/mode` — Switch mode
- `GET /api/profiles` — List personas, lore sets, writing styles
- `POST /api/profiles/switch` — Switch active profile

**Content:**
- `POST /api/image/generate` — Generate image
- `POST /api/tts/generate` — Generate speech
- `GET /api/lore/browse` — Browse lore files
- `GET /api/artifacts` — List story artifacts
- `POST /api/artifacts` — Manage artifacts

**Forge:**
- `POST /api/forge/projects` — Create forge project
- `GET /api/forge/projects` — List projects
- `POST /api/forge/projects/{name}/run` — Run/resume pipeline (SSE)
- `POST /api/forge/projects/{name}/pause` — Pause pipeline

**Providers:**
- `GET /api/providers` — List providers
- `POST /api/providers` — Add provider
- `PUT /api/providers/{alias}` — Update provider
- `DELETE /api/providers/{alias}` — Remove provider
- `POST /api/providers/test` — Test connection
- `GET /api/providers/{alias}/models` — Fetch model list

### 6.2 SSE Streaming

Streaming responses use `IAsyncEnumerable<StreamEvent>` written as SSE events. The tool loop's `RunStreamAsync` yields events that the endpoint serializes directly to the response stream.

### 6.3 Static Files

React frontend served from `wwwroot/` via `UseStaticFiles()`. SPA fallback for client-side routing. The existing React frontend is copied into `wwwroot/` at build time (or publish time).

### 6.4 Layouts

The current layout markdown system is preserved. Users place layout `.md` files in their content directory, the API serves them, and the frontend renders accordingly.

## 7. Deployment

### 7.1 Self-Contained Binary

Published per platform via CI:
- `win-x64`
- `osx-arm64`
- `osx-x64`
- `linux-x64`

Single-file publish, self-contained (no .NET runtime required on target). React frontend embedded in the publish output.

### 7.2 Auto-Update

On startup (or via `--check-update` flag), the application checks the GitHub Releases API for a newer version. If found, downloads the platform-appropriate asset, replaces the binary, and restarts. User content directory (`build/`) is never touched by updates.

### 7.3 Start Script

Minimal platform scripts (`start.sh`, `start.bat`) that run the binary with default options. First-run setup creates the `build/` content directory with defaults.

### 7.4 GitHub Actions CI

- Triggered on version tags (`v*`)
- Builds and publishes for all target platforms
- Creates GitHub Release with platform-specific assets
- Runs full test suite before publishing

## 8. Migration Considerations

### 8.1 Content Compatibility

The `build/` content directory structure (lore, personas, writing styles, story files, forge projects, layouts, character cards, etc.) remains identical. Users' existing content works without modification.

### 8.2 Session Migration

Existing JSON session files use the flat array format. A one-time migration converts them to the tree format (each message gets a GUID, linear chain of parent→child). The migrator runs automatically on first load of a legacy session file.

### 8.3 Provider Config Migration

`build/data/providers.json` format is preserved. The encryption scheme for API keys must be compatible or a re-entry flow provided.

### 8.4 Frontend API Compatibility

Session-related endpoints change (index → GUID), so the frontend needs corresponding updates to the session/fork/delete operations. All other endpoints maintain the same request/response shapes.

## 9. Non-Functional Requirements

- **Startup time**: < 2 seconds to serving requests (excluding first-time lore loading)
- **Memory**: Baseline < 100MB (excluding LLM response buffers)
- **Concurrent users**: Support 1-3 concurrent users on the same instance (household use)
- **Offline**: Must work fully offline except for LLM API calls and auto-update checks
- **Logging**: Structured logging via `Microsoft.Extensions.Logging`. LLM debug logging (request/response capture) preserved for troubleshooting.

## 10. Testing Strategy

### 10.1 Core Tests (QuillForge.Core.Tests)

Fast, pure unit tests. No file I/O, no network, no LLM calls. All dependencies mocked via simple interface implementations (no mocking library).

Key test areas:
- **ToolLoop**: Mock `ICompletionService` to return scripted sequences (tool_use → tool_use → end_turn). Verify handler dispatch, message construction, max rounds, auto-continuation.
- **Librarian parsing**: JSON extraction edge cases (markdown fences, embedded JSON, malformed output, fallback to raw text).
- **Orchestrator routing**: Mode selection, tool dispatch per mode, tool availability per mode.
- **WriterMode state machine**: Pending content → accept/reject/regenerate transitions.
- **ForgeReviewer scoring**: Score parsing, pass/fail threshold logic.
- **ForgePipeline state transitions**: Manifest stage progression, pause handling, chapter status updates.
- **ConversationTree**: Append, fork, delete, thread extraction, variant creation, concurrent access safety.
- **RollDice**: Notation parsing, keep-highest logic, modifier arithmetic.
- **StoryState**: YAML state merge, event counter increment, key removal.

### 10.2 Provider Tests (QuillForge.Providers.Tests)

- Message format conversion (internal ↔ Anthropic, internal ↔ OpenAI)
- Tool schema translation (Core ToolDefinition → Anthropic format, → OpenAI function format)
- Provider quirk handling (DeepSeek reasoning_content, empty required arrays, Ollama context)

### 10.3 Storage Tests (QuillForge.Storage.Tests)

Integration tests with temporary directories:
- Session save/load roundtrip
- Atomic write verification (crash during write doesn't corrupt)
- Legacy session migration
- Lore file loading and search

### 10.4 Architecture Tests (QuillForge.Architecture.Tests)

Verify dependency boundaries:
- `QuillForge.Core` references no external NuGet packages
- `QuillForge.Core` has no reference to `QuillForge.Providers` or `QuillForge.Storage`
- No project references `QuillForge.Web`
- LLM SDK types don't appear in Core's public API surface

## 11. Email Developer Tool

The orchestrator has a tool that lets users email the developer (you) with bug reports or feature requests directly from the chat interface. Preserve this in the rewrite with the same SMTP configuration.

## 12. Web Search Tool

The orchestrator can search the web for real-world information outside the lore corpus. Preserve this capability.

## 13. Story State System

The orchestrator maintains a `.state.yaml` companion file alongside story/chat files tracking plot threads, character conditions, tension levels, event counters, etc. This state is separate from the conversation tree and persists across sessions. Preserve this system with the same YAML format for backwards compatibility.
