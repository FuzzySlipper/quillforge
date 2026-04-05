using System.Text.Json.Serialization;
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
    private readonly string _model;
    private readonly int _maxTokens;

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

    public ForgeReviewerAgent(ICompletionService completionService, AppConfig appConfig, ILogger<ForgeReviewerAgent> logger)
    {
        _completionService = completionService;
        _logger = logger;
        _model = appConfig.Models.ForgeReviewer;
        _maxTokens = appConfig.Agents.ForgeReviewer.MaxTokens;
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
            Model = _model,
            MaxTokens = _maxTokens,
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
        if (!StructuredJsonParser.TryParse<ReviewResultDto>(json, out var dto))
        {
            return false;
        }
        var parsed = dto!;

        var extractedDetails = parsed.ExtractedDetails?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList() ?? [];

        var overall =
            parsed.Continuity * ScoreWeights["continuity"] +
            parsed.BriefAdherence * ScoreWeights["brief_adherence"] +
            parsed.VoiceConsistency * ScoreWeights["voice_consistency"] +
            parsed.Quality * ScoreWeights["quality"];

        result = new ReviewResult
        {
            Continuity = parsed.Continuity,
            BriefAdherence = parsed.BriefAdherence,
            VoiceConsistency = parsed.VoiceConsistency,
            Quality = parsed.Quality,
            Overall = overall,
            Feedback = parsed.Feedback ?? "",
            Passed = overall >= PassThreshold,
            ExtractedDetails = extractedDetails,
        };
        return true;
    }

    private sealed record ReviewResultDto(
        double Continuity,
        [property: JsonPropertyName("brief_adherence")] double BriefAdherence,
        [property: JsonPropertyName("voice_consistency")] double VoiceConsistency,
        double Quality,
        string? Feedback,
        [property: JsonPropertyName("extracted_details")] IReadOnlyList<string>? ExtractedDetails);

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

        ## Detail Extraction

        Also extract small but important details from the chapter that future chapters need to
        stay consistent on. Focus on specifics that are easy to lose track of:
        - Physical descriptions (eye color, clothing, scars, etc.)
        - New character or place names introduced
        - Objects given, received, or described
        - Relationship changes or revelations
        - Timeline specifics (time of day, how many days have passed, etc.)
        - Promises, plans, or commitments characters made

        Respond ONLY with a JSON object:
        {
          "continuity": <1-10>,
          "brief_adherence": <1-10>,
          "voice_consistency": <1-10>,
          "quality": <1-10>,
          "feedback": "specific actionable feedback here",
          "extracted_details": ["detail 1", "detail 2", ...]
        }
        """;
}
