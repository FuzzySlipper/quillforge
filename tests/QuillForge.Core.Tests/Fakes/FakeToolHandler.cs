using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// A simple configurable fake tool handler for testing.
/// </summary>
public sealed class FakeToolHandler : IToolHandler
{
    private readonly Func<ToolInput, AgentContext, CancellationToken, Task<ToolResult>> _handler;

    public FakeToolHandler(
        string name,
        Func<ToolInput, AgentContext, CancellationToken, Task<ToolResult>>? handler = null,
        string? inputSchemaJson = null)
    {
        Name = name;
        Definition = new ToolDefinition(name, $"Fake tool: {name}",
            System.Text.Json.JsonDocument.Parse(inputSchemaJson ?? """{"type":"object","properties":{}}""").RootElement);
        _handler = handler ?? ((_, _, _) => Task.FromResult(ToolResult.Ok($"{name} result")));
    }

    public string Name { get; }
    public ToolDefinition Definition { get; }

    public Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        CallCount++;
        return _handler(input, context, ct);
    }

    public int CallCount { get; private set; }

    /// <summary>
    /// Creates a handler that tracks call count and returns a fixed result.
    /// </summary>
    public static FakeToolHandler WithTracking(string name, string result = "ok")
    {
        return new FakeToolHandler(name, (_, _, _) => Task.FromResult(ToolResult.Ok(result)));
    }
}
