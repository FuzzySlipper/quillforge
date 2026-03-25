---
name: Feedback - Logging and Provider Isolation
description: User wants effusive logging throughout; real concern is provider SDK leakage not NuGet deps
type: feedback
---

Effusive logging everywhere so issues can be traced easily. Do not skimp on log statements.

The original Python project had Claude SDK usage leaking into random places outside the provider layer. The hard rule is: provider-specific types stay in QuillForge.Providers. This is about type isolation, not about NuGet package counts.

**Why:** The Python version was hard to debug and had SDK coupling scattered throughout. The user has been burned by both issues.
**How to apply:** Add detailed logging at every significant operation. When writing Core code, never reference any provider-specific type — use the Core abstractions (ICompletionService, CompletionRequest/Response, etc.).
