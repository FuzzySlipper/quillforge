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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var messageLength = userMessage.Length;
        var contextHint = context.SessionContext is not null
            ? $"characterSection={context.SessionContext.CharacterSection?.Length ?? 0}, storyState={context.SessionContext.StoryStateSummary?.Length ?? 0}, directorNotes={context.SessionContext.DirectorNotes?.Length ?? 0}"
            : "no session context";

        _logger.LogInformation(
            "DirectSceneHandler started: session={SessionId}, mode={Mode}, messageLength={MessageLength}, {ContextHint}",
            context.SessionId,
            context.ActiveMode,
            messageLength,
            contextHint);

        try
        {
            var result = await _narrativeDirector.DirectSceneAsync(
                new NarrativeDirectionRequest
                {
                    UserMessage = userMessage,
                },
                context,
                ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "DirectSceneHandler completed: session={SessionId}, elapsedMs={ElapsedMs}, responseLength={ResponseLength}",
                context.SessionId,
                stopwatch.ElapsedMilliseconds,
                result.ResponseText?.Length ?? 0);

            return ToolResult.Ok(result.ResponseText ?? "");
        }
        catch (CanonPrerequisiteException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "DirectSceneHandler rejected grounded scene generation for session {SessionId}",
                context.SessionId);
            return ToolResult.Fail(ex.Message);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "DirectSceneHandler timed out or was cancelled: session={SessionId}, elapsedMs={ElapsedMs}, messageLength={MessageLength}",
                context.SessionId,
                stopwatch.ElapsedMilliseconds,
                messageLength);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "DirectSceneHandler failed: session={SessionId}, elapsedMs={ElapsedMs}, messageLength={MessageLength}",
                context.SessionId,
                stopwatch.ElapsedMilliseconds,
                messageLength);
            throw;
        }
    }
}

public sealed record DirectSceneArgs
{
    public string UserMessage { get; init; } = "";
}
