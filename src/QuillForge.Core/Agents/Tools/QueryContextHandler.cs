using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Source-aware context lookup across prepared session context and active lore
/// documents. This complements query_lore, which remains the semantic Librarian
/// interface for the lore document corpus only.
/// </summary>
public sealed class QueryContextHandler : TypedToolHandler<QueryContextArgs>
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ILoreStore _loreStore;
    private readonly ILogger<QueryContextHandler> _logger;

    public QueryContextHandler(
        IInteractiveSessionContextService sessionContextService,
        ILoreStore loreStore,
        ILogger<QueryContextHandler> logger)
    {
        _sessionContextService = sessionContextService;
        _loreStore = loreStore;
        _logger = logger;
    }

    public override string Name => "query_context";

    public override ToolDefinition Definition => new(Name,
        "Search the current grounded context with source labels. Includes selected character card context, sticky session canon, plot/story state, recent conversation, file context, and optionally active lore documents.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Fact, entity, or topic to look for in the current context"
                    },
                    "include_lore_documents": {
                        "type": "boolean",
                        "description": "Whether to include active lore document matches. Defaults to true."
                    },
                    "max_results": {
                        "type": "integer",
                        "description": "Maximum result count, from 1 to 12. Defaults to 8."
                    }
                },
                "required": ["query"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(QueryContextArgs input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.Query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("query is required.");
        }

        var maxResults = Math.Clamp(input.MaxResults ?? 8, 1, 12);
        var includeLoreDocuments = input.IncludeLoreDocuments ?? true;
        var tokens = Tokenize(query);
        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        var results = new List<ContextSourceMatch>();

        AddSessionContextMatches(results, query, tokens, sessionContext);

        // Detect active subject for roleplay protocol enrichment
        var activeSubject = ResolveActiveSubject(context);

        if (includeLoreDocuments && !string.IsNullOrWhiteSpace(context.ActiveLoreSet))
        {
            await AddLoreDocumentMatchesAsync(results, query, tokens, context.ActiveLoreSet, activeSubject, ct);
        }

        var ordered = results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.SourceId, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();

        _logger.LogDebug(
            "QueryContextHandler: query=\"{Query}\" session={SessionId} results={Count}",
            query,
            context.SessionId,
            ordered.Count);

        // Build structured knowledge packet if active subject is known
        RoleplayKnowledgePacket? structuredPacket = null;
        if (activeSubject is not null)
        {
            var evidenceItems = new List<RoleplayEvidenceItem>();
            foreach (var result in ordered)
            {
                if (result.Applicability.HasValue && result.AllowedUse.HasValue)
                {
                    evidenceItems.Add(new RoleplayEvidenceItem
                    {
                        Passage = result.Snippet,
                        Applicability = result.Applicability.Value,
                        AllowedUse = result.AllowedUse.Value,
                        SourceRefs =
                        [
                            new RoleplaySourceRef
                            {
                                SourcePath = result.SourceId,
                                SourceKind = MapMatchSourceKind(result.SourceType),
                                Title = result.Title,
                            },
                        ],
                    });
                }
            }

            if (evidenceItems.Count > 0)
            {
                structuredPacket = new RoleplayKnowledgePacket
                {
                    Query = query,
                    ActiveSubject = activeSubject,
                    Scope = evidenceItems.Any(e => e.Applicability == ActiveSubjectApplicability.Applies)
                        ? RoleplayKnowledgeScope.CharacterSpecific
                        : RoleplayKnowledgeScope.SharedWorld,
                    Evidence = evidenceItems,
                    SourceComponent = "query_context",
                };
            }
        }

        var payload = new QueryContextResult
        {
            Query = query,
            ActiveLoreSet = context.ActiveLoreSet,
            Results = ordered,
            ActiveSubject = activeSubject,
            StructuredPacket = structuredPacket,
            Note = ordered.Count == 0
                ? "No matching context sources were found. query_lore may still be useful for semantic lore-document lookup."
                : null,
        };

        return ToolResult.Ok(JsonSerializer.Serialize(payload, s_jsonOptions));
    }

    private static void AddSessionContextMatches(
        List<ContextSourceMatch> results,
        string query,
        IReadOnlyList<string> tokens,
        InteractiveSessionContext context)
    {
        AddMatch(results, "character_card", context.Character ?? "selected-character", "Selected Character Card", context.CharacterSection, query, tokens, includeWhenAskedForSource: ["character", "card", "persona"]);
        AddMatch(results, "user_character_card", context.UserCharacter ?? "selected-user-character", "Selected User Character Card", context.UserCharacterSection, query, tokens, includeWhenAskedForSource: ["user", "character", "card", "persona"]);
        AddMatch(results, "story_state", context.StoryStatePath, "Current Story State", context.StoryStateSummary, query, tokens, includeWhenAskedForSource: ["story", "state"]);
        AddMatch(results, "session_canon", "sticky-session-canon", "Sticky Session Canon", context.StickySessionCanon, query, tokens, includeWhenAskedForSource: ["canon", "session", "sticky"]);
        AddMatch(results, "director_notes", "director-notes", "Director Notes", context.DirectorNotes, query, tokens, includeWhenAskedForSource: ["director", "notes"]);
        AddMatch(results, "recent_conversation", "recent-conversation", "Recent Conversation", context.RecentConversationSummary, query, tokens, includeWhenAskedForSource: ["conversation", "recent", "chat"]);
        AddMatch(results, "plot_state", context.ActivePlotFile ?? "active-plot", "Active Plot Content", context.ActivePlotContent, query, tokens, includeWhenAskedForSource: ["plot", "arc", "beat"]);
        AddMatch(results, "plot_state", "plot-progress", "Plot Progress", context.PlotProgressSummary, query, tokens, includeWhenAskedForSource: ["plot", "progress", "beat"]);
        AddMatch(results, "file_context", context.CurrentFile ?? "current-file", "Recent File Context", context.FileContext, query, tokens, includeWhenAskedForSource: ["file", "scene", "chapter"]);
    }

    private async Task AddLoreDocumentMatchesAsync(
        List<ContextSourceMatch> results,
        string query,
        IReadOnlyList<string> tokens,
        string loreSet,
        string? activeSubject,
        CancellationToken ct)
    {
        var loreFiles = await _loreStore.LoadLoreSetAsync(loreSet, ct);
        foreach (var (filePath, content) in loreFiles)
        {
            var applicability = activeSubject is not null
                ? RoleplayApplicabilityClassifier.Classify(content, activeSubject, filePath)
                : (ActiveSubjectApplicability?)null;

            var allowedUse = applicability.HasValue
                ? RoleplayApplicabilityClassifier.ClassifyAllowedUse(applicability.Value)
                : (AllowedUse?)null;

            AddMatch(results, "lore_document", filePath, $"Lore Document: {filePath}", content, query, tokens,
                applicability: applicability, allowedUse: allowedUse);
        }
    }

    private static void AddMatch(
        List<ContextSourceMatch> results,
        string sourceType,
        string sourceId,
        string title,
        string? content,
        string query,
        IReadOnlyList<string> tokens,
        IReadOnlyList<string>? includeWhenAskedForSource = null,
        ActiveSubjectApplicability? applicability = null,
        AllowedUse? allowedUse = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var score = Score(content, query, tokens);
        if (score == 0 && includeWhenAskedForSource is not null && tokens.Any(token => includeWhenAskedForSource.Contains(token, StringComparer.OrdinalIgnoreCase)))
        {
            score = 1;
        }

        if (score == 0)
        {
            return;
        }

        results.Add(new ContextSourceMatch
        {
            SourceType = sourceType,
            SourceId = sourceId,
            Title = title,
            Snippet = BuildSnippet(content, query, tokens),
            Score = score,
            Applicability = applicability,
            AllowedUse = allowedUse,
        });
    }

    private static int Score(string content, string query, IReadOnlyList<string> tokens)
    {
        var normalized = content.ToLowerInvariant();
        var score = 0;

        if (!string.IsNullOrWhiteSpace(query) && normalized.Contains(query.ToLowerInvariant(), StringComparison.Ordinal))
        {
            score += Math.Max(tokens.Count, 1) + 2;
        }

        foreach (var token in tokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                score++;
            }
        }

        return score;
    }

    private static string BuildSnippet(string content, string query, IReadOnlyList<string> tokens)
    {
        var normalized = content.ReplaceLineEndings("\n").Trim();
        if (normalized.Length <= 700)
        {
            return normalized;
        }

        var lower = normalized.ToLowerInvariant();
        var queryIndex = lower.IndexOf(query.ToLowerInvariant(), StringComparison.Ordinal);
        var tokenIndex = queryIndex >= 0
            ? queryIndex
            : tokens
                .Select(token => lower.IndexOf(token, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(0)
                .Min();

        var start = Math.Max(0, tokenIndex - 250);
        var end = Math.Min(normalized.Length, start + 700);
        var snippet = normalized[start..end].Trim();
        if (start > 0)
        {
            snippet = "..." + snippet;
        }

        if (end < normalized.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static IReadOnlyList<string> Tokenize(string query)
    {
        return query
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Infer the active subject/character from agent context, if available.
    /// </summary>
    private static string? ResolveActiveSubject(AgentContext context)
    {
        if (context.SessionContext?.Character is { Length: > 0 } character)
            return character;

        return null;
    }

    /// <summary>
    /// Map a source type string to a SubjectSourceKind for protocol enrichment.
    /// </summary>
    private static SubjectSourceKind MapMatchSourceKind(string sourceType)
    {
        return sourceType.ToLowerInvariant() switch
        {
            "lore_document" => SubjectSourceKind.Unknown,
            "character_card" => SubjectSourceKind.CharacterFile,
            "user_character_card" => SubjectSourceKind.CharacterFile,
            "story_state" => SubjectSourceKind.EventFile,
            "session_canon" => SubjectSourceKind.SessionCanon,
            "director_notes" => SubjectSourceKind.Correction,
            "plot_state" => SubjectSourceKind.EventFile,
            "file_context" => SubjectSourceKind.LocationFile,
            _ => SubjectSourceKind.Unknown,
        };
    }
}

public sealed record QueryContextArgs
{
    public string Query { get; init; } = "";
    public bool? IncludeLoreDocuments { get; init; }
    public int? MaxResults { get; init; }
}

public sealed record QueryContextResult
{
    public required string Query { get; init; }
    public required string ActiveLoreSet { get; init; }
    public required IReadOnlyList<ContextSourceMatch> Results { get; init; }
    public string? Note { get; init; }

    /// <summary>Active subject inferred from context, if available.</summary>
    public string? ActiveSubject { get; init; }

    /// <summary>Structured knowledge packet with applicability classification, if active subject is known.</summary>
    public RoleplayKnowledgePacket? StructuredPacket { get; init; }
}

public sealed record ContextSourceMatch
{
    public required string SourceType { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Snippet { get; init; }
    public required int Score { get; init; }

    /// <summary>Applicability classification for roleplay protocol, if active subject is known.</summary>
    public ActiveSubjectApplicability? Applicability { get; init; }

    /// <summary>Allowed-use classification for roleplay protocol, if active subject is known.</summary>
    public AllowedUse? AllowedUse { get; init; }
}
