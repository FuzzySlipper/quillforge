# QuillForge

QuillForge is an AI-powered creative writing system for building stories, lore, characters, and long-form fiction in one place.

It is a ground-up C#/.NET rewrite of an older Python/FastAPI app. The rewrite keeps the file-based, user-owned workflow of the original project, but adds stronger architecture boundaries, better session handling, more test coverage, and cleaner provider integration.

QuillForge is under active development. Releases are built from Git tags and are the recommended way for non-technical users to run the app. Your content root is treated as your data and is meant to survive updates. In current portable builds that root is usually `user/`; desktop-mode launches use `Documents/QuillForge`.

## Features

- Conversational orchestrator with specialized agents behind the scenes
- Eight working modes: Guide, Writer, Roleplay, Lore Builder, Forge, Council, Research, and Games
- Branching conversation history with retry, fork, delete, and variants
- Lore-backed responses and writing assistance
- Autonomous Forge pipeline for long-form story generation
- Multi-provider support: Anthropic, OpenAI, Ollama, OpenRouter, Azure OpenAI, and OpenAI-compatible endpoints
- Optional reasoning display when a provider/model exposes reasoning content
- Artifact generation, research workflows, image generation, and TTS support
- Modular social-games framework with a typed Werewolf table, saved templates, agent seats, hidden-information projections, and harness traces

## Modes

| Mode | Purpose |
|------|---------|
| Guide | Onboarding, troubleshooting, and mode selection |
| Writer | Long-form project writing with pending-content accept/reject workflow |
| Roleplay | Interactive narrative with character context and roleplay state |
| Lore Builder | Guided creation and maintenance of lore documents for the Librarian |
| Forge | Command-and-pipeline control for autonomous story production |
| Council | Multi-advisor synthesis for brainstorming and critique |
| Research | Parallel research workflows and research project output |
| Games | Typed social-games table for Werewolf and future explicit game modules |

## Getting Started

### Requirements

- .NET 10 SDK if you want to build from source
- An LLM provider API key or a local Ollama server
- Node.js 20 if you are building from source and need the frontend build path

### From Release (Recommended)

Download the latest desktop release for your platform from:

<https://github.com/FuzzySlipper/quillforge/releases>

Current release artifacts prioritize the desktop shell:

- Fedora-friendly RPM: `QuillForge-fedora-x86_64.rpm`
- Debian-friendly DEB: `QuillForge-debian-amd64.deb`
- macOS manual-download bundles: `QuillForge-macos-<arch>.app.zip` and `QuillForge-macos-<arch>.dmg`
- Windows installer: `QuillForge-windows-x64-setup.exe`

Install by platform:
- Fedora: `sudo dnf install ./QuillForge-fedora-x86_64.rpm`
- Debian/Ubuntu: `sudo apt install ./QuillForge-debian-amd64.deb`
- macOS: unzip `QuillForge-macos-<arch>.app.zip` or open the `.dmg`, then move `QuillForge.app` into `Applications`
- Windows: run `QuillForge-windows-x64-setup.exe`

macOS note for the current unsigned early builds:
- if macOS blocks launch, open `System Settings -> Privacy & Security`, find the blocked QuillForge message, and choose `Open Anyway`
- after that, QuillForge should launch normally from `Applications`

After install, launch QuillForge from the desktop app your platform provides. On first run, QuillForge creates a content root with starter content and data folders. Desktop launches stay local-only by default; use the desktop shell's LAN/mobile toggle when you want phone or tablet access on the same trusted network.

Workspace behavior:
- source/dev runs normally use repo-local `user/`
- portable published runs normally use a sibling `user/` directory
- desktop-mode launches default to `Documents/QuillForge`
- if a desktop-mode launch finds an older sibling `user/` directory next to the published app and `Documents/QuillForge` is still empty, QuillForge copies that old workspace into `Documents/QuillForge` and leaves the original in place

Manual updates keep your writing workspace in place:
- Fedora: reinstall the latest RPM over the old one, for example `sudo dnf install https://github.com/FuzzySlipper/quillforge/releases/latest/download/QuillForge-fedora-x86_64.rpm`
- Debian/Ubuntu: install the new DEB over the old one with `sudo apt install ./QuillForge-debian-amd64.deb`
- macOS: replace the old `QuillForge.app` in `Applications` with the newer one you downloaded
- Windows: run the newer installer and let it replace the installed app
- in all of those cases, leave `Documents/QuillForge` alone unless you are intentionally moving or backing up your writing workspace

