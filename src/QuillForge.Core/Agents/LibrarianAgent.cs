using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Librarian agent loads the lore corpus into its system prompt and answers
/// structured queries with provenance. Returns a LoreBundle with relevant passages,
/// source files, and confidence.
/// </summary>
public sealed class LibrarianAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly ILoreStore _loreStore;
    private readonly ILibrarianPromptStore _promptStore;
    private readonly ILogger<LibrarianAgent> _logger;
    private readonly AppConfig _appConfig;

    public LibrarianAgent(ToolLoop toolLoop, ILoreStore loreStore, ILibrarianPromptStore promptStore, AppConfig appConfig, ILogger<LibrarianAgent> logger)
    {
        _toolLoop = toolLoop;
        _loreStore = loreStore;
        _promptStore = promptStore;
        _logger = logger;
        _appConfig = appConfig;
    }

    /// <summary>
    /// Queries the lore corpus and returns structured results.
    /// When supplementalLore is provided (e.g. run-specific lore from forge),
    /// it is included alongside the main lore corpus.
    /// </summary>
    public async Task<LibrarianResult> QueryAsync(
        string query,
        string loreSetName,
        AgentContext context,
        string? supplementalLore = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Librarian query: \"{Query}\" against lore set \"{LoreSet}\"", query, loreSetName);

        var loreContent = await _loreStore.LoadLoreSetAsync(loreSetName, ct);
        var userInstructions = await _promptStore.LoadAsync(context.LibrarianPrompt, ct);
        _logger.LogDebug("Librarian using prompt \"{PromptName}\" ({Length} chars)", context.LibrarianPrompt, userInstructions.Length);
        var systemPrompt = BuildSystemPrompt(loreContent, loreSetName, userInstructions, supplementalLore);

        var budget = _appConfig.Agents.Librarian;
        var model = _appConfig.Models.Librarian;
        _logger.LogInformation("Librarian using model {Model}", model);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = budget.MaxToolRounds,
            CacheSystemPrompt = budget.CacheSystemPrompt,
            AgentName = "librarian",
        };

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(query)),
        };

        var response = await _toolLoop.RunAsync(config, [], messages, context, ct);
        var responseText = response.Content.GetText();

        _logger.LogDebug("Librarian raw response: {ResponseLength} chars", responseText.Length);

        var bundle = ParseLoreBundle(responseText);

        _logger.LogInformation(
            "Librarian returned {PassageCount} passages, confidence={Confidence}, tokens={Tokens}",
            bundle.RelevantPassages.Count, bundle.Confidence, response.Usage.TotalTokens);

        return new LibrarianResult { Bundle = bundle, Usage = response.Usage };
    }

    internal static string BuildSystemPrompt(
        IReadOnlyDictionary<string, string> loreContent,
        string loreSetName,
        string? userInstructions = null,
        string? supplementalLore = null)
    {
        var sections = loreContent
            .Select(kvp => $"### File: {kvp.Key}\n\n{kvp.Value}")
            .ToList();

        var joinedLore = string.Join("\n\n---\n\n", sections);

        var jsonExample = """
            {
              "relevant_passages": ["passage 1", "passage 2"],
              "source_files": ["path/to/file1.md", "path/to/file2.md"],
              "confidence": "high"
            }
            """;

        var supplementalSection = string.IsNullOrWhiteSpace(supplementalLore)
            ? ""
            : $"""

            ## Run Lore (details from this writing run)

            {supplementalLore}
            """;

        var userInstructionsSection = string.IsNullOrWhiteSpace(userInstructions)
            ? ""
            : $"""

            ## Additional Instructions

            {userInstructions}
            """;

        return $"""
            You are the Librarian, a precise lore retrieval specialist working with the "{loreSetName}" lore set.
            Your ONLY job is to find and return relevant information from this lore corpus. Follow these rules strictly:

            1. ONLY return information that exists in this lore corpus or run lore. Never invent or extrapolate.
            2. Include source file attribution for every passage you return.
               For run lore entries, use "run-lore" as the source file.
            3. Rate your confidence: "high" if the lore directly answers the query, "medium" if partially
               relevant, "low" if only tangentially related.
            4. If the lore contains no relevant information, return empty passages with "low" confidence.
            {userInstructionsSection}

            Respond ONLY with a JSON object in this exact format:
            {jsonExample}

            ## Lore Corpus ({loreSetName})

            {joinedLore}
            {supplementalSection}
            """;
    }

    /// <summary>
    /// Parses the LLM response into a LoreBundle. Uses multi-stage fallback:
    /// direct JSON → markdown fence strip → balanced brace extraction → raw text fallback.
    /// </summary>
    internal static LoreBundle ParseLoreBundle(string responseText)
    {
        var text = responseText.Trim();

        // Try direct JSON parse
        if (TryParseJson(text, out var bundle))
        {
            return bundle;
        }

        // Try stripping markdown code fences
        var stripped = StripMarkdownFences(text);
        if (stripped != text && TryParseJson(stripped, out bundle))
        {
            return bundle;
        }

        // Try extracting balanced braces
        var extracted = ExtractJsonObject(text);
        if (extracted is not null && TryParseJson(extracted, out bundle))
        {
            return bundle;
        }

        // Fallback: treat the entire response as a single passage
        return new LoreBundle
        {
            RelevantPassages = string.IsNullOrWhiteSpace(text) ? [] : [text],
            SourceFiles = [],
            Confidence = LoreConfidence.Low,
        };
    }

    private static bool TryParseJson(string json, out LoreBundle bundle)
    {
        bundle = default!;
        if (!StructuredJsonParser.TryParse<LoreBundleDto>(json, out var dto))
        {
            return false;
        }
        var parsed = dto!;

        var confidence = (parsed.Confidence ?? "high").ToLowerInvariant() switch
        {
            "high" => LoreConfidence.High,
            "medium" => LoreConfidence.Medium,
            "low" => LoreConfidence.Low,
            _ => LoreConfidence.High,
        };

        bundle = new LoreBundle
        {
            RelevantPassages = parsed.RelevantPassages?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [],
            SourceFiles = parsed.SourceFiles?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [],
            Confidence = confidence,
        };
        return true;
    }

    private sealed record LoreBundleDto(
        [property: JsonPropertyName("relevant_passages")] IReadOnlyList<string>? RelevantPassages,
        [property: JsonPropertyName("source_files")] IReadOnlyList<string>? SourceFiles,
        string? Confidence);

    internal static string StripMarkdownFences(string text)
    {
        var lines = text.Split('\n');
        var inFence = false;
        var content = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                content.Add(line);
            }
        }

        return content.Count > 0 ? string.Join('\n', content).Trim() : text;
    }

    internal static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;

            if (depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        return null;
    }
}
