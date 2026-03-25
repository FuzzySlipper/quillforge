---
name: Architecture Decisions
description: Key design decisions settled during pre-implementation review (2026-03-25)
type: project
---

- **Discriminated unions**: Use abstract base class + sealed derived types (e.g., StreamEvent, ToolResult).
- **StreamEvent**: Abstract base with sealed derived types: TextDelta, ToolCall, Done.
- **ToolResult**: Use static factory methods — `ToolResult.Success(content)` / `ToolResult.Failure(error)` — to prevent inconsistent state.
- **Core NuGet deps are fine**: The "zero NuGet" rule was overstated. The real rule is that LLM provider SDK types must not leak out of QuillForge.Providers. Microsoft.Extensions.Logging.Abstractions in Core is fine.
- **.NET 10**: Staying on net10.0 to keep in sync with user's other project. Not negotiable.
- **Encryption/provider keys**: No need to port Python's encryption. Use a clean .NET crypto solution. Users re-enter keys on migration — acceptable tradeoff.
- **Forge error handling**: Pipeline persists progress to manifest file after each step. On error, cancel the run at current point. On restart, skip completed work based on manifest. Add configurable timeout in config file.
- **ConversationTree**: Nodes dict should not be publicly exposed as mutable. Use IReadOnlyDictionary or lookup methods. ChildIds on MessageNode should be IReadOnlyList in public API. Mutable setters (Name, ActiveLeafId) need to go through locking.

**Why:** These were settled to avoid rework and ensure the C# version is more durable than the Python original.
**How to apply:** Reference these when implementing Core types, the provider layer, and the forge pipeline.
