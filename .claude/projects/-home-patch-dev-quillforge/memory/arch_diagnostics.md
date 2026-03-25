---
name: Diagnostics / Debug Console Design
description: IDiagnosticSource pattern for exposing internal state to chat console and agents via /debug command
type: project
---

User wants internal state observable through the chat interface via a `/debug` command. Both users and agents can invoke it — particularly valuable because non-technical users benefit from the orchestrator helping them diagnose issues.

**Design:** `IDiagnosticSource` interface in Core. Services opt in by implementing it. No reflection, no attributes, no scanning — explicit registration in Program.cs like everything else.

```csharp
public interface IDiagnosticSource
{
    string Category { get; }
    Task<IReadOnlyDictionary<string, object>> GetDiagnosticsAsync(CancellationToken ct);
}
```

A `DebugToolHandler : IToolHandler` collects from all registered `IDiagnosticSource` instances and formats output. Available to the orchestrator as a tool so agents can self-diagnose.

**Why:** Users are non-technical. In the Python version, having the orchestrator help diagnose issues has been very beneficial. User previously used reflection attributes for this but considers that approach outdated — explicit opt-in is preferred.

**How to apply:** When implementing services (agents, forge pipeline, session store, provider registry, etc.), consider what state would be useful for debugging and implement `IDiagnosticSource` where appropriate. Don't add it to every class — focus on services where runtime state is non-obvious: active session info, provider connection status, forge pipeline progress, mode state, lore index stats, etc.
