using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// A named, strongly-typed handler for a single tool. Each tool handler is a distinct class
/// with constructor-injected dependencies — no lambdas, no closures.
/// </summary>
public interface IToolHandler
{
    string Name { get; }
    ToolDefinition Definition { get; }
    Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default);
}
