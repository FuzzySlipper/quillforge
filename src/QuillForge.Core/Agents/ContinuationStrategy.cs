using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Agents;

/// <summary>
/// Handles auto-continuation when a completion stops due to max_tokens.
/// Detects truncation, builds a continuation prompt, and merges the partial text.
/// </summary>
public sealed class ContinuationStrategy
{
    private readonly ILogger<ContinuationStrategy> _logger;

    public ContinuationStrategy(ILogger<ContinuationStrategy> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns true if the response was truncated by the token limit.
    /// </summary>
    public bool ShouldContinue(CompletionResponse response)
    {
        return string.Equals(response.StopReason, "max_tokens", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a continuation message to append to the conversation,
    /// asking the model to continue from where it left off.
    /// </summary>
    public CompletionMessage BuildContinuationMessage(CompletionResponse partialResponse)
    {
        var partialText = partialResponse.Content.GetText();
        _logger.LogDebug(
            "Building continuation for truncated response ({Length} chars)",
            partialText.Length);

        return new CompletionMessage("user", new MessageContent("Continue from where you left off."));
    }

    /// <summary>
    /// Merges the text from an original truncated response and a continuation response.
    /// </summary>
    public MessageContent MergeResponses(CompletionResponse original, CompletionResponse continuation)
    {
        var originalText = original.Content.GetText();
        var continuationText = continuation.Content.GetText();

        _logger.LogDebug(
            "Merging continuation: original {OriginalLength} chars + continuation {ContinuationLength} chars",
            originalText.Length, continuationText.Length);

        return new MessageContent(originalText + continuationText);
    }

    /// <summary>
    /// Aggregates token usage across multiple continuation rounds.
    /// </summary>
    public TokenUsage AggregateUsage(TokenUsage first, TokenUsage second)
    {
        return new TokenUsage(
            first.InputTokens + second.InputTokens,
            first.OutputTokens + second.OutputTokens);
    }
}
