using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Reads the current story state from a .state.yaml companion file.
/// Resolves the active story-state path from prepared interactive session context.
/// </summary>
public sealed class GetStoryStateHandler : IToolHandler
{
    private readonly IStoryStateService _storyState;
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ILogger<GetStoryStateHandler> _logger;

    public GetStoryStateHandler(
        IStoryStateService storyState,
        IInteractiveSessionContextService sessionContextService,
        ILogger<GetStoryStateHandler> logger)
    {
        _storyState = storyState;
        _sessionContextService = sessionContextService;
        _logger = logger;
    }

    public string Name => "get_story_state";

    public ToolDefinition Definition => new(Name,
        "Read the current story/session state (plot threads, character conditions, tension, event counters).",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {},
                "required": []
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        _logger.LogDebug(
            "GetStoryStateHandler: reading state from {Path} for session {SessionId}",
            sessionContext.StoryStatePath,
            context.SessionId);
        var state = await _storyState.LoadAsync(sessionContext.StoryStatePath, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(state));
    }
}

/// <summary>
/// Merges updates into the story state.
/// Resolves the active story-state path from prepared interactive session context.
/// </summary>
public sealed class UpdateStoryStateHandler : IToolHandler
{
    private readonly IStoryStateService _storyState;
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ILogger<UpdateStoryStateHandler> _logger;

    public UpdateStoryStateHandler(
        IStoryStateService storyState,
        IInteractiveSessionContextService sessionContextService,
        ILogger<UpdateStoryStateHandler> logger)
    {
        _storyState = storyState;
        _sessionContextService = sessionContextService;
        _logger = logger;
    }

    public string Name => "update_story_state";

    public ToolDefinition Definition => new(Name,
        "Update story state by merging new values. Supports nested updates and event counter increment.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "updates": {
                        "type": "object",
                        "description": "Key-value pairs to merge into the state"
                    },
                    "increment_counter": {
                        "type": "boolean",
                        "description": "If true, increment the event counter for pacing"
                    }
                },
                "required": ["updates"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var args = ToolArgs<UpdateStoryStateArgs>.Parse(input);
        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        var path = sessionContext.StoryStatePath;
        _logger.LogDebug("UpdateStoryStateHandler: updating state at {Path} for session {SessionId}", path, context.SessionId);

        if (args.Updates.ValueKind == JsonValueKind.Object)
        {
            var updates = ConvertJsonToObjectMap(args.Updates);
            await _storyState.MergeAsync(path, updates, ct);
        }

        if (args.IncrementCounter)
        {
            await _storyState.IncrementCounterAsync(path, "_event_counter", ct);
        }

        return ToolResult.Ok("State updated.");
    }

    private static IReadOnlyDictionary<string, object> ConvertJsonToObjectMap(JsonElement element)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = ConvertJsonValue(property.Value);
        }
        return values;
    }

    private static object ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
        JsonValueKind.Object => ConvertJsonToObjectMap(element),
        _ => element.ToString() ?? string.Empty,
    };
}

public sealed record UpdateStoryStateArgs
{
    [JsonPropertyName("updates")]
    public JsonElement Updates { get; init; }

    [JsonPropertyName("increment_counter")]
    public bool IncrementCounter { get; init; }
}
