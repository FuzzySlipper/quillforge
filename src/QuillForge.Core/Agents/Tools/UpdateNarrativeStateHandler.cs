using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Persists narrative-director session notes through the owned session runtime
/// boundary. This keeps narrative memory inside SessionRuntimeService rather
/// than ad hoc endpoint or agent plumbing.
/// </summary>
public sealed class UpdateNarrativeStateHandler : IToolHandler
{
    private readonly ISessionStateService _runtimeService;
    private readonly ILogger<UpdateNarrativeStateHandler> _logger;

    public UpdateNarrativeStateHandler(
        ISessionStateService runtimeService,
        ILogger<UpdateNarrativeStateHandler> logger)
    {
        _runtimeService = runtimeService;
        _logger = logger;
    }

    public string Name => "update_narrative_state";

    public ToolDefinition Definition => new(
        Name,
        "Update session-embedded narrative state with concise director notes and optional active plot file.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "director_notes": {
                        "type": "string",
                        "description": "Concise running notes for the narrative director to carry into the next turn"
                    },
                    "active_plot_file": {
                        "type": "string",
                        "description": "Optional active plot file name to keep associated with this session"
                    },
                    "plot_progress": {
                        "type": "object",
                        "properties": {
                            "current_beat": {
                                "type": "string",
                                "description": "The plot beat the session is currently working through"
                            },
                            "completed_beats": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Plot beats that have been completed in this session"
                            },
                            "deviations": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Meaningful ways the session diverged from the loaded plot"
                            }
                        }
                    }
                },
                "required": ["director_notes"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var directorNotes = input.GetProperty("director_notes").GetString();
        if (string.IsNullOrWhiteSpace(directorNotes))
        {
            return ToolResult.Fail("director_notes is required.");
        }

        var activePlotFile = input.TryGetProperty("active_plot_file", out var plotElement)
            ? plotElement.GetString()
            : null;
        PlotProgressUpdate? plotProgress = null;
        if (input.TryGetProperty("plot_progress", out var progressElement) && progressElement.ValueKind == JsonValueKind.Object)
        {
            plotProgress = new PlotProgressUpdate(
                progressElement.TryGetProperty("current_beat", out var currentBeatElement)
                    ? currentBeatElement.GetString()
                    : null,
                ReadStringArray(progressElement, "completed_beats"),
                ReadStringArray(progressElement, "deviations"));
        }

        var result = await _runtimeService.UpdateNarrativeStateAsync(
            context.SessionId,
            new UpdateNarrativeStateCommand(directorNotes, activePlotFile, plotProgress),
            ct);

        if (result.Status == SessionMutationStatus.Busy)
        {
            _logger.LogWarning(
                "Narrative state update skipped because the session was busy: session={SessionId}",
                context.SessionId);
            return ToolResult.Fail(result.Error ?? "Session is busy.");
        }

        if (result.Status == SessionMutationStatus.Invalid)
        {
            return ToolResult.Fail(result.Error ?? "Narrative state update rejected.");
        }

        _logger.LogDebug(
            "Narrative state updated for session {SessionId}",
            context.SessionId);

        return ToolResult.Ok("Narrative state updated.");
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value);
            }
        }

        return items;
    }
}
