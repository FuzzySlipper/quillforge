using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Forge Reviewer scores chapters on a rubric and provides actionable feedback.
/// No tools — simple prompt-in, structured-JSON-out.
/// </summary>
public sealed class ForgeReviewerAgent
{
    private readonly ICompletionService _completionService;
    private readonly ILogger<ForgeReviewerAgent> _logger;

    /// <summary>
    /// Weighted scoring rubric. Overall = weighted average.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> ScoreWeights = new Dictionary<string, double>
    {
        ["continuity"] = 0.3,
        ["brief_adherence"] = 0.3,
        ["voice_consistency"] = 0.2,
        ["quality"] = 0.2,
    };

    public double PassThreshold { get; init; } = 7.0;

    public ForgeReviewerAgent(ICompletionService completionService, ILogger<ForgeReviewerAgent> logger)
    {
        _completionService = completionService;
        _logger = logger;
    }

    /// <summary>
    /// Reviews a chapter draft and returns scores with feedback.
    /// </summary>
    public async Task<ReviewResult> ReviewAsync(
        string chapterDraft,
        string chapterBrief,
        string styleDoc,
        string? previousChapterTail,
        string? customPrompt = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ForgeReviewer starting review ({DraftLength} chars)", chapterDraft.Length);

        var systemPrompt = customPrompt ?? DefaultReviewerPrompt;
        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(styleDoc))
            contextParts.Add($"## Style Guide\n\n{styleDoc}");
        if (!string.IsNullOrWhiteSpace(previousChapterTail))
            contextParts.Add($"## Previous Chapter Ending\n\n{previousChapterTail}");
        contextParts.Add($"## Chapter Brief\n\n{chapterBrief}");
        contextParts.Add($"## Chapter Draft\n\n{chapterDraft}");

        var userMessage = string.Join("\n\n", contextParts);

        var request = new CompletionRequest
        {
            Model = "default",
            MaxTokens = 4096,
            SystemPrompt = systemPrompt,
            Messages = [new CompletionMessage("user", new MessageContent(userMessage))],
        };

        var response = await _completionService.CompleteAsync(request, ct);
        var responseText = response.Content.GetText();

        _logger.LogDebug("ForgeReviewer raw response: {Length} chars", responseText.Length);

        var result = ParseReviewResult(responseText);

        _logger.LogInformation(
            "ForgeReviewer result: overall={Overall:F1}, passed={Passed}, feedback length={FeedbackLength}",
            result.Overall, result.Passed, result.Feedback.Length);

        return result;
    }

    /// <summary>
    /// Parses the review response. Uses the same multi-stage JSON extraction as Librarian.
    /// </summary>
    internal ReviewResult ParseReviewResult(string responseText)
    {
        var text = responseText.Trim();

        if (TryParseScores(text, out var result)) return result;

        var stripped = LibrarianAgent.StripMarkdownFences(text);
        if (stripped != text && TryParseScores(stripped, out result)) return result;

        var extracted = LibrarianAgent.ExtractJsonObject(text);
        if (extracted is not null && TryParseScores(extracted, out result)) return result;

        _logger.LogWarning("ForgeReviewer: could not parse scores from response, returning failing result");
        return new ReviewResult
        {
            Continuity = 0,
            BriefAdherence = 0,
            VoiceConsistency = 0,
            Quality = 0,
            Overall = 0,
            Feedback = text,
            Passed = false,
        };
    }

    private bool TryParseScores(string json, out ReviewResult result)
    {
        result = default!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var continuity = GetDouble(root, "continuity");
            var briefAdherence = GetDouble(root, "brief_adherence");
            var voiceConsistency = GetDouble(root, "voice_consistency");
            var quality = GetDouble(root, "quality");
            var feedback = root.TryGetProperty("feedback", out var fb) ? fb.GetString() ?? "" : "";

            var overall =
                continuity * ScoreWeights["continuity"] +
                briefAdherence * ScoreWeights["brief_adherence"] +
                voiceConsistency * ScoreWeights["voice_consistency"] +
                quality * ScoreWeights["quality"];

            result = new ReviewResult
            {
                Continuity = continuity,
                BriefAdherence = briefAdherence,
                VoiceConsistency = voiceConsistency,
                Quality = quality,
                Overall = overall,
                Feedback = feedback,
                Passed = overall >= PassThreshold,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static double GetDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var el)) return 0;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0;
    }

    internal const string DefaultReviewerPrompt = """
        You are a meticulous fiction editor reviewing a chapter draft. Score the chapter on
        four dimensions (1-10 scale) and provide specific, actionable feedback.

        ## Scoring Rubric

        - **continuity** (weight 0.3): Lore consistency, timeline accuracy, no contradictions
          with established world-building.
        - **brief_adherence** (weight 0.3): All required plot beats are present, character arcs
          match the brief, no missing story elements.
        - **voice_consistency** (weight 0.2): Style matches the guide, POV and tense are consistent,
          tone is appropriate for the scene.
        - **quality** (weight 0.2): Show-don't-tell, varied sentence structure, natural dialogue,
          good pacing, emotional impact.

        Pass threshold: weighted overall >= 7.0

        Respond ONLY with a JSON object:
        {
          "continuity": <1-10>,
          "brief_adherence": <1-10>,
          "voice_consistency": <1-10>,
          "quality": <1-10>,
          "feedback": "specific actionable feedback here"
        }
        """;
}
