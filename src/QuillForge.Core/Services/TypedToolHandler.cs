using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Base class for handlers that want typed tool arguments by default.
/// </summary>
public abstract class TypedToolHandler<TArgs> : IToolHandler where TArgs : notnull
{
    public abstract string Name { get; }
    public abstract ToolDefinition Definition { get; }

    public Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var args = ToolArgs<TArgs>.Parse(input);
        return HandleTypedAsync(args, context, ct);
    }

    protected abstract Task<ToolResult> HandleTypedAsync(TArgs input, AgentContext context, CancellationToken ct = default);
}
