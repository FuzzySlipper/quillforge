using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Promotes a user OOC correction into sticky session canon so that subsequent
/// turns (including fallback turns when direct_scene is unavailable) preserve it.
/// This is allowed at the top level in Roleplay and Writer modes because it is
/// a narrow, user-driven write to narrative runtime state.
/// </summary>
public sealed class RecordSessionCorrectionHandler : TypedToolHandler<RecordSessionCorrectionArgs>
{
    private readonly ISessionStateService _runtimeService;
    private readonly ILogger<RecordSessionCorrectionHandler> _logger;

    public RecordSessionCorrectionHandler(
        ISessionStateService runtimeService,
        ILogger<RecordSessionCorrectionHandler> logger)
    {
        _runtimeService = runtimeService;
        _logger = logger;
    }

    public override string Name => "record_session_correction";

    public override ToolDefinition Definition => new(
        Name,
        "Record a user OOC correction into sticky session canon so it persists for subsequent turns. Use this when the user corrects canon, characterization, relationships, locations, or timeline details.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "correction_text": {
                        "type": "string",
                        "description": "The corrected fact, exactly as the user stated or as you understood it"
                    },
                    "fact_category": {
                        "type": "string",
                        "description": "Optional category: characterization, relationship, location, timeline, or other"
                    }
                },
                "required": ["correction_text"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(RecordSessionCorrectionArgs input, AgentContext context, CancellationToken ct = default)
    {
        var correctionText = input.CorrectionText?.Trim();
        if (string.IsNullOrWhiteSpace(correctionText))
        {
            return ToolResult.Fail("correction_text is required.");
        }

        var view = await _runtimeService.LoadViewAsync(context.SessionId, ct);
        var existingCanon = view.Narrative.StickySessionCanon ?? "";
        var existingNotes = view.Narrative.DirectorNotes ?? "";

        var categoryPrefix = string.IsNullOrWhiteSpace(input.FactCategory)
            ? "[Correction]"
            : $"[{input.FactCategory.Trim()} correction]";

        var newCanonLine = $"{categoryPrefix} {correctionText}";
        var updatedCanon = string.IsNullOrWhiteSpace(existingCanon)
            ? $"- {newCanonLine}"
            : $"{existingCanon.TrimEnd()}\n- {newCanonLine}";

        var updatedNotes = string.IsNullOrWhiteSpace(existingNotes)
            ? "User correction recorded."
            : $"{existingNotes.TrimEnd()}\nUser correction recorded: {correctionText}";

        var result = await _runtimeService.UpdateNarrativeStateAsync(
            context.SessionId,
            new UpdateNarrativeStateCommand(updatedNotes, updatedCanon),
            ct);

        if (result.Status == SessionMutationStatus.Busy)
        {
            _logger.LogWarning(
                "Session correction skipped because the session was busy: session={SessionId}",
                context.SessionId);
            return ToolResult.Fail(result.Error ?? "Session is busy.");
        }

        if (result.Status == SessionMutationStatus.Invalid)
        {
            return ToolResult.Fail(result.Error ?? "Correction was rejected.");
        }

        _logger.LogInformation(
            "Session correction recorded for session {SessionId}: category={Category} correction={Correction}",
            context.SessionId,
            input.FactCategory ?? "unspecified",
            correctionText);

        return ToolResult.Ok($"Correction recorded in sticky session canon: {newCanonLine}");
    }
}

public sealed record RecordSessionCorrectionArgs
{
    public string CorrectionText { get; init; } = "";
    public string? FactCategory { get; init; }
}
