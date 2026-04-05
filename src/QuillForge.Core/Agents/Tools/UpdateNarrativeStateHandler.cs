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
public sealed class UpdateNarrativeStateHandler : TypedToolHandler<UpdateNarrativeStateArgs>
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

    public override string Name => "update_narrative_state";

    public override ToolDefinition Definition => new(
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

    protected override async Task<ToolResult> HandleTypedAsync(UpdateNarrativeStateArgs input, AgentContext context, CancellationToken ct = default)
    {
        var directorNotes = input.DirectorNotes;
        if (string.IsNullOrWhiteSpace(directorNotes))
        {
            return ToolResult.Fail("director_notes is required.");
        }

        var plotProgress = input.PlotProgress is null
            ? null
            : new PlotProgressUpdate(
                input.PlotProgress.CurrentBeat,
                input.PlotProgress.CompletedBeats,
                input.PlotProgress.Deviations);

        var result = await _runtimeService.UpdateNarrativeStateAsync(
            context.SessionId,
            new UpdateNarrativeStateCommand(directorNotes, input.ActivePlotFile, plotProgress),
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
}

public sealed record UpdateNarrativeStateArgs
{
    public string DirectorNotes { get; init; } = "";
    public string? ActivePlotFile { get; init; }
    public NarrativePlotProgressArgs? PlotProgress { get; init; }
}

public sealed record NarrativePlotProgressArgs
{
    public string? CurrentBeat { get; init; }
    public IReadOnlyList<string>? CompletedBeats { get; init; }
    public IReadOnlyList<string>? Deviations { get; init; }
}
