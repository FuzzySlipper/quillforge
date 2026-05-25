using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates lore queries to the Librarian agent.
/// Resolves the active lore set from AgentContext at call time,
/// not from a value captured at construction.
/// </summary>
public sealed class QueryLoreHandler : TypedToolHandler<QueryLoreArgs>
{
    private readonly LibrarianAgent _librarian;
    private readonly ILoreStore _loreStore;
    private readonly IContentFileService _fileService;
    private readonly CanonPrerequisiteGuard _canonGuard;
    private readonly ILogger<QueryLoreHandler> _logger;

    public QueryLoreHandler(
        LibrarianAgent librarian,
        ILoreStore loreStore,
        IContentFileService fileService,
        CanonPrerequisiteGuard canonGuard,
        ILogger<QueryLoreHandler> logger)
    {
        _librarian = librarian;
        _loreStore = loreStore;
        _fileService = fileService;
        _canonGuard = canonGuard;
        _logger = logger;
    }

    public override string Name => "query_lore";

    public override ToolDefinition Definition => new(Name,
        "Query the Librarian for facts from the active lore document corpus only. This does not search character cards, session canon, plot state, chat history, or web results.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Question to answer from lore documents in the active lore set"
                    }
                },
                "required": ["query"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(QueryLoreArgs input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Query is required.");
        }

        _logger.LogDebug("QueryLoreHandler: querying \"{Query}\" in lore set \"{LoreSet}\"", query, context.ActiveLoreSet);

        try
        {
            await _canonGuard.EnsureQueryableLoreAvailableAsync(context, "query lore", ct);
        }
        catch (CanonPrerequisiteException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        // Load run-specific lore if available (forge pipeline runs)
        string? runLore = null;
        if (!string.IsNullOrEmpty(context.RunLorePath))
        {
            try
            {
                runLore = await _fileService.ReadAsync(context.RunLorePath, ct);
            }
            catch (FileNotFoundException)
            {
                _logger.LogDebug("No run lore file at {Path}", context.RunLorePath);
            }
        }

        var sw = Stopwatch.StartNew();
        var result = await _librarian.QueryAsync(query, context.ActiveLoreSet, context, runLore, ct);
        sw.Stop();

        // Enrich bundle with structured roleplay protocol packet if an active
        // subject can be inferred from context (e.g. character card name).
        var bundle = result.Bundle;
        var activeSubject = ResolveActiveSubject(context);
        if (activeSubject is not null && bundle.RelevantPassages.Count > 0)
        {
            // Build off-character names using bidirectional logic matching
            // QueryContextHandler.BuildOffCharacterNames: when the NPC character
            // card (Character) and the user-played character (UserCharacter)
            // differ, each is an off-character for the other.
            var offCharacterNames = BuildOffCharacterNames(context, activeSubject);

            // Add diagnostic provenance: collect which source files back each passage
            // so the structured packet includes full traceability for suspicious facts.
            // Pass offCharacterNames as excludedSubjects too so ClassifyAllowedUse
            // can distinguish RejectForActiveSubject from OffSubjectEvidence.
            var diagnostics = bundle.RelevantPassages
                .Select((passage, i) =>
                {
                    var sourcePath = i < bundle.SourceFiles.Count ? bundle.SourceFiles[i] : null;
                    return RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                        passage,
                        activeSubject,
                        sourcePath,
                        offCharacterNames,
                        offCharacterNames);
                })
                .ToList();

            bundle = bundle with
            {
                StructuredPacket = LibrarianAgent.BuildStructuredPacket(
                    bundle,
                    query,
                    new RoleplayBuildContext(
                        activeSubject,
                        offCharacterNames?.ToList(),
                        "query_lore")),
                Diagnostics = diagnostics,
            };
        }

        // Report librarian token usage and real wall-clock latency to the forge stats
        // tracker (if this is a forge run). The ToolLoop only aggregates its own completion
        // rounds; the librarian's nested LLM call is invisible to it, so we report it
        // through the callback. Usage stays on LibrarianResult — only the Bundle is
        // serialized to the tool result.
        if (result.Usage.TotalTokens > 0)
        {
            context.OnNestedCompletion?.Invoke("librarian", result.Usage, sw.ElapsedMilliseconds);
        }

        return ToolResult.Ok(JsonSerializer.Serialize(bundle));
    }

    /// <summary>
    /// Infer the active subject/character from agent context, if available.
    /// Currently checks the SessionContext Character field (the selected character
    /// card name). May be extended to use an explicit ActiveSubject field in future.
    /// </summary>
    private static string? ResolveActiveSubject(AgentContext context)
    {
        // If the session has a character card, that's the active subject
        if (context.SessionContext?.Character is { Length: > 0 } character)
            return character;

        // Fallback: check if this is roleplay mode with character context
        if (context.ActiveMode == Mode.Roleplay && context.SessionContext?.CharacterSection is { Length: > 0 })
        {
            // Try to extract the character name from the first line of character section
            var firstLine = context.SessionContext.CharacterSection
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (firstLine is { Length: > 0 })
                return firstLine.Trim('#', ' ', ':', '-');
        }

        return null;
    }

    /// <summary>
    /// Build the set of off-character names — subjects that are distinctly NOT the
    /// active character. Uses the same bidirectional logic as
    /// <see cref="QueryContextHandler"/>: when the NPC character card (Character)
    /// and the user-played character (UserCharacter) differ, each is an
    /// off-character for the other. Returns null when there is no known
    /// off-character or no active subject.
    /// </summary>
    private static HashSet<string>? BuildOffCharacterNames(AgentContext context, string? activeSubject)
    {
        if (activeSubject is null)
            return null;

        HashSet<string>? names = null;

        // If the active character is the NPC card character, the user character
        // is a separate subject whose details should not leak
        if (context.SessionContext?.UserCharacter is { Length: > 0 } userChar &&
            !string.Equals(userChar, activeSubject, StringComparison.OrdinalIgnoreCase))
        {
            names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(userChar);
        }

        // If the active character is the user character, the selected NPC card
        // character is a separate subject
        if (context.SessionContext?.Character is { Length: > 0 } npcChar &&
            !string.Equals(npcChar, activeSubject, StringComparison.OrdinalIgnoreCase))
        {
            names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(npcChar);
        }

        return names;
    }
}

public sealed record QueryLoreArgs
{
    public string Query { get; init; } = "";
}
