using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Long-form project writing mode with accept/reject workflow.
/// Generated prose is held as "pending" until the user accepts it,
/// at which point it's written to the story file.
/// </summary>
public sealed class WriterMode : IMode
{
    private readonly ILogger<WriterMode> _logger;

    public WriterMode(ILogger<WriterMode> logger)
    {
        _logger = logger;
    }

    public string Name => "writer";

    /// <summary>
    /// The currently pending prose content awaiting user acceptance.
    /// Null when no content is pending.
    /// </summary>
    public string? PendingContent { get; private set; }

    public WriterState State { get; private set; } = WriterState.Idle;

    public string BuildSystemPromptSection(ModeContext context)
    {
        var pendingNote = PendingContent is not null
            ? "\n\nThere is pending content awaiting user review. Wait for them to accept, reject, or request changes."
            : "";

        return $"""
            ## Current Mode: Writer

            You are in long-form writing mode for project "{context.ProjectName ?? "untitled"}".
            Current file: {context.CurrentFile ?? "none"}

            Workflow:
            1. Use write_prose to generate content based on the user's request
            2. Present the generated text to the user
            3. IMPORTANT: Do NOT write to the file until the user accepts the content
            4. Wait for the user to accept, reject, or request modifications
            5. Only after acceptance, use write_file to save the content

            {context.FileContext ?? ""}{pendingNote}
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        // Check if the response contains prose that should be held as pending
        var text = response.Content.GetText();
        if (!string.IsNullOrWhiteSpace(text) && text.Length > 200 && State == WriterState.Idle)
        {
            PendingContent = text;
            State = WriterState.PendingReview;
            _logger.LogDebug("WriterMode: content pending review ({Length} chars)", text.Length);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// User accepts the pending content.
    /// </summary>
    public string? Accept()
    {
        if (State != WriterState.PendingReview || PendingContent is null)
        {
            _logger.LogWarning("WriterMode: Accept called but no content pending");
            return null;
        }

        var content = PendingContent;
        PendingContent = null;
        State = WriterState.Idle;
        _logger.LogInformation("WriterMode: content accepted ({Length} chars)", content.Length);
        return content;
    }

    /// <summary>
    /// User rejects the pending content.
    /// </summary>
    public void Reject()
    {
        if (State != WriterState.PendingReview)
        {
            _logger.LogWarning("WriterMode: Reject called but no content pending");
            return;
        }

        _logger.LogInformation("WriterMode: content rejected");
        PendingContent = null;
        State = WriterState.Idle;
    }

    /// <summary>
    /// Reset state (e.g., on mode switch).
    /// </summary>
    public void Reset()
    {
        PendingContent = null;
        State = WriterState.Idle;
    }
}

public enum WriterState
{
    Idle,
    PendingReview,
}
