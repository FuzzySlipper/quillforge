using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Long-form project writing mode with accept/reject workflow.
/// Stateless — writer pending state lives in WriterRuntimeState.
/// </summary>
public sealed class WriterMode : IMode
{
    private readonly ILogger<WriterMode> _logger;

    public WriterMode(ILogger<WriterMode> logger)
    {
        _logger = logger;
    }

    public string Name => "writer";

    public string BuildSystemPromptSection(ModeContext context)
    {
        var pendingNote = !string.IsNullOrEmpty(context.WriterPendingContent)
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
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if a response should be captured as pending content and update state.
    /// </summary>
    public static void CaptureIfPending(AgentResponse response, WriterRuntimeState writer, ILogger logger)
    {
        var text = response.Content.GetText();
        if (!string.IsNullOrWhiteSpace(text) && text.Length > 200 && writer.State == WriterState.Idle)
        {
            writer.PendingContent = text;
            writer.State = WriterState.PendingReview;
            logger.LogDebug("WriterMode: content pending review ({Length} chars)", text.Length);
        }
    }

    /// <summary>
    /// User accepts the pending content.
    /// </summary>
    public static string? Accept(WriterRuntimeState writer, ILogger logger)
    {
        if (writer.State != WriterState.PendingReview || writer.PendingContent is null)
        {
            logger.LogWarning("WriterMode: Accept called but no content pending");
            return null;
        }

        var content = writer.PendingContent;
        writer.PendingContent = null;
        writer.State = WriterState.Idle;
        logger.LogInformation("WriterMode: content accepted ({Length} chars)", content.Length);
        return content;
    }

    /// <summary>
    /// User rejects the pending content.
    /// </summary>
    public static void Reject(WriterRuntimeState writer, ILogger logger)
    {
        if (writer.State != WriterState.PendingReview)
        {
            logger.LogWarning("WriterMode: Reject called but no content pending");
            return;
        }

        logger.LogInformation("WriterMode: content rejected");
        writer.PendingContent = null;
        writer.State = WriterState.Idle;
    }

    /// <summary>
    /// Reset writer state (e.g., on mode switch).
    /// </summary>
    public static void Reset(WriterRuntimeState writer)
    {
        writer.PendingContent = null;
        writer.State = WriterState.Idle;
    }
}
