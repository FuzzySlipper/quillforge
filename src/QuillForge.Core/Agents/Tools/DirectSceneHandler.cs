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
        "Route a canon-sensitive scene or drafting request through the Narrative Director, which grounds the request, updates state, and returns the final prose response.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "user_message": {
                        "type": "string",
                        "description": "The latest scene, roleplay, or grounded drafting request to respond to"
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

        try
        {
            var result = await _narrativeDirector.DirectSceneAsync(
                new NarrativeDirectionRequest
                {
                    UserMessage = userMessage,
                },
                context,
                ct);

            return ToolResult.Ok(result.ResponseText);
        }
        catch (CanonPrerequisiteException ex)
        {
            _logger.LogWarning(
                ex,
                "DirectSceneHandler rejected grounded scene generation for session {SessionId}",
                context.SessionId);
            return ToolResult.Fail(ex.Message);
        }
    }
}

public sealed record DirectSceneArgs
{
    public string UserMessage { get; init; } = "";
}