If you are running the legacy browser/server host from source or an older portable build, open the URL shown in the terminal. In source-development runs this is usually `http://localhost:5204`.

QuillForge can check GitHub releases and show update availability in the app, but it does not auto-install updates for you. For a maintainer-facing pre-release checklist, see `docs/desktop-release-validation.md`. For social-games setup, testing, and extension workflows, see `docs/social-games-setup-testing-extension.md`.

### From Source

```bash
git clone https://github.com/FuzzySlipper/quillforge.git
cd quillforge
dotnet run --project src/QuillForge.Web
```

Then open the URL printed in the terminal.

### Configure a Provider

You can add providers through the UI or through the API.

```bash
# Anthropic
curl -X POST http://localhost:5204/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias":"claude","type":"Anthropic","apiKey":"sk-ant-...","defaultModel":"claude-sonnet-4-20250514"}'

# OpenAI
curl -X POST http://localhost:5204/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias":"gpt","type":"OpenAI","apiKey":"sk-...","defaultModel":"gpt-4o"}'

# Local Ollama
curl -X POST http://localhost:5204/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias":"local","type":"Ollama","baseUrl":"http://localhost:11434","defaultModel":"qwen2.5:14b"}'
```

## Configuration

QuillForge stores app-level configuration in `<content-root>/config.yaml`.

Example:

```yaml
models:
  orchestrator: claude
  prose_writer: claude
  librarian: local
  forge_writer: claude
  forge_planner: claude
  forge_reviewer: claude
  artifact: default
  research: local
profiles:
  default: default
persona:
  active: narrator
  max_tokens: 6000
narrative_rules:
  active: default
lore:
  active: default
writing_style:
  active: literary
layout:
  active: default
web_search:
  enabled: true
  provider: searxng
  searxng_url: http://localhost:8080
forge:
  review_pass_threshold: 7.0
  max_revisions: 3
  pause_after_chapter1: true
  stage_timeout_minutes: 120
```

Web search can be configured from the in-app App Settings panel next to Provider Manager. If you prefer editing YAML directly, web search providers currently include `searxng`, `tavily`, `brave`, `google`, and `zai`. The Z.AI option is for users who already have a GLM/Z.AI Coding Plan subscription and want QuillForge to call Z.AI's hosted Web Search MCP endpoint directly as a web-search provider:

```yaml
web_search:
  enabled: true
  provider: zai
  zai_api_key: your_zai_api_key
  # Optional; defaults to Z.AI's documented Web Search MCP endpoint/tool.
  zai_mcp_endpoint: https://api.z.ai/api/mcp/web_search_prime/mcp
  zai_mcp_tool_name: webSearchPrime
  max_results: 10
```

Although Z.AI documents this as an MCP server for coding clients, QuillForge does not expose it as a general MCP client. It formats the standard Streamable HTTP MCP `initialize`, `tools/list`, and `tools/call` requests internally and maps the `webSearchPrime` result back into the existing `web_search` tool.

Naming note: `persona.active` is still a compatibility field from earlier versions. Live interactive routing is now app-owned by mode rather than driven by a user-editable conductor prompt.

## Content Directory

QuillForge keeps user-owned content in the content root. In source/dev and current portable runs that root is usually `user/`. In desktop-mode launches it defaults to `Documents/QuillForge`. This directory is intended to be portable and safe to back up.

```text
<content-root>/
├── config.yaml
├── lore/
├── assistant/
├── conductor/
├── librarian-prompts/
├── narrative-rules/
├── profiles/
├── plots/
├── writing-styles/
├── story/
├── writing/
├── chats/
├── forge/
├── forge-prompts/
├── council/
├── layouts/
├── character-cards/
├── backgrounds/
├── generated-images/
├── generated-audio/
├── artifacts/
├── research/
├── game-templates/
└── data/
    ├── providers.json
    ├── sessions/
    ├── session-state/
    └── llm-debug/
```

Practical rule:

