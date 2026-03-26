# QuillForge

An AI-powered creative writing system. QuillForge provides a conversational interface backed by specialized agents that help authors build, explore, and write within richly detailed fictional worlds.

## Features

- **Librarian** — Query your lore corpus with source attribution and confidence scoring
- **Prose Writer** — Generate scenes with automatic lore lookups for consistency
- **Orchestrator** — Conversational partner with five interaction modes
- **Forge Pipeline** — Autonomous long-form story generation (plan, design, write, review, assemble)
- **Conversation Tree** — Branching conversations with forking, variants, and safe deletion
- **Multi-Provider** — Anthropic, OpenAI, Ollama, OpenRouter, Azure OpenAI, and custom OpenAI-compatible endpoints

### Modes

| Mode | Purpose |
|------|---------|
| General | Free-form conversation with flexible tool routing |
| Writer | Long-form project writing with accept/reject workflow |
| Roleplay | Interactive narrative with auto-append and dice rolling |
| Forge | Conversational story design before autonomous pipeline |
| Council | Multi-perspective AI advisory synthesis |

## Getting Started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building from source)
- An LLM provider API key (Anthropic, OpenAI, etc.) or a local [Ollama](https://ollama.ai) server

### From Release (Recommended)

Download the latest release for your platform from [Releases](../../releases). No runtime dependencies required.

```bash
# Linux / macOS
chmod +x QuillForge.Web
./QuillForge.Web
```

```
# Windows
QuillForge.Web.exe
```

On first run, QuillForge creates a `build/` directory with example content — lore, personas, writing styles, and layouts to get you started.

Open `http://localhost:5000` in your browser.

### From Source

```bash
git clone https://github.com/your-org/quillforge.git
cd quillforge
dotnet run --project src/QuillForge.Web
```

### Configure a Provider

After starting, add an LLM provider through the UI or via the API:

```bash
# Anthropic
curl -X POST http://localhost:5000/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias": "claude", "type": "Anthropic", "apiKey": "sk-ant-...", "defaultModel": "claude-sonnet-4-20250514"}'

# OpenAI
curl -X POST http://localhost:5000/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias": "gpt", "type": "OpenAI", "apiKey": "sk-...", "defaultModel": "gpt-4o"}'

# Local Ollama (no API key needed)
curl -X POST http://localhost:5000/api/providers \
  -H "Content-Type: application/json" \
  -d '{"alias": "local", "type": "Ollama", "baseUrl": "http://localhost:11434", "defaultModel": "qwen2.5:14b"}'
```

## Configuration

Edit `build/config.yaml` to customize models, active profiles, and features:

```yaml
models:
  orchestrator: claude        # provider alias or "default"
  prose_writer: claude
  librarian: local
persona:
  active: narrator
lore:
  active: default
writing_style:
  active: literary
web_search:
  enabled: true
  provider: searxng
  searxng_url: http://localhost:8080
forge:
  review_pass_threshold: 7.0
  max_revisions: 3
  stage_timeout_minutes: 120
```

## Content Directory

QuillForge stores all user content in the `build/` directory:

```
build/
├── config.yaml              App configuration
├── lore/                    World-building markdown (organized by lore set)
├── persona/                 Character/persona definitions
├── writing-styles/          Prose style guides
├── story/                   Append-only chapter files
├── writing/                 Workspace drafts
├── chats/                   Roleplay session logs
├── forge/                   Forge project directories
├── forge-prompts/           Customizable forge stage prompts
├── council/                 Advisor persona prompts
├── layouts/                 UI layout markdown files
├── backgrounds/             UI background images
├── data/
│   ├── sessions/            Conversation history (JSON)
│   └── llm-debug/           Debug logs for LLM calls
```

All content is plain files (markdown, YAML, JSON). Back up the `build/` directory to preserve your work. Updates never touch this directory.

## Development

### Build & Test

```bash
dotnet build QuillForge.slnx
dotnet test QuillForge.slnx                                    # all tests (includes LLM integration)
dotnet test QuillForge.slnx --filter "Category!=Integration"   # unit tests only (no LLM needed)
```

### Architecture

```
src/
  QuillForge.Core/        Domain models, agents, tool handlers, pipeline stages.
                          No LLM SDK dependencies.
  QuillForge.Providers/   LLM SDK adapters (Anthropic, OpenAI via Microsoft.Extensions.AI).
                          Provider-specific types stay here.
  QuillForge.Storage/     File system implementations, session persistence,
                          configuration loading.
  QuillForge.Web/         ASP.NET Core host, API endpoints, DI composition root.

tests/
  QuillForge.Core.Tests/          Fast unit tests (no I/O, no LLM)
  QuillForge.Providers.Tests/     Message format conversion + Ollama integration tests
  QuillForge.Storage.Tests/       File I/O tests with temp directories
  QuillForge.Architecture.Tests/  Dependency boundary enforcement
```

Dependency direction: `Web -> Providers -> Core` and `Web -> Storage -> Core`. Core depends on nothing.

### Publishing

```bash
# Self-contained single-file binary (no .NET runtime required on target)
dotnet publish src/QuillForge.Web -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/QuillForge.Web -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

## License

See [LICENSE](LICENSE) for details.

## References

Concepts based on paper [CreAgentive: An Agent Workflow Driven Multi-Category Creative Generation Engine](https://arxiv.org/html/2509.26461v1)