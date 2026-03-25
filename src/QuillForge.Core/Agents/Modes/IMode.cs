using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Defines mode-specific behavior for the Orchestrator. Each mode provides
/// a system prompt section and response post-processing.
/// All tools are available in all modes (behavior differences are in the prompts).
/// </summary>
public interface IMode
{
    string Name { get; }

    /// <summary>
    /// The mode-specific section appended to the Orchestrator's system prompt.
    /// </summary>
    string BuildSystemPromptSection(ModeContext context);

    /// <summary>
    /// Called after the Orchestrator produces a response. Modes can perform
    /// post-processing (e.g., WriterMode stores pending content).
    /// </summary>
    Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default);
}

/// <summary>
/// Context available to modes for prompt building and response handling.
/// </summary>
public sealed record ModeContext
{
    public string? ProjectName { get; init; }
    public string? CurrentFile { get; init; }
    public string? FileContext { get; init; }
    public string? StoryStateSummary { get; init; }
    public string? CharacterSection { get; init; }
}
