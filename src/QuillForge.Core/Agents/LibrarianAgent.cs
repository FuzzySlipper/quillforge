using System.Text.Json;
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
    private readonly ILogger<LibrarianAgent> _logger;
    private readonly string _model;

    public LibrarianAgent(ToolLoop toolLoop, ILoreStore loreStore, AppConfig appConfig, ILogger<LibrarianAgent> logger)
    {
        _toolLoop = toolLoop;
        _loreStore = loreStore;
        _logger = logger;
        _model = appConfig.Models.Librarian;
    }

    /// <summary>
    /// Queries the lore corpus and returns structured results.
    /// </summary>
    public async Task<LoreBundle> QueryAsync(
        string query,
        string loreSetName,
        AgentContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Librarian query: \"{Query}\" against lore set \"{LoreSet}\"", query, loreSetName);

        var loreContent = await _loreStore.LoadLoreSetAsync(loreSetName, ct);
        var systemPrompt = BuildSystemPrompt(loreContent);

        _logger.LogInformation("Librarian using model {Model}", _model);

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = 4096,
            SystemPrompt = systemPrompt,
            MaxToolRounds = 1,
            CacheSystemPrompt = true,  // Lore corpus is large and stable; cache to save input tokens
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
            "Librarian returned {PassageCount} passages, confidence={Confidence}",
            bundle.RelevantPassages.Count, bundle.Confidence);

        return bundle;
    }

    internal static string BuildSystemPrompt(IReadOnlyDictionary<string, string> loreContent)
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

        return $"""
            You are the Librarian, a precise lore retrieval specialist. Your ONLY job is to find and return
            relevant information from the lore corpus below. Follow these rules strictly:

            1. ONLY return information that exists in the lore corpus. Never invent or extrapolate.
            2. Include source file attribution for every passage you return.
            3. Rate your confidence: "high" if the lore directly answers the query, "medium" if partially
               relevant, "low" if only tangentially related.
            4. If the lore contains no relevant information, return empty passages with "low" confidence.

            Respond ONLY with a JSON object in this exact format:
            {jsonExample}

            ## Lore Corpus

            {joinedLore}
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
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var passages = root.TryGetProperty("relevant_passages", out var passagesEl)
                ? passagesEl.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];

            var sources = root.TryGetProperty("source_files", out var sourcesEl)
                ? sourcesEl.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : [];

            var confidence = LoreConfidence.High;
            if (root.TryGetProperty("confidence", out var confEl))
            {
                var confStr = confEl.GetString() ?? "high";
                confidence = confStr.ToLowerInvariant() switch
                {
                    "high" => LoreConfidence.High,
                    "medium" => LoreConfidence.Medium,
                    "low" => LoreConfidence.Low,
                    _ => LoreConfidence.High,
                };
            }

            bundle = new LoreBundle
            {
                RelevantPassages = passages,
                SourceFiles = sources,
                Confidence = confidence,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