- Edit the content folders when you want to change lore, prompts, profiles, layouts, or writing assets.
- Avoid hand-editing `<content-root>/data/sessions/` and `<content-root>/data/session-state/` unless you are doing recovery or debugging work.
- `<content-root>/conductor/` is retained as legacy migration/reference material if present; current live routing behavior is app-owned by mode.
- If you are migrating from SillyTavern or another "one big prompt" setup, see `dev/app-docs/sillytavern-migration.md` for the recommended split across lore, character cards, narrative rules, writing style, librarian prompts, and Assistant tone.

## Development

### Build and Test

```bash
dotnet restore QuillForge.slnx
dotnet build QuillForge.slnx
dotnet test QuillForge.slnx
```

For a faster local suite that skips integration-style tests and live-provider tests:

```bash
dotnet test QuillForge.slnx --filter "Category!=Integration&Category!=LiveProvider"
```

### Desktop Shell

The first Tauri desktop shell lives in `src/QuillForge.Desktop/`.

```bash
cd src/QuillForge.Desktop
npm install
npm run tauri:dev
```

That flow builds the shell UI, publishes `QuillForge.Web` as a self-contained sidecar for the current host, and launches the desktop app. The tagged release workflow now stages stable desktop assets for Fedora RPM, Debian DEB, macOS zipped `.app` bundles plus DMGs, and a Windows installer. The desktop shell keeps backend binding local-only by default and can explicitly restart in LAN/mobile mode when you need to reach it from a phone or tablet on the same trusted network. Local Linux `tauri:build` runs can still stay focused on `.deb` unless you override bundles explicitly. AppImage packaging remains a follow-up path while linuxdeploy support is being sorted out. Use `docs/desktop-release-validation.md` when you need the concrete pre-tag manual validation checklist.

### Source Layout

```text
src/
  Den.Persistence/       Product-neutral persisted document infrastructure
  Den.RulesEngine/       Portable deterministic social-games rules engine
  Den.RulesEngine.Werewolf/ First explicit Werewolf game module
  QuillForge.Core/       Domain models, tool loop, modes, agents, pipeline
  QuillForge.Providers/  LLM adapters and provider-specific integrations
  QuillForge.Storage/    File-backed stores, config/session persistence, content I/O
  QuillForge.Web/        ASP.NET Core host, endpoints, startup, React client
  QuillForge.Desktop/    Tauri desktop shell, local shell UI, backend sidecar packaging

tests/
  Den.RulesEngine.Tests/
  Den.RulesEngine.Werewolf.Tests/
  QuillForge.Core.Tests/
  QuillForge.Providers.Tests/
  QuillForge.Storage.Tests/
  QuillForge.Architecture.Tests/
```

High-level dependency direction:

- `QuillForge.Web -> QuillForge.Providers -> QuillForge.Core`
- `QuillForge.Web -> QuillForge.Storage -> Den.Persistence`
- `QuillForge.Storage -> QuillForge.Core`
- `QuillForge.Core` depends on nothing in the rest of QuillForge

### Publishing

