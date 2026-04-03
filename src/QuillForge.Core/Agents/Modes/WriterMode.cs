using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Long-form project writing mode with accept/reject workflow.
/// Stateless — writer pending state lives in WriterRuntimeState.
/// </summary>
public sealed class WriterMode : IMode
{
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
}
