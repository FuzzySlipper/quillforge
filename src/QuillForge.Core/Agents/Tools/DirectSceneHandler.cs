using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates interactive scene direction to the NarrativeDirectorAgent.
/// The director owns scene decisions, story-state updates, and prose handoff.
/// </summary>
public sealed class DirectSceneHandler : TypedToolHandler<DirectSceneArgs>
{
    private readonly NarrativeDirectorAgent _narrativeDirector;
    private readonly ILogger<DirectSceneHandler> _logger;

    public DirectSceneHandler(
        NarrativeDirectorAgent narrativeDirector,
        ILogger<DirectSceneHandler> logger)
    {
        _narrativeDirector = narrativeDirector;
        _logger = logger;
    }

    public override string Name => "direct_scene";

    public override ToolDefinition Definition => new(
        Name,
        "Direct the next beat of an interactive scene, update story state, and return the final prose response.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "user_message": {
                        "type": "string",
                        "description": "The latest in-scene user message or action to respond to"
                    }
                },
                "required": ["user_message"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(DirectSceneArgs input, AgentContext context, CancellationToken ct = default)
    {
        var userMessage = input.UserMessage;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return ToolResult.Fail("user_message is required.");
        }

        _logger.LogDebug(
            "DirectSceneHandler: directing scene for session {SessionId}",
            context.SessionId);

        var result = await _narrativeDirector.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = userMessage,
            },
            context,
            ct);

        return ToolResult.Ok(result.ResponseText);
    }
}

public sealed record DirectSceneArgs
{
    public string UserMessage { get; init; } = "";
}