```bash
dotnet publish src/QuillForge.Web/QuillForge.Web.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web/QuillForge.Web.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web/QuillForge.Web.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

## License

See [LICENSE](LICENSE) for details.

## References

Concepts based in part on [CreAgentive: An Agent Workflow Driven Multi-Category Creative Generation Engine](https://arxiv.org/html/2509.26461v1)

## Architecture Guide

This section is intentionally written for two audiences:

- people trying to understand what QuillForge is doing
- AI assistants such as Claude Desktop helping someone install, run, configure, or troubleshoot it

If you only need the short version, read the next two sections. If you are helping someone more deeply, read the whole guide.

### The Short Version

QuillForge is not just "one big prompt plus chat history."

It is a layered app with:

- a web host and React UI
- an orchestrator that decides how to respond
- a shared tool loop that can call specialized tools and agents
- provider adapters that hide vendor-specific SDK quirks
- file-backed user content and session state

The project is intentionally split so that story logic, provider logic, web endpoints, and persistent files do not collapse into one giant blob.

### The Mental Model

Think of QuillForge as a writing studio with a front desk and several specialist rooms.

- You talk to the front desk through the web UI.
- The front desk is the orchestrator.
- The orchestrator can answer directly or call specialists.
- Specialists can look up lore, write prose, run council responses, launch research work, or move work through the Forge pipeline.
- Everything important is written down into durable files so a session can be resumed later.

This is why the codebase looks more segmented than a simple chatbot app. The segmentation is deliberate.

### What Happens When You Send a Message

When a user sends a message, the rough flow is:

1. The browser sends the message and current session ID to the web API.
2. QuillForge loads the live session runtime and the conversation tree for that session.
3. It resolves the active profile, lore set, writing style, mode, and other context.
4. The orchestrator builds the mode-specific prompt section.
5. The shared `ToolLoop` calls the selected model.
6. If the model requests tools, the tool loop validates and dispatches those tool calls.
7. Tool results come back into the loop and the model continues.
8. Streaming text, diagnostics, and optional reasoning deltas are sent to the UI.
9. The final assistant message is persisted into the conversation tree.
10. Session runtime is updated if the mode needs it, such as writer pending content or roleplay state.

Important detail: provider reasoning is not treated as ordinary chat transcript text by default. QuillForge stores user-visible reasoning separately and keeps provider replay data in a provider-owned envelope when that matters for continuation.

### The Four Main Kinds of State

One of the most important ideas in QuillForge is that different kinds of state have different owners.

#### 1. AppConfig

`AppConfig` is the app-wide durable config document.

It owns things like:

- provider settings
- model routing defaults
- diagnostics defaults
- the default profile for new sessions
- app-wide layout and feature settings

It should not be treated as "whatever is active right now in one conversation."

#### 2. ProfileConfig

`ProfileConfig` is a reusable bundle of author choices.

A profile is where QuillForge stores reusable selections such as:

- lore set
- narrative rules
- writing style
- librarian prompt
- roleplay defaults

Profiles are meant to be reused across many sessions.

#### 3. SessionState

`SessionState` is the live runtime for one interactive run.

It owns things like:

- active mode
- active project/file
- session-local overrides
- writer pending review state
- roleplay runtime selections
- narrative runtime such as plot progress or director notes

This is "what this session is doing right now."

#### 4. ConversationTree

`ConversationTree` is the persisted branching message artifact.

This is not just a flat list of messages. It supports:

- branching
- retry variants
- message deletion
- conversation forking
- stable GUID message identity

The tree is intentionally separate from session runtime. A conversation transcript and a live session state are related, but they are not the same thing.

### Why QuillForge Keeps SessionState and ConversationTree Separate

This separation is one of the parts that feels unusual at first.

The short reason is that "what was said" and "what the session is currently set to" are different kinds of truth.

Examples:

- The conversation tree answers: what messages exist, what branch is active, and what variants were generated.
- The session state answers: what mode is active, what project is open, whether writer mode has pending content, and what character is selected in roleplay.

Keeping them separate makes branching, reloading, and debugging much less brittle.

### Why the Project Is Split Into Multiple C# Projects

The split is there to enforce boundaries, not to make the repo look enterprise-y.

#### `src/QuillForge.Core`

This is the domain center of the app.

It contains:

- domain models
- conversation tree
- session models
- modes
- agents
- tool loop
- pipeline types

It should not know about Anthropic SDK types, OpenAI SDK types, ASP.NET, or filesystem implementation details.

#### `src/QuillForge.Providers`

This is where provider-specific logic lives.

It contains:

- Anthropic, OpenAI, Ollama, Azure, and OpenRouter integration
- reasoning-capable adapters
- TTS and image generation provider wiring
- web search provider implementations

If a provider has special request or streaming behavior, this is where that code belongs.

#### `src/QuillForge.Storage`

This contains file-backed stores and content loading logic.

It is responsible for:

- reading and writing config
- reading and writing profiles
- reading and writing sessions
- loading lore, assistant prompts, legacy conductor prompts, writing styles, and other content files

The app is intentionally file-first. User content should remain inspectable and portable.

#### `src/Den.Persistence`

This is product-neutral persistence infrastructure used by QuillForge.

It exists so persisted document concerns such as atomic writes, normalization, validation, and migrations can live in one place instead of being reimplemented ad hoc.

#### `src/QuillForge.Web`

This is the application host.

It contains:

- ASP.NET Core startup and DI registration
- HTTP endpoints
- session/request coordination
- the React frontend in `Client/`

If something is specifically about request handling, startup wiring, or browser behavior, it probably belongs here.

### Modes Are Prompt and Workflow Choices, Not Separate Apps

QuillForge has multiple modes, but they are not unrelated systems glued together.

They are six behavior layers on top of shared runtime and tool infrastructure.

- Guide mode is the front desk and default starting point.
- Writer mode leans into scene generation and pending review workflow.
- Roleplay mode uses character context and session character state.
- Forge mode is an operator surface for explicit forge commands and pipeline stages.
- Council mode runs advisory-style multi-voice output.
- Research mode coordinates structured research work and project output.

Because the modes share the same session and chat foundations, bugs in shared paths usually affect more than one mode. That is why shared contracts and regression tests matter so much here.

### Tool Loop: The Core Reusable Engine

QuillForge does not want every agent to hand-roll its own "call model -> maybe call a tool -> call model again" logic.

That pattern lives in one place: the `ToolLoop`.

That gives the app one shared implementation for:

- completion calls
- tool validation
- tool dispatch
- retry/tool-round control
- streaming events
- continuation behavior

This reduces drift between agents and makes bugs easier to fix once instead of six times.

### Provider Reasoning, Streaming, and Persistence

Some modern models emit reasoning or provider-specific replay state.

QuillForge treats that carefully:

- visible assistant content is the normal transcript
- user-visible reasoning is stored separately so it can be restored in the UI
- provider replay data is stored separately so adapters can reconstruct the correct vendor-specific context when needed

This is why reasoning is not simply jammed into the visible assistant text.

If you are an assistant helping debug the app, do not assume reasoning belongs in the plain transcript. In QuillForge it is intentionally modeled as a separate artifact.

### Why the `user/` Folder Matters So Much

QuillForge is designed around user-owned files.

That means:

- lore is plain markdown
- assistant prompts are plain markdown
- conductors are plain markdown legacy migration material when present
- writing styles are plain markdown
- profiles are plain yaml
- sessions are json
- debug logs are files

This makes the system easier to inspect, back up, and recover.

For non-technical users, this also means a helper app can usually solve problems by editing files rather than reverse-engineering a database.

### Which Files Are Usually Safe To Edit

Usually safe:

- `user/lore/`
- `user/assistant/`
- `user/conductor/` if you are inspecting or migrating older installs
- `user/librarian-prompts/`
- `user/narrative-rules/`
- `user/profiles/`
- `user/plots/`
- `user/writing-styles/`
- `user/layouts/`
- `user/character-cards/`
- `user/config.yaml`

Usually leave alone unless debugging or doing careful recovery:

- `user/data/sessions/`
- `user/data/session-state/`
- `user/data/providers.json`
- generated files under `user/generated-images/`, `user/generated-audio/`, and active forge artifacts unless the user explicitly wants cleanup

### Expectations for AI Helpers

If you are an AI assistant helping someone with a QuillForge checkout or installation:

1. Prefer the latest GitHub release for normal users.
2. Treat `user/` as the user's owned data and work area.
3. Treat `src/` as the app code.
4. Do not flatten provider-specific reasoning into normal transcript content when debugging or migrating sessions.
5. Do not assume message order is identity. Message IDs are GUIDs.
6. Do not assume session runtime and conversation history are interchangeable.
7. Prefer editing user content files over rewriting generated session JSON when the goal is customization rather than repair.
8. If operating inside the repository, read `AGENTS.md` and `docs/prd.md` before making code changes.

### Why the Structure Can Feel Odd At First

If QuillForge feels more structured than a typical local writing/chat app, that is because it is trying to protect a few things at once:

- user-owned files
- provider flexibility
- multi-mode behavior
- durable sessions
- recoverable state
- a codebase that can survive rapid iteration without collapsing into untestable glue

The shape is intentional. It is meant to make future changes safer, not to be academically pure.

### If You Are Trying To Understand The Repo Quickly

Start here:

1. `README.md`
2. `AGENTS.md`
3. `docs/prd.md`
4. `src/QuillForge.Web/Program.cs`
5. `src/QuillForge.Core/Agents/ToolLoop.cs`
6. `src/QuillForge.Core/Models/ConversationTree.cs`
7. `src/QuillForge.Core/Models/SessionState.cs`
8. `src/QuillForge.Web/Endpoints/ChatEndpoints.cs`

That path will give you the fastest understanding of how the app actually behaves.
